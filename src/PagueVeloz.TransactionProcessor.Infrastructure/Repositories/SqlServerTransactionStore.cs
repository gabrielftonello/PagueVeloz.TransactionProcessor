using Microsoft.EntityFrameworkCore;
using PagueVeloz.TransactionProcessor.Application.Abstractions;
using PagueVeloz.TransactionProcessor.Domain.Transactions;
using PagueVeloz.TransactionProcessor.Infrastructure.Persistence;
using PagueVeloz.TransactionProcessor.Infrastructure.Persistence.Entities;

namespace PagueVeloz.TransactionProcessor.Infrastructure.Repositories;

public sealed class SqlServerTransactionStore : ITransactionStore
{
  private readonly TransactionDbContext _db;

  public SqlServerTransactionStore(TransactionDbContext db) => _db = db;

  public async Task<PersistedTransaction?> GetByReferenceIdAsync(string referenceId, CancellationToken ct)
  {
    var e = await _db.Transactions.AsNoTracking().FirstOrDefaultAsync(x => x.ReferenceId == referenceId, ct);
    return e is null ? null : Map(e);
  }

  public async Task<PersistedTransaction?> GetByRelatedReferenceIdAsync(string relatedReferenceId, CancellationToken ct)
  {
    var e = await _db.Transactions.AsNoTracking().FirstOrDefaultAsync(x => x.RelatedReferenceId == relatedReferenceId, ct);
    return e is null ? null : Map(e);
  }

  public Task AddAsync(PersistedTransaction tx, CancellationToken ct)
  {
    _db.Transactions.Add(new TransactionEntity
    {
      TransactionId = tx.TransactionId,
      ReferenceId = tx.ReferenceId,
      AccountId = tx.AccountId,
      Operation = (int)tx.Operation,
      Amount = tx.Amount,
      Currency = tx.Currency,
      Status = (int)tx.Status,
      BalanceAfter = tx.BalanceAfter,
      ReservedBalanceAfter = tx.ReservedBalanceAfter,
      AvailableBalanceAfter = tx.AvailableBalanceAfter,
      Timestamp = tx.Timestamp,
      ErrorMessage = tx.ErrorMessage,
      TargetAccountId = tx.TargetAccountId,
      RelatedReferenceId = tx.RelatedReferenceId,
      IsReversed = tx.IsReversed,
      ReversalReferenceId = null
    });
    return Task.CompletedTask;
  }

  public async Task MarkReversedAsync(string referenceId, string reversalReferenceId, CancellationToken ct)
  {
    var e = await _db.Transactions.SingleAsync(x => x.ReferenceId == referenceId, ct);
    e.IsReversed = true;
    e.ReversalReferenceId = reversalReferenceId;
  }

  public async Task<IReadOnlyList<PersistedTransaction>> ListByAccountIdAsync(string accountId, CancellationToken ct)
  {
    var list = await _db.Transactions
      .Where(x => x.AccountId == accountId)
      .OrderByDescending(x => x.Timestamp)
      .ToListAsync(ct);

    return list.Select(Map).ToList();
  }

  private static PersistedTransaction Map(TransactionEntity e) =>
    new(
      TransactionId: e.TransactionId,
      ReferenceId: e.ReferenceId,
      AccountId: e.AccountId,
      Operation: (OperationType)e.Operation,
      Amount: e.Amount,
      Currency: e.Currency,
      Status: (TransactionStatus)e.Status,
      BalanceAfter: e.BalanceAfter,
      ReservedBalanceAfter: e.ReservedBalanceAfter,
      AvailableBalanceAfter: e.AvailableBalanceAfter,
      Timestamp: e.Timestamp,
      ErrorMessage: e.ErrorMessage,
      TargetAccountId: e.TargetAccountId,
      RelatedReferenceId: e.RelatedReferenceId,
      IsReversed: e.IsReversed
    );
}
