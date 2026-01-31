
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PagueVeloz.TransactionProcessor.IntegrationTests.Infrastructure;
using PagueVeloz.TransactionProcessor.Infrastructure.Persistence;
using PagueVeloz.TransactionProcessor.Infrastructure.Persistence.Entities;
using PagueVeloz.TransactionProcessor.Worker;
using Xunit;

namespace PagueVeloz.TransactionProcessor.IntegrationTests.Worker;

[Collection(IntegrationCollection.Name)]
public sealed class AsyncCommandQueueTests
{
  private readonly IntegrationTestFixture _fx;

  public AsyncCommandQueueTests(IntegrationTestFixture fx) => _fx = fx;

  [Fact]
  public async Task Enqueue_endpoint_persists_command_and_worker_processes_it()
  {
    var acc = NewId("ACC");
    await _fx.CreateAccountAsync(acc, initialBalance: 0, creditLimit: 0);

    var referenceId = "TXN-ASYNC-001-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
    var enqueueTx = Tx("credit", acc, 12_345, referenceId);

    var resp = await _fx.Client.PostAsJsonAsync("/api/transactions/enqueue", enqueueTx);
    Assert.Equal(HttpStatusCode.Accepted, resp.StatusCode);

    await using (var sp = _fx.BuildWorkerServiceProvider(_fx.RabbitHost, _fx.RabbitPort))
    await using (var scope = sp.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<TransactionDbContext>();

      var count = await db.QueuedCommands.CountAsync();
      if (count == 0)
        throw new Exception("No queued command persisted after /enqueue.");

      var processor = scope.ServiceProvider.GetRequiredService<QueuedCommandProcessor>();
      await processor.RunOnceAsync(CancellationToken.None);

      var cmd = await FindLatestQueuedCommandByReferenceId(db, referenceId);
      Assert.NotNull(cmd);
      Assert.Equal("Done", cmd!.Status);
      Assert.True(string.IsNullOrWhiteSpace(cmd.ErrorMessage), $"Unexpected error: {cmd.ErrorMessage}");
    }

    var txRes = await _fx.Client.GetFromJsonAsync<Dictionary<string, object?>>($"/api/transactions/{referenceId}");
    Assert.NotNull(txRes);
    Assert.Equal("success", GetString(txRes!, "status"));
    Assert.Equal("12345", GetString(txRes!, "balance"));
  }

  [Fact]
  public async Task Enqueue_invalid_payload_returns_400_and_does_not_persist_command()
  {
    var before = await CountQueuedCommandsAsync();

    var req = new Dictionary<string, object?>
    {
      ["operation"] = "credit",
      ["amount"] = 1000,
      ["currency"] = "BRL",
      ["reference_id"] = "TXN-ASYNC-VAL-001"
    };

    var resp = await _fx.Client.PostAsJsonAsync("/api/transactions/enqueue", req);
    Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);

    var after = await CountQueuedCommandsAsync();
    Assert.Equal(before, after);
  }

  [Fact]
  public async Task Enqueue_same_reference_id_twice_is_idempotent_no_double_apply()
  {
    var acc = NewId("ACC");
    await _fx.CreateAccountAsync(acc, initialBalance: 0, creditLimit: 0);

    var referenceId = "TXN-ASYNC-IDEMP-001-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
    var enqueueTx = Tx("credit", acc, 1_000, referenceId);

    var r1 = await _fx.Client.PostAsJsonAsync("/api/transactions/enqueue", enqueueTx);
    var r2 = await _fx.Client.PostAsJsonAsync("/api/transactions/enqueue", enqueueTx);

    Assert.Equal(HttpStatusCode.Accepted, r1.StatusCode);
    Assert.Equal(HttpStatusCode.Accepted, r2.StatusCode);

    await using (var sp = _fx.BuildWorkerServiceProvider(_fx.RabbitHost, _fx.RabbitPort))
    await using (var scope = sp.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<TransactionDbContext>();
      var processor = scope.ServiceProvider.GetRequiredService<QueuedCommandProcessor>();

      for (var i = 0; i < 10; i++)
        await processor.RunOnceAsync(CancellationToken.None);

      var cmds = await FindQueuedCommandsByReferenceId(db, referenceId);
      Assert.NotEmpty(cmds);
      Assert.All(cmds, c => Assert.True(IsTerminal(c.Status), $"Non-terminal status: {c.Status}"));
    }

    var txRes = await _fx.Client.GetFromJsonAsync<Dictionary<string, object?>>($"/api/transactions/{referenceId}");
    Assert.NotNull(txRes);
    Assert.Equal("success", GetString(txRes!, "status"));

    var accState = await _fx.Client.GetFromJsonAsync<Dictionary<string, object?>>($"/api/accounts/{acc}");
    Assert.NotNull(accState);
    Assert.Equal("1000", GetString(accState!, "balance"));
  }

  [Fact]
  public async Task Enqueue_debit_without_funds_keeps_balance_and_marks_command_failed_or_done_with_error()
  {
    var acc = NewId("ACC");
    await _fx.CreateAccountAsync(acc, initialBalance: 0, creditLimit: 0);

    var referenceId = "TXN-ASYNC-NOFUNDS-001-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
    var enqueueTx = Tx("debit", acc, 1_000, referenceId);

    var resp = await _fx.Client.PostAsJsonAsync("/api/transactions/enqueue", enqueueTx);
    Assert.Equal(HttpStatusCode.Accepted, resp.StatusCode);

    await using (var sp = _fx.BuildWorkerServiceProvider(_fx.RabbitHost, _fx.RabbitPort))
    await using (var scope = sp.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<TransactionDbContext>();
      var processor = scope.ServiceProvider.GetRequiredService<QueuedCommandProcessor>();

      for (var i = 0; i < 10; i++)
        await processor.RunOnceAsync(CancellationToken.None);

      var cmd = await FindLatestQueuedCommandByReferenceId(db, referenceId);
      Assert.NotNull(cmd);
      Assert.True(IsTerminal(cmd!.Status), $"Status inesperado: {cmd.Status}");

      if (string.Equals(cmd.Status, "Failed", StringComparison.OrdinalIgnoreCase))
        Assert.False(string.IsNullOrWhiteSpace(cmd.ErrorMessage), "Failed sem ErrorMessage.");
    }

    var accState = await _fx.Client.GetFromJsonAsync<Dictionary<string, object?>>($"/api/accounts/{acc}");
    Assert.NotNull(accState);
    Assert.Equal("0", GetString(accState!, "balance"));

    var get = await _fx.Client.GetAsync($"/api/transactions/{referenceId}");
    Assert.True(get.StatusCode is HttpStatusCode.OK or HttpStatusCode.NotFound);
  }

  [Fact]
  public async Task Enqueue_transfer_to_missing_target_does_not_debit_source_and_marks_command_failed_or_done_with_error()
  {
    var from = NewId("ACC");
    await _fx.CreateAccountAsync(from, initialBalance: 10_000, creditLimit: 0);

    var missing = NewId("ACC");
    var referenceId = "TXN-ASYNC-MISS-001-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    var req = new Dictionary<string, object?>
    {
      ["operation"] = "transfer",
      ["account_id"] = from,
      ["target_account_id"] = missing,
      ["amount"] = 5_000,
      ["currency"] = "BRL",
      ["reference_id"] = referenceId,
      ["metadata"] = new Dictionary<string, object?> { ["test"] = true }
    };

    var resp = await _fx.Client.PostAsJsonAsync("/api/transactions/enqueue", req);
    Assert.Equal(HttpStatusCode.Accepted, resp.StatusCode);

    await using (var sp = _fx.BuildWorkerServiceProvider(_fx.RabbitHost, _fx.RabbitPort))
    await using (var scope = sp.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<TransactionDbContext>();
      var processor = scope.ServiceProvider.GetRequiredService<QueuedCommandProcessor>();

      for (var i = 0; i < 10; i++)
        await processor.RunOnceAsync(CancellationToken.None);

      var cmd = await FindLatestQueuedCommandByReferenceId(db, referenceId);
      Assert.NotNull(cmd);
      Assert.True(IsTerminal(cmd!.Status), $"Status inesperado: {cmd.Status}");

      if (string.Equals(cmd.Status, "Failed", StringComparison.OrdinalIgnoreCase))
        Assert.False(string.IsNullOrWhiteSpace(cmd.ErrorMessage), "Failed sem ErrorMessage.");
    }

    var fromState = await _fx.Client.GetFromJsonAsync<Dictionary<string, object?>>($"/api/accounts/{from}");
    Assert.NotNull(fromState);
    Assert.Equal("10000", GetString(fromState!, "balance"));
  }

  [Fact]
  public async Task Enqueue_two_credits_worker_processes_both_balance_matches_sum()
  {
    var acc = NewId("ACC");
    await _fx.CreateAccountAsync(acc, initialBalance: 0, creditLimit: 0);

    var r1 = "TXN-ASYNC-CC-1-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
    var r2 = "TXN-ASYNC-CC-2-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    var e1 = Tx("credit", acc, 1_000, r1);
    var e2 = Tx("credit", acc, 2_000, r2);

    var p1 = _fx.Client.PostAsJsonAsync("/api/transactions/enqueue", e1);
    var p2 = _fx.Client.PostAsJsonAsync("/api/transactions/enqueue", e2);

    var resp = await Task.WhenAll(p1, p2);
    Assert.All(resp, x => Assert.Equal(HttpStatusCode.Accepted, x.StatusCode));

    await using (var sp = _fx.BuildWorkerServiceProvider(_fx.RabbitHost, _fx.RabbitPort))
    await using (var scope = sp.CreateAsyncScope())
    {
      var processor = scope.ServiceProvider.GetRequiredService<QueuedCommandProcessor>();
      for (var i = 0; i < 10; i++)
        await processor.RunOnceAsync(CancellationToken.None);
    }

    var a = await _fx.Client.GetFromJsonAsync<Dictionary<string, object?>>($"/api/accounts/{acc}");
    Assert.NotNull(a);
    Assert.Equal("3000", GetString(a!, "balance"));
  }

  private async Task<int> CountQueuedCommandsAsync()
  {
    await using var sp = _fx.BuildWorkerServiceProvider(_fx.RabbitHost, _fx.RabbitPort);
    await using var scope = sp.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<TransactionDbContext>();
    return await db.QueuedCommands.CountAsync();
  }

  private static async Task<List<QueuedCommandEntity>> FindQueuedCommandsByReferenceId(TransactionDbContext db, string referenceId)
  {
    var candidates = await db.QueuedCommands
      .Where(x => x.PayloadJson.Contains(referenceId))
      .OrderByDescending(x => x.EnqueuedAt)
      .ToListAsync();

    return candidates
      .Where(c => string.Equals(ExtractReferenceId(c.PayloadJson), referenceId, StringComparison.Ordinal))
      .ToList();
  }

  private static async Task<QueuedCommandEntity?> FindLatestQueuedCommandByReferenceId(TransactionDbContext db, string referenceId)
  {
    var list = await FindQueuedCommandsByReferenceId(db, referenceId);
    return list.OrderByDescending(x => x.EnqueuedAt).FirstOrDefault();
  }

  private static string ExtractReferenceId(string payloadJson)
  {
    try
    {
      using var doc = JsonDocument.Parse(payloadJson);
      var root = doc.RootElement;

      if (root.ValueKind != JsonValueKind.Object) return "";

      if (root.TryGetProperty("reference_id", out var a) && a.ValueKind == JsonValueKind.String)
        return a.GetString() ?? "";

      if (root.TryGetProperty("referenceId", out var b) && b.ValueKind == JsonValueKind.String)
        return b.GetString() ?? "";

      return "";
    }
    catch
    {
      return "";
    }
  }

  private static bool IsTerminal(string? status)
    => string.Equals(status, "Done", StringComparison.OrdinalIgnoreCase)
       || string.Equals(status, "Failed", StringComparison.OrdinalIgnoreCase);

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

  private static string NewId(string prefix)
    => $"{prefix}-{Guid.NewGuid():N}"[..16].ToUpperInvariant();

  private static string GetString(Dictionary<string, object?> d, string key)
  {
    if (!d.TryGetValue(key, out var v) || v is null) return "";
    if (v is JsonElement je)
    {
      if (je.ValueKind == JsonValueKind.String) return je.GetString() ?? "";
      return je.ToString();
    }
    return v.ToString() ?? "";
  }
}

