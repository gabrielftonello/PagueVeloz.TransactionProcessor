using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using PagueVeloz.TransactionProcessor.IntegrationTests.Infrastructure;
using Xunit;

namespace PagueVeloz.TransactionProcessor.IntegrationTests.UseCases;

[Collection(IntegrationCollection.Name)]
public sealed class AccountTransactionsHistoryTests
{
  private readonly IntegrationTestFixture _fx;

  public AccountTransactionsHistoryTests(IntegrationTestFixture fx) => _fx = fx;

  [Fact]
  public async Task Get_transactions_by_account_guid_returns_all_transactions_for_that_account()
  {
    var accountA = Guid.NewGuid();
    var accountB = Guid.NewGuid();

    await _fx.CreateAccountAsync(accountA.ToString("D"), initialBalance: 0, creditLimit: 0);
    await _fx.CreateAccountAsync(accountB.ToString("D"), initialBalance: 0, creditLimit: 0);

    await _fx.ProcessTransactionAsync(NewTx(operation: "credit", accountId: accountA.ToString("D"), amount: 1_000, referenceId: NewId("TXN")));
    await _fx.ProcessTransactionAsync(NewTx(operation: "debit",  accountId: accountA.ToString("D"), amount:   250, referenceId: NewId("TXN")));

    await _fx.ProcessTransactionAsync(NewTx(operation: "credit", accountId: accountB.ToString("D"), amount:   100, referenceId: NewId("TXN")));

    var txA = await _fx.GetAccountTransactionsAsync(accountA);
    var txB = await _fx.GetAccountTransactionsAsync(accountB);

    Assert.NotNull(txA);
    Assert.NotNull(txB);

    Assert.Equal(2, txA.Count);
    Assert.Equal(1, txB.Count);

    foreach (var x in txA.Concat(txB))
    {
      Assert.True(x.ContainsKey("transaction_id"));
      Assert.True(x.ContainsKey("status"));
      Assert.True(x.ContainsKey("balance"));
      Assert.True(x.ContainsKey("reserved_balance"));
      Assert.True(x.ContainsKey("available_balance"));
      Assert.True(x.ContainsKey("timestamp"));

      var txId = x["transaction_id"]?.ToString();
      Assert.False(string.IsNullOrWhiteSpace(txId));

      var ts = x["timestamp"]?.ToString();
      Assert.False(string.IsNullOrWhiteSpace(ts));

      _ = DateTimeOffset.Parse(
        ts!,
        CultureInfo.InvariantCulture,
        DateTimeStyles.RoundtripKind);
    }

    var idsA = txA.Select(x => x["transaction_id"]!.ToString()).ToHashSet(StringComparer.Ordinal);
    var idsB = txB.Select(x => x["transaction_id"]!.ToString()).ToHashSet(StringComparer.Ordinal);

    Assert.Empty(idsA.Intersect(idsB));
  }

  [Fact]
  public async Task Get_transactions_by_account_guid_returns_404_when_account_does_not_exist()
  {
    var response = await _fx.Client.GetAsync($"/api/accounts/{Guid.NewGuid():D}/transactions");
    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }

  private static Dictionary<string, object?> NewTx(string operation, string accountId, long amount, string referenceId)
    => new()
    {
      ["operation"] = operation,
      ["account_id"] = accountId,
      ["amount"] = amount,
      ["currency"] = "BRL",
      ["reference_id"] = referenceId
    };

  private static string NewId(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..16].ToUpperInvariant();
}
