namespace PagueVeloz.TransactionProcessor.Domain.Transactions;

public enum OperationType
{
  Credit = 1,
  Debit = 2,
  Reserve = 3,
  Capture = 4,
  Reversal = 5,
  Transfer = 6
}
