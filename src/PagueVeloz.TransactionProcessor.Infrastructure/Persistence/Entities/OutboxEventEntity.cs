namespace PagueVeloz.TransactionProcessor.Infrastructure.Persistence.Entities;

public sealed class OutboxEventEntity
{
  public Guid EventId { get; set; }
  public string AggregateId { get; set; } = default!;
  public string EventType { get; set; } = default!;
  public string PayloadJson { get; set; } = default!;
  public DateTimeOffset OccurredAt { get; set; }

  public DateTimeOffset? ProcessedAt { get; set; }
  public int Attempts { get; set; }
  public DateTimeOffset NextAttemptAt { get; set; }
  public string? LastError { get; set; }
}
