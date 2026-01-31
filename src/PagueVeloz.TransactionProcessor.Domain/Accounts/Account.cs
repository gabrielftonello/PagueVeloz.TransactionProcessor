using PagueVeloz.TransactionProcessor.Domain.Accounts;
using PagueVeloz.TransactionProcessor.Domain.Events;
using PagueVeloz.TransactionProcessor.Domain.Transactions;

namespace PagueVeloz.TransactionProcessor.Domain.Accounts;

public sealed class Account
{
  public string AccountId { get; }
  public string ClientId { get; }
  public string Currency { get; }
  public long Balance { get; private set; }
  public long ReservedBalance { get; private set; }
  public long CreditLimit { get; private set; }
  public AccountStatus Status { get; private set; }
  public long LedgerSequence { get; private set; }

  public long AvailableBalance => Balance - ReservedBalance;

  public Account(
    string accountId,
    string clientId,
    string currency,
    long balance,
    long reservedBalance,
    long creditLimit,
    AccountStatus status,
    long ledgerSequence)
  {
    AccountId = accountId;
    ClientId = clientId;
    Currency = currency;
    Balance = balance;
    ReservedBalance = reservedBalance;
    CreditLimit = creditLimit;
    Status = status;
    LedgerSequence = ledgerSequence;
  }

  public void EnsureActive()
  {
    if (Status != AccountStatus.Active)
      throw new DomainException($"Account '{AccountId}' is not active (status={Status}).");
  }

  public void EnsureCurrency(string currency)
  {
    if (!string.Equals(Currency, currency, StringComparison.OrdinalIgnoreCase))
      throw new DomainException($"Currency mismatch for account '{AccountId}'. Expected '{Currency}', got '{currency}'.");
  }

  public void Credit(long amount)
  {
    EnsureActive();
    EnsurePositiveAmount(amount);
    Balance += amount;
  }

  public void Debit(long amount)
  {
    EnsureActive();
    EnsurePositiveAmount(amount);
    var spendable = AvailableBalance + CreditLimit;
    if (spendable < amount)
      throw new DomainException("Insufficient funds considering credit limit.");
    Balance -= amount;
  }

  public void Reserve(long amount)
  {
    EnsureActive();
    EnsurePositiveAmount(amount);
    if (AvailableBalance < amount)
      throw new DomainException("Insufficient available balance to reserve.");
    ReservedBalance += amount;
  }

  public void Capture(long amount)
  {
    EnsureActive();
    EnsurePositiveAmount(amount);
    if (ReservedBalance < amount)
      throw new DomainException("Insufficient reserved balance to capture.");
    ReservedBalance -= amount;
    Balance -= amount;
  }

  public void ReleaseReservation(long amount)
  {
    EnsureActive();
    EnsurePositiveAmount(amount);
    if (ReservedBalance < amount)
      throw new DomainException("Insufficient reserved balance to release.");
    ReservedBalance -= amount;
  }

  public void RefundCapture(long amount)
  {
    EnsureActive();
    EnsurePositiveAmount(amount);
    Balance += amount;
  }

  public long NextSequence()
  {
    LedgerSequence += 1;
    return LedgerSequence;
  }

  private static void EnsurePositiveAmount(long amount)
  {
    if (amount <= 0)
      throw new DomainException("Amount must be a positive integer (in cents).");
  }

  public AccountEvent BuildLedgerEvent(OperationRequest req, DateTimeOffset now, string eventType)
  {
    var seq = NextSequence();
    return new AccountEvent(
      AccountId: AccountId,
      Sequence: seq,
      EventType: eventType,
      Amount: req.Amount,
      Currency: req.Currency,
      ReferenceId: req.ReferenceId,
      RelatedReferenceId: req.RelatedReferenceId,
      TargetAccountId: req.TargetAccountId,
      OccurredAt: now,
      BalanceAfter: Balance,
      ReservedBalanceAfter: ReservedBalance,
      AvailableBalanceAfter: AvailableBalance,
      Metadata: req.Metadata
    );
  }
}
