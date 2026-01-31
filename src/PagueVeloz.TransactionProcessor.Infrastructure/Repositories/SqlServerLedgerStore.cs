using System.Text.Json;
using PagueVeloz.TransactionProcessor.Application.Abstractions;
using PagueVeloz.TransactionProcessor.Domain.Events;
using PagueVeloz.TransactionProcessor.Infrastructure.Persistence;
using PagueVeloz.TransactionProcessor.Infrastructure.Persistence.Entities;

namespace PagueVeloz.TransactionProcessor.Infrastructure.Repositories;

public sealed class SqlServerLedgerStore : ILedgerStore
{
  private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
  private readonly TransactionDbContext _db;

  public SqlServerLedgerStore(TransactionDbContext db) => _db = db;

  public Task AppendAsync(AccountEvent evt, CancellationToken ct)
  {
    _db.AccountEvents.Add(new AccountEventEntity
    {
      EventId = Guid.NewGuid(),
      AccountId = evt.AccountId,
      Sequence = evt.Sequence,
      EventType = evt.EventType,
      Amount = evt.Amount,
      Currency = evt.Currency,
      ReferenceId = evt.ReferenceId,
      RelatedReferenceId = evt.RelatedReferenceId,
      TargetAccountId = evt.TargetAccountId,
      OccurredAt = evt.OccurredAt,
      BalanceAfter = evt.BalanceAfter,
      ReservedBalanceAfter = evt.ReservedBalanceAfter,
      AvailableBalanceAfter = evt.AvailableBalanceAfter,
      MetadataJson = JsonSerializer.Serialize(evt.Metadata, JsonOptions)
    });

    return Task.CompletedTask;
  }
}
