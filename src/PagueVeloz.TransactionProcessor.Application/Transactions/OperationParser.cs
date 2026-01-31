using PagueVeloz.TransactionProcessor.Domain.Transactions;

namespace PagueVeloz.TransactionProcessor.Application.Transactions;

public static class OperationParser
{
  public static OperationType Parse(string operation)
  {
    if (string.IsNullOrWhiteSpace(operation))
      throw new ArgumentException("operation is required");

    return operation.Trim().ToLowerInvariant() switch
    {
      "credit" => OperationType.Credit,
      "debit" => OperationType.Debit,
      "reserve" => OperationType.Reserve,
      "capture" => OperationType.Capture,
      "reversal" => OperationType.Reversal,
      "transfer" => OperationType.Transfer,
      _ => throw new ArgumentException($"Unknown operation '{operation}'.")
    };
  }
}
