namespace PagueVeloz.TransactionProcessor.Application.IntegrationEvents;

public sealed record TransactionProcessedIntegrationEvent(
  string TransactionId,
  string ReferenceId,
  string Operation,
  string AccountId,
  string? TargetAccountId,
  long Amount,
  string Currency,
  string Status,
  long Balance,
  long ReservedBalance,
  long AvailableBalance,
  DateTimeOffset Timestamp,
  string? ErrorMessage,
  Dictionary<string, object?> Metadata
);
