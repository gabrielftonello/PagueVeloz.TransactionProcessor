using System.Data;
using System.Text.Json;
using MediatR;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PagueVeloz.TransactionProcessor.Application.Abstractions;
using PagueVeloz.TransactionProcessor.Application.Contracts.Transactions;
using PagueVeloz.TransactionProcessor.Application.IntegrationEvents;
using PagueVeloz.TransactionProcessor.Application.Transactions;
using PagueVeloz.TransactionProcessor.Domain;
using PagueVeloz.TransactionProcessor.Domain.Accounts;
using PagueVeloz.TransactionProcessor.Domain.Events;
using PagueVeloz.TransactionProcessor.Domain.Transactions;

namespace PagueVeloz.TransactionProcessor.Application.Commands.ProcessTransaction;

public sealed class ProcessTransactionHandler : IRequestHandler<ProcessTransactionCommand, TransactionResponse>
{
  private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

  private readonly IUnitOfWork _uow;
  private readonly IAccountRepository _accounts;
  private readonly ITransactionStore _txStore;
  private readonly ILedgerStore _ledger;
  private readonly IOutboxStore _outbox;
  private readonly IClock _clock;

  public ProcessTransactionHandler(
    IUnitOfWork uow,
    IAccountRepository accounts,
    ITransactionStore txStore,
    ILedgerStore ledger,
    IOutboxStore outbox,
    IClock clock)
  {
    _uow = uow;
    _accounts = accounts;
    _txStore = txStore;
    _ledger = ledger;
    _outbox = outbox;
    _clock = clock;
  }

  public async Task<TransactionResponse> Handle(ProcessTransactionCommand request, CancellationToken ct)
  {
    var req = request.Request;
    var op = OperationParser.Parse(req.Operation);
    var metadata = (IReadOnlyDictionary<string, object?>)(req.Metadata ?? new Dictionary<string, object?>());
    var transactionId = $"{req.ReferenceId}-PROCESSED";

    var existing = await _txStore.GetByReferenceIdAsync(req.ReferenceId, ct);
    if (existing is not null)
      return Map(existing);

    const int maxAttempts = 20;

    for (var attempt = 1; attempt <= maxAttempts; attempt++)
    {
      await using var tx = await _uow.BeginTransactionAsync(IsolationLevel.Serializable, ct);

      try
      {
        var now = _clock.UtcNow;

        PersistedTransaction persisted;
        AccountEvent? ledgerEvent = null;

        switch (op)
        {
          case OperationType.Credit:
          {
            var account = await _accounts.GetForUpdateAsync(req.AccountId, ct)
              ?? throw new DomainException($"Account '{req.AccountId}' not found.");

            existing = await _txStore.GetByReferenceIdAsync(req.ReferenceId, ct);
            if (existing is not null)
            {
              await tx.CommitAsync(ct);
              return Map(existing);
            }

            (persisted, ledgerEvent) = await HandleSingleAccountLockedAsync(
              account,
              req,
              op,
              transactionId,
              now,
              metadata,
              apply: a => a.Credit(req.Amount),
              ledgerEventType: "Credited",
              ct);
            break;
          }

          case OperationType.Debit:
          {
            var account = await _accounts.GetForUpdateAsync(req.AccountId, ct)
              ?? throw new DomainException($"Account '{req.AccountId}' not found.");

            existing = await _txStore.GetByReferenceIdAsync(req.ReferenceId, ct);
            if (existing is not null)
            {
              await tx.CommitAsync(ct);
              return Map(existing);
            }

            (persisted, ledgerEvent) = await HandleSingleAccountLockedAsync(
              account,
              req,
              op,
              transactionId,
              now,
              metadata,
              apply: a => a.Debit(req.Amount),
              ledgerEventType: "Debited",
              ct);
            break;
          }

          case OperationType.Reserve:
          {
            var account = await _accounts.GetForUpdateAsync(req.AccountId, ct)
              ?? throw new DomainException($"Account '{req.AccountId}' not found.");

            existing = await _txStore.GetByReferenceIdAsync(req.ReferenceId, ct);
            if (existing is not null)
            {
              await tx.CommitAsync(ct);
              return Map(existing);
            }

            (persisted, ledgerEvent) = await HandleSingleAccountLockedAsync(
              account,
              req,
              op,
              transactionId,
              now,
              metadata,
              apply: a => a.Reserve(req.Amount),
              ledgerEventType: "Reserved",
              ct);
            break;
          }

          case OperationType.Capture:
          {
            var account = await _accounts.GetForUpdateAsync(req.AccountId, ct)
              ?? throw new DomainException($"Account '{req.AccountId}' not found.");

            existing = await _txStore.GetByReferenceIdAsync(req.ReferenceId, ct);
            if (existing is not null)
            {
              await tx.CommitAsync(ct);
              return Map(existing);
            }

            (persisted, ledgerEvent) = await HandleSingleAccountLockedAsync(
              account,
              req,
              op,
              transactionId,
              now,
              metadata,
              apply: a => a.Capture(req.Amount),
              ledgerEventType: "Captured",
              ct);
            break;
          }

          case OperationType.Transfer:
          {
            if (string.IsNullOrWhiteSpace(req.TargetAccountId))
              throw new DomainException("target_account_id is required for transfer.");

            var ids = new[] { req.AccountId, req.TargetAccountId! }
              .Select(x => x.Trim())
              .Order(StringComparer.Ordinal)
              .ToList();

            var locked = await _accounts.GetForUpdateAsync(ids, ct);

            if (locked.Count != ids.Count)
            {
              var missing = ids.Except(locked.Select(a => a.AccountId), StringComparer.Ordinal).FirstOrDefault() ?? req.TargetAccountId!;
              throw new DomainException($"Account '{missing}' not found.");
            }

            existing = await _txStore.GetByReferenceIdAsync(req.ReferenceId, ct);
            if (existing is not null)
            {
              await tx.CommitAsync(ct);
              return Map(existing);
            }

            (persisted, ledgerEvent) = await HandleTransferLockedAsync(req, transactionId, now, metadata, locked, ct);
            break;
          }

          case OperationType.Reversal:
          {
            existing = await _txStore.GetByReferenceIdAsync(req.ReferenceId, ct);
            if (existing is not null)
            {
              await tx.CommitAsync(ct);
              return Map(existing);
            }

            (persisted, ledgerEvent) = await HandleReversalAsync(req, transactionId, now, metadata, ct);
            break;
          }

          default:
            throw new DomainException($"Unsupported operation: {op}");
        }

        if (ledgerEvent is not null)
          await _ledger.AppendAsync(ledgerEvent, ct);

        await _txStore.AddAsync(persisted, ct);

        var evt = new TransactionProcessedIntegrationEvent(
          TransactionId: persisted.TransactionId,
          ReferenceId: persisted.ReferenceId,
          Operation: req.Operation.Trim().ToLowerInvariant(),
          AccountId: persisted.AccountId,
          TargetAccountId: persisted.TargetAccountId,
          Amount: persisted.Amount,
          Currency: persisted.Currency,
          Status: persisted.Status.ToString().ToLowerInvariant(),
          Balance: persisted.BalanceAfter,
          ReservedBalance: persisted.ReservedBalanceAfter,
          AvailableBalance: persisted.AvailableBalanceAfter,
          Timestamp: persisted.Timestamp,
          ErrorMessage: persisted.ErrorMessage,
          Metadata: new Dictionary<string, object?>(metadata)
        );

        var payload = JsonSerializer.Serialize(evt, JsonOptions);

        await _outbox.EnqueueAsync(
          new OutboxMessage(
            EventId: Guid.NewGuid(),
            AggregateId: persisted.AccountId,
            EventType: "transaction.processed",
            PayloadJson: payload,
            OccurredAt: now
          ),
          ct);

        await _uow.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return Map(persisted);
      }
      catch (DomainException ex)
      {
        var now = _clock.UtcNow;
        var failed = await PersistFailureAsync(req, op, transactionId, now, ex.Message, ct);
        await _uow.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Map(failed);
      }
      catch (DbUpdateConcurrencyException)
      {
        try { await tx.RollbackAsync(ct); } catch { }
        _uow.ClearChangeTracker();

        if (attempt == maxAttempts)
          break;

        await Task.Delay(Backoff(attempt), ct);
      }
      catch (Exception ex) when (IsDeadlock(ex))
      {
        try { await tx.RollbackAsync(ct); } catch { }
        _uow.ClearChangeTracker();

        if (attempt == maxAttempts)
          break;

        await Task.Delay(Backoff(attempt), ct);
      }
    }

    await using (var finalTx = await _uow.BeginTransactionAsync(IsolationLevel.Serializable, ct))
    {
      var now = _clock.UtcNow;
      var failed = await PersistFailureAsync(req, op, transactionId, now, "concurrency_retry_exhausted", ct);
      await _uow.SaveChangesAsync(ct);
      await finalTx.CommitAsync(ct);
      return Map(failed);
    }
  }

  private async Task<(PersistedTransaction tx, AccountEvent ledgerEvent)> HandleSingleAccountLockedAsync(
    Account account,
    TransactionRequest req,
    OperationType op,
    string transactionId,
    DateTimeOffset now,
    IReadOnlyDictionary<string, object?> metadata,
    Action<Account> apply,
    string ledgerEventType,
    CancellationToken ct)
  {
    account.EnsureActive();
    account.EnsureCurrency(req.Currency);

    var opReq = new OperationRequest(
      Operation: op,
      AccountId: req.AccountId,
      Amount: req.Amount,
      Currency: req.Currency.ToUpperInvariant(),
      ReferenceId: req.ReferenceId,
      Metadata: metadata,
      TargetAccountId: req.TargetAccountId,
      RelatedReferenceId: req.RelatedReferenceId
    );

    apply(account);

    var ledgerEvent = account.BuildLedgerEvent(opReq, now, ledgerEventType);

    await _accounts.UpdateAsync(account, ct);

    var persisted = new PersistedTransaction(
      TransactionId: transactionId,
      ReferenceId: req.ReferenceId,
      AccountId: account.AccountId,
      Operation: op,
      Amount: req.Amount,
      Currency: account.Currency,
      Status: TransactionStatus.Success,
      BalanceAfter: account.Balance,
      ReservedBalanceAfter: account.ReservedBalance,
      AvailableBalanceAfter: account.AvailableBalance,
      Timestamp: now,
      ErrorMessage: null,
      TargetAccountId: req.TargetAccountId,
      RelatedReferenceId: req.RelatedReferenceId,
      IsReversed: false
    );

    return (persisted, ledgerEvent);
  }

  private async Task<(PersistedTransaction tx, AccountEvent ledgerEvent)> HandleTransferLockedAsync(
    TransactionRequest req,
    string transactionId,
    DateTimeOffset now,
    IReadOnlyDictionary<string, object?> metadata,
    IReadOnlyList<Account> locked,
    CancellationToken ct)
  {
    var source = locked.Single(a => a.AccountId == req.AccountId);
    var dest = locked.Single(a => a.AccountId == req.TargetAccountId);

    source.EnsureActive();
    dest.EnsureActive();
    source.EnsureCurrency(req.Currency);
    dest.EnsureCurrency(req.Currency);

    source.Debit(req.Amount);
    dest.Credit(req.Amount);

    var opReq = new OperationRequest(
      Operation: OperationType.Transfer,
      AccountId: source.AccountId,
      Amount: req.Amount,
      Currency: req.Currency.ToUpperInvariant(),
      ReferenceId: req.ReferenceId,
      Metadata: metadata,
      TargetAccountId: dest.AccountId,
      RelatedReferenceId: null
    );

    var ledgerEventSource = source.BuildLedgerEvent(opReq, now, "TransferDebited");

    var opReqDest = opReq with { AccountId = dest.AccountId };
    var ledgerEventDest = dest.BuildLedgerEvent(opReqDest, now, "TransferCredited");

    await _accounts.UpdateAsync(source, ct);
    await _accounts.UpdateAsync(dest, ct);

    var persisted = new PersistedTransaction(
      TransactionId: transactionId,
      ReferenceId: req.ReferenceId,
      AccountId: source.AccountId,
      Operation: OperationType.Transfer,
      Amount: req.Amount,
      Currency: source.Currency,
      Status: TransactionStatus.Success,
      BalanceAfter: source.Balance,
      ReservedBalanceAfter: source.ReservedBalance,
      AvailableBalanceAfter: source.AvailableBalance,
      Timestamp: now,
      ErrorMessage: null,
      TargetAccountId: dest.AccountId,
      RelatedReferenceId: null,
      IsReversed: false
    );

    await _ledger.AppendAsync(ledgerEventDest, ct);

    return (persisted, ledgerEventSource);
  }

  private async Task<(PersistedTransaction tx, AccountEvent ledgerEvent)> HandleReversalAsync(
    TransactionRequest req,
    string transactionId,
    DateTimeOffset now,
    IReadOnlyDictionary<string, object?> metadata,
    CancellationToken ct)
  {
    if (string.IsNullOrWhiteSpace(req.RelatedReferenceId))
      throw new DomainException("related_reference_id is required for reversal.");

    var original = await _txStore.GetByReferenceIdAsync(req.RelatedReferenceId!, ct);
    if (original is null)
      throw new DomainException($"Original transaction '{req.RelatedReferenceId}' not found.");

    if (original.IsReversed)
      throw new DomainException($"Original transaction '{req.RelatedReferenceId}' is already reversed.");

    var impacted = new List<string> { original.AccountId };
    if (original.Operation == OperationType.Transfer && !string.IsNullOrWhiteSpace(original.TargetAccountId))
      impacted.Add(original.TargetAccountId!);

    impacted = impacted.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

    var locked = await _accounts.GetForUpdateAsync(impacted, ct);

    if (locked.Count != impacted.Count)
    {
      var missing = impacted.Except(locked.Select(a => a.AccountId), StringComparer.Ordinal).FirstOrDefault() ?? impacted[0];
      throw new DomainException($"Account '{missing}' not found.");
    }

    var origin = locked.Single(a => a.AccountId == original.AccountId);

    origin.EnsureActive();
    origin.EnsureCurrency(original.Currency);

    Account? target = null;
    if (original.Operation == OperationType.Transfer && !string.IsNullOrWhiteSpace(original.TargetAccountId))
    {
      target = locked.Single(a => a.AccountId == original.TargetAccountId);
      target.EnsureActive();
      target.EnsureCurrency(original.Currency);
    }

    switch (original.Operation)
    {
      case OperationType.Credit:
        origin.Debit(original.Amount);
        break;
      case OperationType.Debit:
        origin.Credit(original.Amount);
        break;
      case OperationType.Reserve:
        origin.ReleaseReservation(original.Amount);
        break;
      case OperationType.Capture:
        origin.RefundCapture(original.Amount);
        break;
      case OperationType.Transfer:
        if (target is null)
          throw new DomainException("Invalid original transfer (missing target).");
        target.Debit(original.Amount);
        origin.Credit(original.Amount);
        break;
      default:
        throw new DomainException($"Reversal not supported for operation '{original.Operation}'.");
    }

    var opReq = new OperationRequest(
      Operation: OperationType.Reversal,
      AccountId: origin.AccountId,
      Amount: original.Amount,
      Currency: original.Currency,
      ReferenceId: req.ReferenceId,
      Metadata: metadata,
      TargetAccountId: target?.AccountId,
      RelatedReferenceId: original.ReferenceId
    );

    var ledgerEvent = origin.BuildLedgerEvent(opReq, now, "Reversed");

    await _accounts.UpdateAsync(origin, ct);
    if (target is not null)
    {
      var opReqTarget = opReq with { AccountId = target.AccountId };
      var ledgerEventTarget = target.BuildLedgerEvent(opReqTarget, now, "ReversalApplied");
      await _ledger.AppendAsync(ledgerEventTarget, ct);
      await _accounts.UpdateAsync(target, ct);
    }

    await _txStore.MarkReversedAsync(original.ReferenceId, req.ReferenceId, ct);

    var persisted = new PersistedTransaction(
      TransactionId: transactionId,
      ReferenceId: req.ReferenceId,
      AccountId: origin.AccountId,
      Operation: OperationType.Reversal,
      Amount: original.Amount,
      Currency: origin.Currency,
      Status: TransactionStatus.Success,
      BalanceAfter: origin.Balance,
      ReservedBalanceAfter: origin.ReservedBalance,
      AvailableBalanceAfter: origin.AvailableBalance,
      Timestamp: now,
      ErrorMessage: null,
      TargetAccountId: target?.AccountId,
      RelatedReferenceId: original.ReferenceId,
      IsReversed: false
    );

    return (persisted, ledgerEvent);
  }

  private async Task<PersistedTransaction> PersistFailureAsync(
    TransactionRequest req,
    OperationType op,
    string transactionId,
    DateTimeOffset now,
    string errorMessage,
    CancellationToken ct)
  {
    var account = await _accounts.GetForUpdateAsync(req.AccountId, ct);

    var balance = account?.Balance ?? 0;
    var reserved = account?.ReservedBalance ?? 0;
    var available = account is null ? 0 : account.AvailableBalance;
    var currency = account?.Currency ?? req.Currency.ToUpperInvariant();

    var failed = new PersistedTransaction(
      TransactionId: transactionId,
      ReferenceId: req.ReferenceId,
      AccountId: req.AccountId,
      Operation: op,
      Amount: req.Amount,
      Currency: currency,
      Status: TransactionStatus.Failed,
      BalanceAfter: balance,
      ReservedBalanceAfter: reserved,
      AvailableBalanceAfter: available,
      Timestamp: now,
      ErrorMessage: errorMessage,
      TargetAccountId: req.TargetAccountId,
      RelatedReferenceId: req.RelatedReferenceId,
      IsReversed: false
    );

    await _txStore.AddAsync(failed, ct);

    var evt = new TransactionProcessedIntegrationEvent(
      TransactionId: failed.TransactionId,
      ReferenceId: failed.ReferenceId,
      Operation: req.Operation.Trim().ToLowerInvariant(),
      AccountId: failed.AccountId,
      TargetAccountId: failed.TargetAccountId,
      Amount: failed.Amount,
      Currency: failed.Currency,
      Status: "failed",
      Balance: failed.BalanceAfter,
      ReservedBalance: failed.ReservedBalanceAfter,
      AvailableBalance: failed.AvailableBalanceAfter,
      Timestamp: now,
      ErrorMessage: failed.ErrorMessage,
      Metadata: new Dictionary<string, object?>(req.Metadata ?? new())
    );

    var payload = JsonSerializer.Serialize(evt, JsonOptions);

    await _outbox.EnqueueAsync(
      new OutboxMessage(Guid.NewGuid(), failed.AccountId, "transaction.processed", payload, now),
      ct);

    return failed;
  }

  private static TransactionResponse Map(PersistedTransaction tx) =>
    new(
      TransactionId: tx.TransactionId,
      Status: tx.Status.ToString().ToLowerInvariant(),
      Balance: tx.BalanceAfter,
      ReservedBalance: tx.ReservedBalanceAfter,
      AvailableBalance: tx.AvailableBalanceAfter,
      Timestamp: tx.Timestamp.UtcDateTime.ToString("O"),
      ErrorMessage: tx.ErrorMessage
    );

  private static SqlException? FindSqlException(Exception ex)
  {
    for (Exception? cur = ex; cur is not null; cur = cur.InnerException)
    {
      if (cur is SqlException sql)
        return sql;
    }
    return null;
  }

  private static bool IsDeadlock(Exception ex)
  {
    var sql = FindSqlException(ex);
    return sql is not null && sql.Number == 1205;
  }

  private static TimeSpan Backoff(int attempt)
  {
    var exp = Math.Min(250, (int)(20 * Math.Pow(2, attempt - 1)));
    var jitter = Random.Shared.Next(0, 30);
    return TimeSpan.FromMilliseconds(exp + jitter);
  }
}
