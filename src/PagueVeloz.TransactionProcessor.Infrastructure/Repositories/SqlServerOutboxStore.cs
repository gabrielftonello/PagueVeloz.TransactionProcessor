using PagueVeloz.TransactionProcessor.Application.Abstractions;
using PagueVeloz.TransactionProcessor.Infrastructure.Persistence;
using PagueVeloz.TransactionProcessor.Infrastructure.Persistence.Entities;

namespace PagueVeloz.TransactionProcessor.Infrastructure.Repositories;

public sealed class SqlServerOutboxStore : IOutboxStore
{
  private readonly TransactionDbContext _db;

  public SqlServerOutboxStore(TransactionDbContext db) => _db = db;

  public Task EnqueueAsync(OutboxMessage msg, CancellationToken ct)
  {
    _db.OutboxEvents.Add(new OutboxEventEntity
    {
      EventId = msg.EventId,
      AggregateId = msg.AggregateId,
      EventType = msg.EventType,
      PayloadJson = msg.PayloadJson,
      OccurredAt = msg.OccurredAt,
      Attempts = 0,
      NextAttemptAt = msg.OccurredAt,
      ProcessedAt = null,
      LastError = null
    });

    return Task.CompletedTask;
  }
}
