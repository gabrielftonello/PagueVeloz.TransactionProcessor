using PagueVeloz.TransactionProcessor.Infrastructure.Outbox.Models;

namespace PagueVeloz.TransactionProcessor.Infrastructure.Outbox;

public interface IOutboxProcessingStore
{
  Task<IReadOnlyList<PendingOutboxEvent>> FetchBatchForProcessingAsync(int batchSize, CancellationToken ct);
  Task MarkProcessedAsync(Guid eventId, DateTimeOffset processedAt, CancellationToken ct);
  Task MarkFailedAsync(Guid eventId, int attempts, DateTimeOffset nextAttemptAt, string lastError, CancellationToken ct);
}
