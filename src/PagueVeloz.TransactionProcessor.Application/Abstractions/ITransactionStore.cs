using PagueVeloz.TransactionProcessor.Domain.Transactions;

namespace PagueVeloz.TransactionProcessor.Application.Abstractions;

public interface ITransactionStore
{
  Task<PersistedTransaction?> GetByReferenceIdAsync(string referenceId, CancellationToken ct);
  Task<PersistedTransaction?> GetByRelatedReferenceIdAsync(string relatedReferenceId, CancellationToken ct);
  Task AddAsync(PersistedTransaction tx, CancellationToken ct);

  Task<IReadOnlyList<PersistedTransaction>> ListByAccountIdAsync(string accountId, CancellationToken ct);

  Task MarkReversedAsync(string referenceId, string reversalReferenceId, CancellationToken ct);
}

public sealed record PersistedTransaction(
  string TransactionId,
  string ReferenceId,
  string AccountId,
  OperationType Operation,
  long Amount,
  string Currency,
  TransactionStatus Status,
  long BalanceAfter,
  long ReservedBalanceAfter,
  long AvailableBalanceAfter,
  DateTimeOffset Timestamp,
  string? ErrorMessage,
  string? TargetAccountId,
  string? RelatedReferenceId,
  bool IsReversed
);
