using PagueVeloz.TransactionProcessor.Domain.Events;
using PagueVeloz.TransactionProcessor.Domain.Transactions;

namespace PagueVeloz.TransactionProcessor.Domain.Transactions;

public sealed record OperationResult(
  string TransactionId,
  TransactionStatus Status,
  long Balance,
  long ReservedBalance,
  long AvailableBalance,
  DateTimeOffset Timestamp,
  string? ErrorMessage,
  AccountEvent? LedgerEvent
);
