using System.Net;
using System.Net.Http.Json;
using PagueVeloz.TransactionProcessor.IntegrationTests.Infrastructure;
using Xunit;

namespace PagueVeloz.TransactionProcessor.IntegrationTests.UseCases;

[Collection(IntegrationCollection.Name)]
public sealed class ApiValidationTests
{
  private readonly IntegrationTestFixture _fx;

  public ApiValidationTests(IntegrationTestFixture fx) => _fx = fx;

  [Fact]
  public async Task Transfer_without_target_account_id_returns_400_validation_error()
  {
    var acc = NewId("ACC");
    await _fx.CreateAccountAsync(acc, initialBalance: 10_000, creditLimit: 0);

    var req = new Dictionary<string, object?>
    {
      ["operation"] = "transfer",
      ["account_id"] = acc,
      ["amount"] = 1_000,
      ["currency"] = "BRL",
      ["reference_id"] = "TXN-VAL-TR-001"
    };

    var resp = await _fx.Client.PostAsJsonAsync("/api/transactions", req);
    Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
  }

  [Fact]
  public async Task Reversal_without_related_reference_id_returns_400_validation_error()
  {
    var acc = NewId("ACC");
    await _fx.CreateAccountAsync(acc, initialBalance: 10_000, creditLimit: 0);

    var req = new Dictionary<string, object?>
    {
      ["operation"] = "reversal",
      ["account_id"] = acc,
      ["amount"] = 1_000,
      ["currency"] = "BRL",
      ["reference_id"] = "TXN-VAL-REV-001"
    };

    var resp = await _fx.Client.PostAsJsonAsync("/api/transactions", req);
    Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
  }

  [Fact]
  public async Task Amount_zero_returns_400_validation_error()
  {
    var acc = NewId("ACC");
    await _fx.CreateAccountAsync(acc, initialBalance: 10_000, creditLimit: 0);

    var req = new Dictionary<string, object?>
    {
      ["operation"] = "credit",
      ["account_id"] = acc,
      ["amount"] = 0,
      ["currency"] = "BRL",
      ["reference_id"] = "TXN-VAL-AMT-001"
    };

    var resp = await _fx.Client.PostAsJsonAsync("/api/transactions", req);
    Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
  }

  private static string NewId(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..16].ToUpperInvariant();
}
