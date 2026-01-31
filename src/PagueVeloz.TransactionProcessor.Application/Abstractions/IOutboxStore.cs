namespace PagueVeloz.TransactionProcessor.Application.Abstractions;

public interface IOutboxStore
{
  Task EnqueueAsync(OutboxMessage msg, CancellationToken ct);
}

public sealed record OutboxMessage(
  Guid EventId,
  string AggregateId,
  string EventType,
  string PayloadJson,
  DateTimeOffset OccurredAt
);
