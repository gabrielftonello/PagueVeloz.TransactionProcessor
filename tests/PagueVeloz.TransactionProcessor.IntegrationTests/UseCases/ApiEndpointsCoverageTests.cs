using System.Net;
using System.Net.Http.Json;
using PagueVeloz.TransactionProcessor.IntegrationTests.Infrastructure;
using Xunit;

namespace PagueVeloz.TransactionProcessor.IntegrationTests.UseCases;

[Collection(IntegrationCollection.Name)]
public sealed class ApiEndpointsCoverageTests
{
  private readonly IntegrationTestFixture _fx;

  public ApiEndpointsCoverageTests(IntegrationTestFixture fx) => _fx = fx;

  [Fact]
  public async Task Health_live_returns_200()
  {
    var resp = await _fx.Client.GetAsync("/health/live");
    Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
  }

  [Fact]
  public async Task Health_ready_returns_200()
  {
    var resp = await _fx.Client.GetAsync("/health/ready");
    Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
  }

  [Fact]
  public async Task Create_account_without_client_id_returns_400()
  {
    var req = new Dictionary<string, object?>
    {
      ["account_id"] = NewId("ACC"),
      ["initial_balance"] = 10_000,
      ["credit_limit"] = 0,
      ["currency"] = "BRL"
    };

    var resp = await _fx.Client.PostAsJsonAsync("/api/accounts", req);
    Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
  }

  [Fact]
  public async Task Get_account_not_found_returns_404()
  {
    var resp = await _fx.Client.GetAsync($"/api/accounts/{NewId("ACC")}");
    Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
  }

  [Fact]
  public async Task Get_account_transactions_for_existing_account_returns_200_and_empty_list()
  {
    var acc = NewId("ACC");
    await _fx.CreateAccountAsync(acc, initialBalance: 0, creditLimit: 0);

    var resp = await _fx.Client.GetAsync($"/api/accounts/{acc}/transactions");
    Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

    var list = await resp.Content.ReadFromJsonAsync<List<Dictionary<string, object?>>>();
    Assert.NotNull(list);
    Assert.Empty(list!);
  }

  [Fact]
  public async Task Get_account_transactions_not_found_returns_404()
  {
    var acc = NewId("ACC");
    var resp = await _fx.Client.GetAsync($"/api/accounts/{acc}/transactions");
    Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
  }

  [Fact]
  public async Task Transaction_without_reference_id_returns_400_validation_error()
  {
    var acc = NewId("ACC");
    await _fx.CreateAccountAsync(acc, initialBalance: 10_000, creditLimit: 0);

    var req = new Dictionary<string, object?>
    {
      ["operation"] = "debit",
      ["account_id"] = acc,
      ["amount"] = 1_000,
      ["currency"] = "BRL"
    };

    var resp = await _fx.Client.PostAsJsonAsync("/api/transactions", req);
    Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
  }

  [Fact]
  public async Task Transaction_with_invalid_operation_returns_400_validation_error()
  {
    var acc = NewId("ACC");
    await _fx.CreateAccountAsync(acc, initialBalance: 10_000, creditLimit: 0);

    var req = new Dictionary<string, object?>
    {
      ["operation"] = "nope",
      ["account_id"] = acc,
      ["amount"] = 1_000,
      ["currency"] = "BRL",
      ["reference_id"] = "TXN-VAL-OP-001"
    };

    var resp = await _fx.Client.PostAsJsonAsync("/api/transactions", req);
    Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
  }

  [Fact]
  public async Task Transaction_amount_negative_returns_400_validation_error()
  {
    var acc = NewId("ACC");
    await _fx.CreateAccountAsync(acc, initialBalance: 10_000, creditLimit: 0);

    var req = new Dictionary<string, object?>
    {
      ["operation"] = "credit",
      ["account_id"] = acc,
      ["amount"] = -1,
      ["currency"] = "BRL",
      ["reference_id"] = "TXN-VAL-AMT-NEG-001"
    };

    var resp = await _fx.Client.PostAsJsonAsync("/api/transactions", req);
    Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
  }

  [Fact]
  public async Task Capture_without_related_reference_id_returns_400_validation_error()
  {
    var acc = NewId("ACC");
    await _fx.CreateAccountAsync(acc, initialBalance: 10_000, creditLimit: 0);

    var req = new Dictionary<string, object?>
    {
      ["operation"] = "capture",
      ["account_id"] = acc,
      ["amount"] = 1_000,
      ["currency"] = "BRL",
      ["reference_id"] = "TXN-VAL-CAP-001"
    };

    var resp = await _fx.Client.PostAsJsonAsync("/api/transactions", req);
    Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
  }

  [Fact]
  public async Task Get_transaction_by_reference_not_found_returns_404()
  {
    var resp = await _fx.Client.GetAsync($"/api/transactions/{NewId("TXN")}");
    Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
  }

  [Fact]
  public async Task After_processing_transaction_get_by_reference_returns_200()
  {
    var acc = NewId("ACC");
    await _fx.CreateAccountAsync(acc, initialBalance: 10_000, creditLimit: 0);

    var refId = "TXN-GET-001";
    var req = new Dictionary<string, object?>
    {
      ["operation"] = "debit",
      ["account_id"] = acc,
      ["amount"] = 1_000,
      ["currency"] = "BRL",
      ["reference_id"] = refId,
      ["metadata"] = new Dictionary<string, object?>()
    };

    var post = await _fx.Client.PostAsJsonAsync("/api/transactions", req);
    Assert.Equal(HttpStatusCode.OK, post.StatusCode);

    var postBody = await post.Content.ReadFromJsonAsync<Dictionary<string, object?>>();
    Assert.NotNull(postBody);

    var postTxId = GetString(postBody!, "transaction_id");
    Assert.False(string.IsNullOrWhiteSpace(postTxId));

    var get = await _fx.Client.GetAsync($"/api/transactions/{refId}");
    Assert.Equal(HttpStatusCode.OK, get.StatusCode);

    var getBody = await get.Content.ReadFromJsonAsync<Dictionary<string, object?>>();
    Assert.NotNull(getBody);

    var getTxId = GetString(getBody!, "transaction_id");
    Assert.Equal(postTxId, getTxId);
  }

  [Fact]
  public async Task Enqueue_valid_transaction_returns_202()
  {
    var acc = NewId("ACC");
    await _fx.CreateAccountAsync(acc, initialBalance: 10_000, creditLimit: 0);

    var req = new Dictionary<string, object?>
    {
      ["operation"] = "debit",
      ["account_id"] = acc,
      ["amount"] = 1_000,
      ["currency"] = "BRL",
      ["reference_id"] = "TXN-ENQ-001",
      ["metadata"] = new Dictionary<string, object?>()
    };

    var resp = await _fx.Client.PostAsJsonAsync("/api/transactions/enqueue", req);
    Assert.Equal(HttpStatusCode.Accepted, resp.StatusCode);
  }

  [Fact]
  public async Task Enqueue_invalid_transaction_returns_400()
  {
    var req = new Dictionary<string, object?>
    {
      ["operation"] = "debit",
      ["account_id"] = "ACC-DOES-NOT-MATTER",
      ["amount"] = 1_000,
      ["currency"] = "BRL"
    };

    var resp = await _fx.Client.PostAsJsonAsync("/api/transactions/enqueue", req);
    Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
  }

  private static string NewId(string prefix)
    => $"{prefix}-{Guid.NewGuid():N}"[..16].ToUpperInvariant();

  private static string GetString(Dictionary<string, object?> d, string key)
  {
    if (!d.TryGetValue(key, out var v) || v is null) return "";

    if (v is System.Text.Json.JsonElement je)
    {
      if (je.ValueKind == System.Text.Json.JsonValueKind.String) return je.GetString() ?? "";
      return je.ToString();
    }

    return v.ToString() ?? "";
  }

}
