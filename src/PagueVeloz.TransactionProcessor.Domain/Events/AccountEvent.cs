namespace PagueVeloz.TransactionProcessor.Domain.Events;

public sealed record AccountEvent(
  string AccountId,
  long Sequence,
  string EventType,
  long Amount,
  string Currency,
  string ReferenceId,
  string? RelatedReferenceId,
  string? TargetAccountId,
  DateTimeOffset OccurredAt,
  long BalanceAfter,
  long ReservedBalanceAfter,
  long AvailableBalanceAfter,
  IReadOnlyDictionary<string, object?> Metadata
);
