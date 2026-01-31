namespace PagueVeloz.TransactionProcessor.Infrastructure.Outbox.Models;

public sealed record PendingOutboxEvent(
  Guid EventId,
  string AggregateId,
  string EventType,
  string PayloadJson,
  DateTimeOffset OccurredAt,
  int Attempts
);
