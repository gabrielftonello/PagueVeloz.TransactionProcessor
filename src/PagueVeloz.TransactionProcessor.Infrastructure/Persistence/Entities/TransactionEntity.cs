namespace PagueVeloz.TransactionProcessor.Infrastructure.Persistence.Entities;

public sealed class TransactionEntity
{
  public string TransactionId { get; set; } = default!;
  public string ReferenceId { get; set; } = default!;
  public string AccountId { get; set; } = default!;
  public int Operation { get; set; }
  public long Amount { get; set; }
  public string Currency { get; set; } = default!;
  public int Status { get; set; }
  public long BalanceAfter { get; set; }
  public long ReservedBalanceAfter { get; set; }
  public long AvailableBalanceAfter { get; set; }
  public DateTimeOffset Timestamp { get; set; }
  public string? ErrorMessage { get; set; }

  public string? TargetAccountId { get; set; }
  public string? RelatedReferenceId { get; set; }
  public bool IsReversed { get; set; }
  public string? ReversalReferenceId { get; set; }
}
