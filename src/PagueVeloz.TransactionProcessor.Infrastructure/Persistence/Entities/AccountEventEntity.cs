namespace PagueVeloz.TransactionProcessor.Infrastructure.Persistence.Entities;

public sealed class AccountEventEntity
{
  public Guid EventId { get; set; }
  public string AccountId { get; set; } = default!;
  public long Sequence { get; set; }
  public string EventType { get; set; } = default!;
  public long Amount { get; set; }
  public string Currency { get; set; } = default!;
  public string ReferenceId { get; set; } = default!;
  public string? RelatedReferenceId { get; set; }
  public string? TargetAccountId { get; set; }
  public DateTimeOffset OccurredAt { get; set; }

  public long BalanceAfter { get; set; }
  public long ReservedBalanceAfter { get; set; }
  public long AvailableBalanceAfter { get; set; }

  public string MetadataJson { get; set; } = "{}";
}
