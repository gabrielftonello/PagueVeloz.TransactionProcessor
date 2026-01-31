using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PagueVeloz.TransactionProcessor.IntegrationTests.Infrastructure;
using PagueVeloz.TransactionProcessor.Infrastructure.Persistence;
using Xunit;

namespace PagueVeloz.TransactionProcessor.IntegrationTests.UseCases;

[Collection(IntegrationCollection.Name)]
public sealed class BusinessRulesValidationTests
{
  private readonly IntegrationTestFixture _fx;

  public BusinessRulesValidationTests(IntegrationTestFixture fx) => _fx = fx;

  [Fact]
  public async Task Currency_mismatch_returns_failed_transaction_and_does_not_change_balance()
  {
    var acc = NewId("ACC");
    await _fx.CreateAccountAsync(acc, initialBalance: 10_000, creditLimit: 0, currency: "BRL");

    var tx = Tx("credit", acc, 1_000, "TXN-CUR-001", currency: "USD");
    var (status, res) = await PostTx(tx);

    Assert.Contains(status, new[] { HttpStatusCode.BadRequest, HttpStatusCode.UnprocessableEntity });
    Assert.Equal("failed", res["status"]?.ToString());
    Assert.Contains("currency", res["error_message"]?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);

    var a = await _fx.GetAccountAsync(acc);
    Assert.Equal("10000", a["balance"]?.ToString());
  }

  [Fact]
  public async Task Blocked_account_returns_failed_and_preserves_balances()
  {
    var acc = NewId("ACC");
    await _fx.CreateAccountAsync(acc, initialBalance: 10_000, creditLimit: 0);

    await using var sp = _fx.BuildWorkerServiceProvider(_fx.RabbitHost, _fx.RabbitPort);
    await using (var scope = sp.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<TransactionDbContext>();
      await db.Database.ExecuteSqlRawAsync("UPDATE Accounts SET Status = 3 WHERE AccountId = {0}", acc);
    }

    var (status, res) = await PostTx(Tx("debit", acc, 1_000, "TXN-BLOCK-001"));

    Assert.Contains(status, new[] { HttpStatusCode.BadRequest, HttpStatusCode.UnprocessableEntity });
    Assert.Equal("failed", res["status"]?.ToString());
    Assert.Contains("not active", res["error_message"]?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);

    var a = await _fx.GetAccountAsync(acc);
    Assert.Equal("10000", a["balance"]?.ToString());
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
