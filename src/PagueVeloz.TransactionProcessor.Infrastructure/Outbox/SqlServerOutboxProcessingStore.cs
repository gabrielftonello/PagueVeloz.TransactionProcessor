using Microsoft.EntityFrameworkCore;
using PagueVeloz.TransactionProcessor.Infrastructure.Outbox.Models;
using PagueVeloz.TransactionProcessor.Infrastructure.Persistence;

namespace PagueVeloz.TransactionProcessor.Infrastructure.Outbox;

public sealed class SqlServerOutboxProcessingStore : IOutboxProcessingStore
{
  private readonly TransactionDbContext _db;

  public SqlServerOutboxProcessingStore(TransactionDbContext db) => _db = db;

  public async Task<IReadOnlyList<PendingOutboxEvent>> FetchBatchForProcessingAsync(int batchSize, CancellationToken ct)
  {
    var now = DateTimeOffset.UtcNow;

    var rows = await _db.OutboxEvents
      .FromSqlRaw($"SELECT TOP({batchSize}) * FROM OutboxEvents WITH (UPDLOCK, READPAST, ROWLOCK) WHERE ProcessedAt IS NULL AND NextAttemptAt <= SYSUTCDATETIME() ORDER BY NextAttemptAt")
      .ToListAsync(ct);

    return rows.Select(e => new PendingOutboxEvent(
      e.EventId,
      e.AggregateId,
      e.EventType,
      e.PayloadJson,
      e.OccurredAt,
      e.Attempts
    )).ToList();
  }

  public async Task MarkProcessedAsync(Guid eventId, DateTimeOffset processedAt, CancellationToken ct)
  {
    var e = await _db.OutboxEvents.SingleAsync(x => x.EventId == eventId, ct);
    e.ProcessedAt = processedAt;
    e.LastError = null;
  }

  public async Task MarkFailedAsync(Guid eventId, int attempts, DateTimeOffset nextAttemptAt, string lastError, CancellationToken ct)
  {
    var e = await _db.OutboxEvents.SingleAsync(x => x.EventId == eventId, ct);
    e.Attempts = attempts;
    e.NextAttemptAt = nextAttemptAt;
    e.LastError = lastError.Length > 2000 ? lastError[..2000] : lastError;
  }
}
