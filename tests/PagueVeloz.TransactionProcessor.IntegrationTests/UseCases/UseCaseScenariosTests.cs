using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PagueVeloz.TransactionProcessor.IntegrationTests.Infrastructure;
using Xunit;

namespace PagueVeloz.TransactionProcessor.IntegrationTests.UseCases;

[Collection(IntegrationCollection.Name)]
public sealed class UseCaseScenariosTests
{
  private readonly IntegrationTestFixture _fx;

  public UseCaseScenariosTests(IntegrationTestFixture fx) => _fx = fx;

  [Fact]
  public async Task Basic_credit_and_debit_sequence_matches_expected_balances()
  {
    var acc = NewId("ACC");
    await _fx.CreateAccountAsync(acc, initialBalance: 0, creditLimit: 0);

    var (s1, r1) = await PostTx(Tx("credit", acc, 100_000, "TXN-001"));
    Assert.Equal(HttpStatusCode.OK, s1);
    Assert.Equal("success", r1["status"]?.ToString());
    Assert.Equal("100000", r1["balance"]?.ToString());

    var (s2, r2) = await PostTx(Tx("debit", acc, 20_000, "TXN-002"));
    Assert.Equal(HttpStatusCode.OK, s2);
    Assert.Equal("success", r2["status"]?.ToString());
    Assert.Equal("80000", r2["balance"]?.ToString());

    var (s3, r3) = await PostTx(Tx("debit", acc, 90_000, "TXN-003"));
    Assert.Contains(s3, new[] { HttpStatusCode.BadRequest, HttpStatusCode.UnprocessableEntity });
    Assert.Equal("failed", r3["status"]?.ToString());
    Assert.Equal("80000", r3["balance"]?.ToString());

    var a = await _fx.GetAccountAsync(acc);
    Assert.Equal("80000", a["balance"]?.ToString());
    Assert.Equal("0", a["reserved_balance"]?.ToString());
    Assert.Equal("80000", a["available_balance"]?.ToString());
  }

  [Fact]
  public async Task Credit_limit_allows_negative_balance_until_limit_exceeded()
  {
    var acc = NewId("ACC");
    await _fx.CreateAccountAsync(acc, initialBalance: 30_000, creditLimit: 50_000);

    var (s1, ok) = await PostTx(Tx("debit", acc, 60_000, "TXN-CL-001"));
    Assert.Equal(HttpStatusCode.OK, s1);
    Assert.Equal("success", ok["status"]?.ToString());
    Assert.Equal("-30000", ok["balance"]?.ToString());

    var (s2, fail) = await PostTx(Tx("debit", acc, 30_000, "TXN-CL-002"));
    Assert.Contains(s2, new[] { HttpStatusCode.BadRequest, HttpStatusCode.UnprocessableEntity });
    Assert.Equal("failed", fail["status"]?.ToString());
    Assert.Equal("-30000", fail["balance"]?.ToString());
  }

  [Fact]
  public async Task Reserve_then_capture_moves_between_available_and_reserved()
  {
    var acc = NewId("ACC");
    await _fx.CreateAccountAsync(acc, initialBalance: 100_000, creditLimit: 0);

    var (s1, reserve) = await PostTx(Tx("reserve", acc, 30_000, "TXN-RES-001"));
    Assert.Equal(HttpStatusCode.OK, s1);
    Assert.Equal("success", reserve["status"]?.ToString());
    Assert.Equal("100000", reserve["balance"]?.ToString());
    Assert.Equal("30000", reserve["reserved_balance"]?.ToString());
    Assert.Equal("70000", reserve["available_balance"]?.ToString());

    var captureTx = Tx("capture", acc, 30_000, "TXN-CAP-001");
    captureTx["related_reference_id"] = "TXN-RES-001";

    var (s2, capture) = await PostTx(captureTx);
    Assert.Equal(HttpStatusCode.OK, s2);
    Assert.Equal("success", capture["status"]?.ToString());
    Assert.Equal("70000", capture["balance"]?.ToString());
    Assert.Equal("0", capture["reserved_balance"]?.ToString());
    Assert.Equal("70000", capture["available_balance"]?.ToString());
  }

  [Fact]
  public async Task Transfer_moves_value_between_two_accounts_atomically()
  {
    var source = NewId("ACC");
    var dest = NewId("ACC");

    await _fx.CreateAccountAsync(source, initialBalance: 100_000, creditLimit: 0);
    await _fx.CreateAccountAsync(dest, initialBalance: 50_000, creditLimit: 0);

    var tx = Tx("transfer", source, 50_000, "TXN-TR-001");
    tx["target_account_id"] = dest;

    var (s, res) = await PostTx(tx);
    Assert.Equal(HttpStatusCode.OK, s);
    Assert.Equal("success", res["status"]?.ToString());

    var a1 = await _fx.GetAccountAsync(source);
    var a2 = await _fx.GetAccountAsync(dest);
    Assert.Equal("50000", a1["balance"]?.ToString());
    Assert.Equal("100000", a2["balance"]?.ToString());
  }

  [Fact]
  public async Task Reversal_reverts_a_previous_credit_and_is_idempotently_blocked_after_first_reversal()
  {
    var acc = NewId("ACC");
    await _fx.CreateAccountAsync(acc, initialBalance: 0, creditLimit: 0);

    var (s1, credit) = await PostTx(Tx("credit", acc, 10_000, "TXN-CR-001"));
    Assert.Equal(HttpStatusCode.OK, s1);
    Assert.Equal("success", credit["status"]?.ToString());
    Assert.Equal("10000", credit["balance"]?.ToString());

    var revTx = Tx("reversal", acc, 10_000, "TXN-REV-001");
    revTx["related_reference_id"] = "TXN-CR-001";

    var (s2, rev) = await PostTx(revTx);
    Assert.Equal(HttpStatusCode.OK, s2);
    Assert.Equal("success", rev["status"]?.ToString());

    var after = await _fx.GetAccountAsync(acc);
    Assert.Equal("0", after["balance"]?.ToString());

    var rev2Tx = Tx("reversal", acc, 10_000, "TXN-REV-002");
    rev2Tx["related_reference_id"] = "TXN-CR-001";

    var (s3, rev2) = await PostTx(rev2Tx);
    Assert.Contains(s3, new[] { HttpStatusCode.BadRequest, HttpStatusCode.UnprocessableEntity });
    Assert.Equal("failed", rev2["status"]?.ToString());
    Assert.Contains("already reversed", rev2["error_message"]?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public async Task Reversal_reverts_a_previous_transfer_for_both_accounts()
  {
    var source = NewId("ACC");
    var dest = NewId("ACC");
    await _fx.CreateAccountAsync(source, initialBalance: 100_000, creditLimit: 0);
    await _fx.CreateAccountAsync(dest, initialBalance: 0, creditLimit: 0);

    var transferTx = Tx("transfer", source, 30_000, "TXN-TR-REV-001");
    transferTx["target_account_id"] = dest;

    var (s1, transfer) = await PostTx(transferTx);
    Assert.Equal(HttpStatusCode.OK, s1);
    Assert.Equal("success", transfer["status"]?.ToString());

    var revTx = Tx("reversal", source, 30_000, "TXN-TR-REV-002");
    revTx["related_reference_id"] = "TXN-TR-REV-001";

    var (s2, rev) = await PostTx(revTx);
    Assert.Equal(HttpStatusCode.OK, s2);
    Assert.Equal("success", rev["status"]?.ToString());

    var a1 = await _fx.GetAccountAsync(source);
    var a2 = await _fx.GetAccountAsync(dest);
    Assert.Equal("100000", a1["balance"]?.ToString());
    Assert.Equal("0", a2["balance"]?.ToString());
  }

  private async Task<(HttpStatusCode StatusCode, Dictionary<string, object?> Body)> PostTx(Dictionary<string, object?> tx)
  {
    var resp = await _fx.Client.PostAsJsonAsync("/api/transactions", tx);
    var bodyText = await resp.Content.ReadAsStringAsync();

    if (resp.StatusCode == HttpStatusCode.InternalServerError)
      throw new InvalidOperationException($"Server returned 500. Body={bodyText}");

    Dictionary<string, object?>? body;
    try
    {
      body = JsonSerializer.Deserialize<Dictionary<string, object?>>(bodyText, new JsonSerializerOptions
      {
        PropertyNameCaseInsensitive = true
      });
    }
    catch
    {
      throw new InvalidOperationException($"Invalid JSON. Status={(int)resp.StatusCode} {resp.ReasonPhrase}. Body={bodyText}");
    }

    if (body is null)
      throw new InvalidOperationException($"Empty body. Status={(int)resp.StatusCode} {resp.ReasonPhrase}. Body={bodyText}");

    return (resp.StatusCode, body);
  }

  private static Dictionary<string, object?> Tx(string operation, string accountId, long amount, string referenceId, string currency = "BRL")
    => new()
    {
      ["operation"] = operation,
      ["account_id"] = accountId,
      ["amount"] = amount,
      ["currency"] = currency,
      ["reference_id"] = referenceId,
      ["metadata"] = new Dictionary<string, object?> { ["test"] = true }
    };

  private static string NewId(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..16].ToUpperInvariant();
}
