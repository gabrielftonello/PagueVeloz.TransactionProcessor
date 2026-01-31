namespace PagueVeloz.TransactionProcessor.Infrastructure.Persistence.Entities;

public sealed class QueuedCommandEntity
{
  public Guid CommandId { get; set; }
  public string PayloadJson { get; set; } = default!;
  public DateTimeOffset EnqueuedAt { get; set; }
  public string Status { get; set; } = "Pending"; // Pending, Processing, Done, Failed
  public string? ErrorMessage { get; set; }
  public DateTimeOffset? ProcessedAt { get; set; }
}
