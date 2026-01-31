using PagueVeloz.TransactionProcessor.Domain.Transactions;

namespace PagueVeloz.TransactionProcessor.Domain.Transactions;

public sealed record OperationRequest(
  OperationType Operation,
  string AccountId,
  long Amount,
  string Currency,
  string ReferenceId,
  IReadOnlyDictionary<string, object?> Metadata,
  string? TargetAccountId = null,
  string? RelatedReferenceId = null
);
