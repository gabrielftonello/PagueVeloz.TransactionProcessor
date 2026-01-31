using PagueVeloz.TransactionProcessor.Domain;
using PagueVeloz.TransactionProcessor.Domain.Accounts;
using PagueVeloz.TransactionProcessor.Domain.Transactions;
using Xunit;

namespace PagueVeloz.TransactionProcessor.UnitTests;

public sealed class AccountTests
{
  [Fact]
  public void Credit_increases_balance()
  {
    var a = NewAccount(balance: 0, creditLimit: 0);
    a.Credit(10_000);
    Assert.Equal(10_000, a.Balance);
    Assert.Equal(10_000, a.AvailableBalance);
  }

  [Fact]
  public void Debit_uses_credit_limit_when_needed()
  {
    var a = NewAccount(balance: 30_000, creditLimit: 50_000);
    a.Debit(60_000);

    Assert.Equal(-30_000, a.Balance);
    Assert.Equal(-30_000, a.AvailableBalance);
  }

  [Fact]
  public void Debit_fails_when_exceeds_credit_limit()
  {
    var a = NewAccount(balance: 30_000, creditLimit: 50_000);

    Assert.Throws<DomainException>(() => a.Debit(90_000));
    Assert.Equal(30_000, a.Balance);
  }

  [Fact]
  public void Reserve_moves_from_available_to_reserved()
  {
    var a = NewAccount(balance: 100_000, creditLimit: 0);
    a.Reserve(30_000);

    Assert.Equal(100_000, a.Balance);
    Assert.Equal(30_000, a.ReservedBalance);
    Assert.Equal(70_000, a.AvailableBalance);
  }

  [Fact]
  public void Capture_decreases_reserved_and_balance()
  {
    var a = NewAccount(balance: 100_000, creditLimit: 0);
    a.Reserve(30_000);
    a.Capture(30_000);

    Assert.Equal(70_000, a.Balance);
    Assert.Equal(0, a.ReservedBalance);
    Assert.Equal(70_000, a.AvailableBalance);
  }

    [Fact]
  public void Invariant_balance_equals_available_plus_reserved_initially()
  {
    var a = NewAccount(balance: 123_000, reserved: 45_000, creditLimit: 0, status: AccountStatus.Active);
    Assert.Equal(a.Balance, a.AvailableBalance + a.ReservedBalance);
  }

  [Fact]
  public void Credit_does_not_change_reserved_balance()
  {
    var a = NewAccount(balance: 10_000, reserved: 3_000, creditLimit: 0, status: AccountStatus.Active);
    a.Credit(5_000);

    Assert.Equal(15_000, a.Balance);
    Assert.Equal(3_000, a.ReservedBalance);
    Assert.Equal(12_000, a.AvailableBalance);
    Assert.Equal(a.Balance, a.AvailableBalance + a.ReservedBalance);
  }

  [Fact]
  public void Debit_does_not_change_reserved_balance()
  {
    var a = NewAccount(balance: 50_000, reserved: 20_000, creditLimit: 0, status: AccountStatus.Active);
    a.Debit(10_000);

    Assert.Equal(40_000, a.Balance);
    Assert.Equal(20_000, a.ReservedBalance);
    Assert.Equal(20_000, a.AvailableBalance);
    Assert.Equal(a.Balance, a.AvailableBalance + a.ReservedBalance);
  }

  [Fact]
  public void Reserve_does_not_change_balance()
  {
    var a = NewAccount(balance: 80_000, reserved: 0, creditLimit: 0, status: AccountStatus.Active);
    a.Reserve(30_000);

    Assert.Equal(80_000, a.Balance);
    Assert.Equal(30_000, a.ReservedBalance);
    Assert.Equal(50_000, a.AvailableBalance);
    Assert.Equal(a.Balance, a.AvailableBalance + a.ReservedBalance);
  }

  [Fact]
  public void Reserve_exactly_available_succeeds()
  {
    var a = NewAccount(balance: 20_000, reserved: 5_000, creditLimit: 0, status: AccountStatus.Active);
    a.Reserve(15_000);

    Assert.Equal(20_000, a.Balance);
    Assert.Equal(20_000, a.ReservedBalance);
    Assert.Equal(0, a.AvailableBalance);
    Assert.Equal(a.Balance, a.AvailableBalance + a.ReservedBalance);
  }

  [Fact]
  public void Reserve_fails_when_amount_exceeds_available()
  {
    var a = NewAccount(balance: 20_000, reserved: 5_000, creditLimit: 0, status: AccountStatus.Active);

    Assert.Throws<DomainException>(() => a.Reserve(15_001));
    Assert.Equal(20_000, a.Balance);
    Assert.Equal(5_000, a.ReservedBalance);
    Assert.Equal(15_000, a.AvailableBalance);
  }

  [Fact]
  public void Reserve_cannot_use_credit_limit()
  {
    var a = NewAccount(balance: 0, reserved: 0, creditLimit: 50_000, status: AccountStatus.Active);

    Assert.Throws<DomainException>(() => a.Reserve(1));
    Assert.Equal(0, a.Balance);
    Assert.Equal(0, a.ReservedBalance);
    Assert.Equal(0, a.AvailableBalance);
  }

  [Fact]
  public void Capture_partial_keeps_available_constant()
  {
    var a = NewAccount(balance: 100_000, reserved: 0, creditLimit: 0, status: AccountStatus.Active);
    a.Reserve(30_000);

    var availableBefore = a.AvailableBalance;
    a.Capture(10_000);

    Assert.Equal(90_000, a.Balance);
    Assert.Equal(20_000, a.ReservedBalance);
    Assert.Equal(availableBefore, a.AvailableBalance);
    Assert.Equal(a.Balance, a.AvailableBalance + a.ReservedBalance);
  }

  [Fact]
  public void Capture_equal_reserved_clears_reserved()
  {
    var a = NewAccount(balance: 100_000, reserved: 0, creditLimit: 0, status: AccountStatus.Active);
    a.Reserve(30_000);
    a.Capture(30_000);

    Assert.Equal(70_000, a.Balance);
    Assert.Equal(0, a.ReservedBalance);
    Assert.Equal(70_000, a.AvailableBalance);
    Assert.Equal(a.Balance, a.AvailableBalance + a.ReservedBalance);
  }

  [Fact]
  public void ReleaseReservation_equal_reserved_clears_reserved()
  {
    var a = NewAccount(balance: 100_000, reserved: 25_000, creditLimit: 0, status: AccountStatus.Active);
    a.ReleaseReservation(25_000);

    Assert.Equal(100_000, a.Balance);
    Assert.Equal(0, a.ReservedBalance);
    Assert.Equal(100_000, a.AvailableBalance);
    Assert.Equal(a.Balance, a.AvailableBalance + a.ReservedBalance);
  }

  [Fact]
  public void ReleaseReservation_fails_when_amount_exceeds_reserved()
  {
    var a = NewAccount(balance: 100_000, reserved: 25_000, creditLimit: 0, status: AccountStatus.Active);

    Assert.Throws<DomainException>(() => a.ReleaseReservation(25_001));
    Assert.Equal(100_000, a.Balance);
    Assert.Equal(25_000, a.ReservedBalance);
    Assert.Equal(75_000, a.AvailableBalance);
  }

  [Fact]
  public void RefundCapture_does_not_change_reserved_balance()
  {
    var a = NewAccount(balance: 10_000, reserved: 3_000, creditLimit: 0, status: AccountStatus.Active);
    a.RefundCapture(5_000);

    Assert.Equal(15_000, a.Balance);
    Assert.Equal(3_000, a.ReservedBalance);
    Assert.Equal(12_000, a.AvailableBalance);
    Assert.Equal(a.Balance, a.AvailableBalance + a.ReservedBalance);
  }
 [Fact]
  public void Debit_succeeds_when_amount_equals_available_plus_credit_limit()
  {
    var a = NewAccount(balance: 0, reserved: 0, creditLimit: 50_000, status: AccountStatus.Active);

    a.Debit(50_000);

    Assert.Equal(-50_000, a.Balance);
    Assert.Equal(0, a.ReservedBalance);
    Assert.Equal(-50_000, a.AvailableBalance);
  }

  [Fact]
  public void Debit_fails_when_amount_exceeds_available_plus_credit_limit_by_1()
  {
    var a = NewAccount(balance: 0, reserved: 0, creditLimit: 50_000, status: AccountStatus.Active);

    Assert.Throws<DomainException>(() => a.Debit(50_001));
    Assert.Equal(0, a.Balance);
    Assert.Equal(0, a.ReservedBalance);
    Assert.Equal(0, a.AvailableBalance);
  }

  [Fact]
  public void Debit_considers_reserved_balance_when_calculating_available_plus_credit()
  {
    var a = NewAccount(balance: 10_000, reserved: 8_000, creditLimit: 5_000, status: AccountStatus.Active);

    a.Debit(7_000);

    Assert.Equal(3_000, a.Balance);
    Assert.Equal(8_000, a.ReservedBalance);
    Assert.Equal(-5_000, a.AvailableBalance);
  }

  [Fact]
  public void Debit_fails_when_reserved_reduces_available_and_exceeds_credit()
  {
    var a = NewAccount(balance: 10_000, reserved: 8_000, creditLimit: 5_000, status: AccountStatus.Active);

    Assert.Throws<DomainException>(() => a.Debit(7_001));
    Assert.Equal(10_000, a.Balance);
    Assert.Equal(8_000, a.ReservedBalance);
    Assert.Equal(2_000, a.AvailableBalance);
  }

  [Fact]
  public void After_using_part_of_credit_limit_debit_can_go_to_exact_limit()
  {
    var a = NewAccount(balance: 30_000, reserved: 0, creditLimit: 50_000, status: AccountStatus.Active);

    a.Debit(60_000);
    Assert.Equal(-30_000, a.Balance);

    a.Debit(20_000);
    Assert.Equal(-50_000, a.Balance);
    Assert.Equal(-50_000, a.AvailableBalance);
  }

  [Fact]
  public void After_reaching_exact_credit_limit_debit_fails_for_any_extra_amount()
  {
    var a = NewAccount(balance: 30_000, reserved: 0, creditLimit: 50_000, status: AccountStatus.Active);

    a.Debit(80_000);
    Assert.Equal(-50_000, a.Balance);

    Assert.Throws<DomainException>(() => a.Debit(1));
    Assert.Equal(-50_000, a.Balance);
  }
    [Theory]
  [InlineData(AccountStatus.Inactive)]
  [InlineData(AccountStatus.Blocked)]
  public void Credit_throws_when_account_not_active(AccountStatus status)
  {
    var a = NewAccount(balance: 0, reserved: 0, creditLimit: 0, status: status);
    Assert.Throws<DomainException>(() => a.Credit(1));
  }

  [Theory]
  [InlineData(AccountStatus.Inactive)]
  [InlineData(AccountStatus.Blocked)]
  public void Debit_throws_when_account_not_active(AccountStatus status)
  {
    var a = NewAccount(balance: 10_000, reserved: 0, creditLimit: 0, status: status);
    Assert.Throws<DomainException>(() => a.Debit(1));
  }

  [Theory]
  [InlineData(AccountStatus.Inactive)]
  [InlineData(AccountStatus.Blocked)]
  public void Reserve_throws_when_account_not_active(AccountStatus status)
  {
    var a = NewAccount(balance: 10_000, reserved: 0, creditLimit: 0, status: status);
    Assert.Throws<DomainException>(() => a.Reserve(1));
  }

  [Theory]
  [InlineData(AccountStatus.Inactive)]
  [InlineData(AccountStatus.Blocked)]
  public void Capture_throws_when_account_not_active(AccountStatus status)
  {
    var a = NewAccount(balance: 10_000, reserved: 10_000, creditLimit: 0, status: status);
    Assert.Throws<DomainException>(() => a.Capture(1));
  }

  [Fact]
  public void EnsureCurrency_rejects_different_currency_even_if_only_case_differs_on_account_side()
  {
    var a = NewAccount(balance: 0, reserved: 0, creditLimit: 0, status: AccountStatus.Active, currency: "brl");

    a.EnsureCurrency("BRL");
    Assert.Throws<DomainException>(() => a.EnsureCurrency("USD"));
  }

  [Fact]
  public void EnsureActive_does_not_throw_when_active()
  {
    var a = NewAccount(balance: 0, reserved: 0, creditLimit: 0, status: AccountStatus.Active);
    a.EnsureActive();
  }
   [Theory]
  [InlineData(0)]
  [InlineData(-1)]
  public void Amount_must_be_positive(long amount)
  {
    var a = NewAccount(balance: 10_000, reserved: 0, creditLimit: 0, status: AccountStatus.Active);

    Assert.Throws<DomainException>(() => a.Credit(amount));
    Assert.Throws<DomainException>(() => a.Debit(amount));
    Assert.Throws<DomainException>(() => a.Reserve(amount));
    Assert.Throws<DomainException>(() => a.Capture(amount));
    Assert.Throws<DomainException>(() => a.ReleaseReservation(amount));
    Assert.Throws<DomainException>(() => a.RefundCapture(amount));
  }

  [Theory]
  [InlineData(AccountStatus.Inactive)]
  [InlineData(AccountStatus.Blocked)]
  public void EnsureActive_fails_for_non_active(AccountStatus status)
  {
    var a = NewAccount(balance: 0, reserved: 0, creditLimit: 0, status: status);
    var ex = Assert.Throws<DomainException>(() => a.EnsureActive());
    Assert.Contains("not active", ex.Message, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public void EnsureCurrency_is_case_insensitive_but_rejects_mismatch()
  {
    var a = NewAccount(balance: 0, reserved: 0, creditLimit: 0, status: AccountStatus.Active, currency: "BRL");

    a.EnsureCurrency("brl");
    Assert.Throws<DomainException>(() => a.EnsureCurrency("USD"));
  }

  [Fact]
  public void Capture_fails_when_reserved_insufficient()
  {
    var a = NewAccount(balance: 100_000, reserved: 10_000, creditLimit: 0, status: AccountStatus.Active);
    Assert.Throws<DomainException>(() => a.Capture(10_001));
  }

  [Fact]
  public void ReleaseReservation_decreases_only_reserved()
  {
    var a = NewAccount(balance: 100_000, reserved: 25_000, creditLimit: 0, status: AccountStatus.Active);
    a.ReleaseReservation(5_000);

    Assert.Equal(100_000, a.Balance);
    Assert.Equal(20_000, a.ReservedBalance);
    Assert.Equal(80_000, a.AvailableBalance);
  }

  [Fact]
  public void RefundCapture_increases_balance_only()
  {
    var a = NewAccount(balance: 70_000, reserved: 0, creditLimit: 0, status: AccountStatus.Active);
    a.RefundCapture(30_000);

    Assert.Equal(100_000, a.Balance);
    Assert.Equal(0, a.ReservedBalance);
    Assert.Equal(100_000, a.AvailableBalance);
  }

  [Fact]
  public void BuildLedgerEvent_increments_sequence_and_copies_fields()
  {
    var a = NewAccount(balance: 10_000, reserved: 0, creditLimit: 0, status: AccountStatus.Active);

    var req = new OperationRequest(
      Operation: OperationType.Credit,
      AccountId: a.AccountId,
      Amount: 10_000,
      Currency: "BRL",
      ReferenceId: "TXN-001",
      Metadata: new Dictionary<string, object?> { ["k"] = "v" }
    );

    var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    var e1 = a.BuildLedgerEvent(req, now, "Credited");
    var e2 = a.BuildLedgerEvent(req with { ReferenceId = "TXN-002" }, now, "Credited");

    Assert.Equal(1, e1.Sequence);
    Assert.Equal(2, e2.Sequence);
    Assert.Equal("Credited", e1.EventType);
    Assert.Equal("TXN-001", e1.ReferenceId);
    Assert.Equal("BRL", e1.Currency);
    Assert.Equal(10_000, e1.Amount);
    Assert.Equal("v", e1.Metadata["k"]);
  }

  [Theory]
  [InlineData(AccountStatus.Inactive)]
  [InlineData(AccountStatus.Blocked)]
  public void Debit_when_not_active_does_not_change_state(AccountStatus status)
  {
    var a = NewAccount(balance: 10_000, reserved: 2_000, creditLimit: 50_000, status: status);

    Assert.Throws<DomainException>(() => a.Debit(1));

    Assert.Equal(10_000, a.Balance);
    Assert.Equal(2_000, a.ReservedBalance);
    Assert.Equal(50_000, a.CreditLimit);
    Assert.Equal(status, a.Status);
  }

  private static Account NewAccount(long balance, long reserved, long creditLimit, AccountStatus status, string currency = "BRL") =>
    new(
      accountId: "ACC-001",
      clientId: "CLI-001",
      currency: currency,
      balance: balance,
      reservedBalance: reserved,
      creditLimit: creditLimit,
      status: status,
      ledgerSequence: 0
    );

  private static Account NewAccount(long balance, long creditLimit) =>
    new(
      accountId: "ACC-001",
      clientId: "CLI-001",
      currency: "BRL",
      balance: balance,
      reservedBalance: 0,
      creditLimit: creditLimit,
      status: AccountStatus.Active,
      ledgerSequence: 0
    );

}
