using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.MsSql;
using Xunit;
using Xunit.Abstractions;

namespace PagueVeloz.TransactionProcessor.IntegrationTests.UseCases;

public sealed class ConcurrencyAndIdempotencyTests : IAsyncLifetime
{
  private readonly MsSqlContainer _db;
  private HttpClient _client = default!;
  private readonly ITestOutputHelper _output;

  public ConcurrencyAndIdempotencyTests(ITestOutputHelper output)
  {
    _output = output;
    _db = new MsSqlBuilder().WithPassword("Your_password123").Build();
  }

  public async Task InitializeAsync()
  {
    await _db.StartAsync();
    var factory = new CustomFactory(_db.GetConnectionString(), _output);
    _client = factory.CreateClient();
  }

  public async Task DisposeAsync()
  {
    _client.Dispose();
    await _db.DisposeAsync();
  }

  [Fact]
  public async Task Concurrent_debits_do_not_overdraw()
  {
    var createAcc = new Dictionary<string, object?>
    {
      ["client_id"] = "CLI-TEST",
      ["account_id"] = "ACC-TEST-1",
      ["initial_balance"] = 10_000,
      ["credit_limit"] = 0,
      ["currency"] = "BRL"
    };

    var accResp = await _client.PostAsJsonAsync("/api/accounts", createAcc);
    await DumpIfNotExpected(accResp, HttpStatusCode.Created, "POST /api/accounts");
    Assert.Equal(HttpStatusCode.Created, accResp.StatusCode);

    var t1 = DebitAsync("TXN-DEBIT-1");
    var t2 = DebitAsync("TXN-DEBIT-2");

    await Task.WhenAll(t1, t2);

    var r1 = t1.Result;
    var r2 = t2.Result;

    Assert.Contains(r1["status"]?.ToString(), new[] { "success", "failed" });
    Assert.Contains(r2["status"]?.ToString(), new[] { "success", "failed" });
    Assert.NotEqual(r1["status"]?.ToString(), r2["status"]?.ToString());

    var bal = await _client.GetFromJsonAsync<Dictionary<string, object?>>("/api/accounts/ACC-TEST-1");
    Assert.NotNull(bal);
    Assert.Equal("3000", bal!["balance"]!.ToString());

    async Task<Dictionary<string, object?>> DebitAsync(string referenceId)
    {
      var tx = new Dictionary<string, object?>
      {
        ["operation"] = "debit",
        ["account_id"] = "ACC-TEST-1",
        ["amount"] = 7_000,
        ["currency"] = "BRL",
        ["reference_id"] = referenceId,
        ["metadata"] = new Dictionary<string, object?>()
      };

      var resp = await _client.PostAsJsonAsync("/api/transactions", tx);

      if (resp.StatusCode == HttpStatusCode.InternalServerError)
      {
        await DumpResponse(resp, $"POST /api/transactions (reference_id={referenceId})");
        Assert.True(false, $"Servidor retornou 500 para reference_id={referenceId}. Veja o body acima (deve ter exception/message).");
      }

      var body = await resp.Content.ReadFromJsonAsync<Dictionary<string, object?>>();
      Assert.NotNull(body);
      return body!;
    }
  }

  [Fact]
  public async Task Same_reference_id_is_idempotent()
  {
    var createAcc = new Dictionary<string, object?>
    {
      ["client_id"] = "CLI-TEST",
      ["account_id"] = "ACC-TEST-2",
      ["initial_balance"] = 10_000,
      ["credit_limit"] = 0,
      ["currency"] = "BRL"
    };

    var accResp = await _client.PostAsJsonAsync("/api/accounts", createAcc);
    await DumpIfNotExpected(accResp, HttpStatusCode.Created, "POST /api/accounts");
    Assert.Equal(HttpStatusCode.Created, accResp.StatusCode);

    var tx = new Dictionary<string, object?>
    {
      ["operation"] = "debit",
      ["account_id"] = "ACC-TEST-2",
      ["amount"] = 1_000,
      ["currency"] = "BRL",
      ["reference_id"] = "TXN-IDEMP-1",
      ["metadata"] = new Dictionary<string, object?> { ["k"] = "v" }
    };

    var (s1, b1) = await PostTx(tx);
    var (s2, b2) = await PostTx(tx);

    Assert.Equal(s1, s2);
    Assert.Equal(b1["transaction_id"]!.ToString(), b2["transaction_id"]!.ToString());
    Assert.Equal(b1["status"]!.ToString(), b2["status"]!.ToString());
    Assert.Equal(b1["balance"]!.ToString(), b2["balance"]!.ToString());
  }

  [Fact]
  public async Task Many_concurrent_credits_are_atomic_no_lost_updates()
  {
    var acc = NewId("ACC");
    await CreateAccount(acc, initialBalance: 0, creditLimit: 0);

    const int n = 25;
    const long amount = 1_000;

    var gate = NewStartGate();
    var tasks = Enumerable.Range(0, n)
      .Select(i => RunAfter(gate, () => PostTxExpect2xx(new Dictionary<string, object?>
      {
        ["operation"] = "credit",
        ["account_id"] = acc,
        ["amount"] = amount,
        ["currency"] = "BRL",
        ["reference_id"] = NewId("TXN"),
        ["metadata"] = new Dictionary<string, object?> { ["i"] = i }
      })))
      .ToArray();

    gate.SetResult(true);
    await Task.WhenAll(tasks);

    foreach (var t in tasks)
      Assert.Equal("success", GetString(t.Result, "status"));

    var a = await GetAccount(acc);
    Assert.Equal(n * amount, GetLong(a, "balance"));
    Assert.Equal(0, GetLong(a, "reserved_balance"));
    Assert.Equal(n * amount, GetLong(a, "available_balance"));
  }

  [Fact]
  public async Task Concurrent_transfers_do_not_overdraw_and_do_not_create_money()
  {
    var from = NewId("ACC");
    var to = NewId("ACC");

    await CreateAccount(from, initialBalance: 10_000, creditLimit: 0);
    await CreateAccount(to, initialBalance: 0, creditLimit: 0);

    var gate = NewStartGate();

    var t1 = RunAfter(gate, () => PostTxExpect2xx(new Dictionary<string, object?>
    {
      ["operation"] = "transfer",
      ["account_id"] = from,
      ["target_account_id"] = to,
      ["amount"] = 7_000,
      ["currency"] = "BRL",
      ["reference_id"] = NewId("TXN"),
      ["metadata"] = new Dictionary<string, object?>()
    }));

    var t2 = RunAfter(gate, () => PostTxExpect2xx(new Dictionary<string, object?>
    {
      ["operation"] = "transfer",
      ["account_id"] = from,
      ["target_account_id"] = to,
      ["amount"] = 7_000,
      ["currency"] = "BRL",
      ["reference_id"] = NewId("TXN"),
      ["metadata"] = new Dictionary<string, object?>()
    }));

    gate.SetResult(true);
    await Task.WhenAll(t1, t2);

    var s1 = GetString(t1.Result, "status");
    var s2 = GetString(t2.Result, "status");

    Assert.Contains(s1, new[] { "success", "failed" });
    Assert.Contains(s2, new[] { "success", "failed" });
    Assert.NotEqual(s1, s2);

    var aFrom = await GetAccount(from);
    var aTo = await GetAccount(to);

    var balFrom = GetLong(aFrom, "balance");
    var balTo = GetLong(aTo, "balance");

    Assert.Equal(10_000, balFrom + balTo);
    Assert.Equal(3_000, balFrom);
    Assert.Equal(7_000, balTo);
  }

  [Fact]
  public async Task Concurrent_reserve_and_debit_does_not_double_spend()
  {
    var acc = NewId("ACC");
    await CreateAccount(acc, initialBalance: 10_000, creditLimit: 0);

    var gate = NewStartGate();

    var reserve = RunAfter(gate, () => PostTxExpect2xx(new Dictionary<string, object?>
    {
      ["operation"] = "reserve",
      ["account_id"] = acc,
      ["amount"] = 7_000,
      ["currency"] = "BRL",
      ["reference_id"] = NewId("TXN"),
      ["metadata"] = new Dictionary<string, object?>()
    }));

    var debit = RunAfter(gate, () => PostTxExpect2xx(new Dictionary<string, object?>
    {
      ["operation"] = "debit",
      ["account_id"] = acc,
      ["amount"] = 7_000,
      ["currency"] = "BRL",
      ["reference_id"] = NewId("TXN"),
      ["metadata"] = new Dictionary<string, object?>()
    }));

    gate.SetResult(true);
    await Task.WhenAll(reserve, debit);

    var sr = GetString(reserve.Result, "status");
    var sd = GetString(debit.Result, "status");

    Assert.Contains(sr, new[] { "success", "failed" });
    Assert.Contains(sd, new[] { "success", "failed" });
    Assert.NotEqual(sr, sd);

    var a = await GetAccount(acc);
    var balance = GetLong(a, "balance");
    var reserved = GetLong(a, "reserved_balance");
    var available = GetLong(a, "available_balance");

    Assert.Equal(balance, available + reserved);

    var ok1 = balance == 3_000 && reserved == 0 && available == 3_000;
    var ok2 = balance == 10_000 && reserved == 7_000 && available == 3_000;

    Assert.True(ok1 || ok2, $"Estado inesperado: balance={balance}, reserved={reserved}, available={available}");
  }

  [Fact]
  public async Task Concurrent_captures_do_not_capture_more_than_reserved()
  {
    var acc = NewId("ACC");
    await CreateAccount(acc, initialBalance: 0, creditLimit: 0);

    await PostTxExpect2xx(new Dictionary<string, object?>
    {
      ["operation"] = "credit",
      ["account_id"] = acc,
      ["amount"] = 10_000,
      ["currency"] = "BRL",
      ["reference_id"] = NewId("TXN"),
      ["metadata"] = new Dictionary<string, object?>()
    });

    var reserveRef = NewId("TXN");
    await PostTxExpect2xx(new Dictionary<string, object?>
    {
      ["operation"] = "reserve",
      ["account_id"] = acc,
      ["amount"] = 6_000,
      ["currency"] = "BRL",
      ["reference_id"] = reserveRef,
      ["metadata"] = new Dictionary<string, object?>()
    });

    var gate = NewStartGate();

    var cap1 = RunAfter(gate, () => PostTxExpect2xx(new Dictionary<string, object?>
    {
      ["operation"] = "capture",
      ["account_id"] = acc,
      ["amount"] = 4_000,
      ["currency"] = "BRL",
      ["reference_id"] = NewId("TXN"),
      ["related_reference_id"] = reserveRef,
      ["metadata"] = new Dictionary<string, object?>()
    }));

    var cap2 = RunAfter(gate, () => PostTxExpect2xx(new Dictionary<string, object?>
    {
      ["operation"] = "capture",
      ["account_id"] = acc,
      ["amount"] = 4_000,
      ["currency"] = "BRL",
      ["reference_id"] = NewId("TXN"),
      ["related_reference_id"] = reserveRef,
      ["metadata"] = new Dictionary<string, object?>()
    }));

    gate.SetResult(true);
    await Task.WhenAll(cap1, cap2);

    var s1 = GetString(cap1.Result, "status");
    var s2 = GetString(cap2.Result, "status");

    Assert.Contains(s1, new[] { "success", "failed" });
    Assert.Contains(s2, new[] { "success", "failed" });
    Assert.NotEqual(s1, s2);

    var a = await GetAccount(acc);
    Assert.Equal(6_000, GetLong(a, "balance"));
    Assert.Equal(2_000, GetLong(a, "reserved_balance"));
    Assert.Equal(4_000, GetLong(a, "available_balance"));
  }

  [Fact]
  public async Task Transfer_to_missing_target_is_atomic_source_not_debited()
  {
    var from = NewId("ACC");
    await CreateAccount(from, initialBalance: 10_000, creditLimit: 0);

    var missing = NewId("ACC");

    var resp = await _client.PostAsJsonAsync("/api/transactions", new Dictionary<string, object?>
    {
      ["operation"] = "transfer",
      ["account_id"] = from,
      ["target_account_id"] = missing,
      ["amount"] = 5_000,
      ["currency"] = "BRL",
      ["reference_id"] = NewId("TXN"),
      ["metadata"] = new Dictionary<string, object?>()
    });

    if (resp.StatusCode == HttpStatusCode.InternalServerError)
    {
      await DumpResponse(resp, "POST /api/transactions (transfer missing target) - 500");
      Assert.True(false, "Servidor retornou 500.");
    }

    if ((int)resp.StatusCode >= 200 && (int)resp.StatusCode <= 299)
    {
      var body = await resp.Content.ReadFromJsonAsync<Dictionary<string, object?>>();
      Assert.NotNull(body);
      Assert.Contains(GetString(body!, "status"), new[] { "failed", "pending" });
    }
    else
    {
      Assert.Contains(resp.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.NotFound, HttpStatusCode.UnprocessableEntity });
    }

    var aFrom = await GetAccount(from);
    Assert.Equal(10_000, GetLong(aFrom, "balance"));
    Assert.Equal(0, GetLong(aFrom, "reserved_balance"));
    Assert.Equal(10_000, GetLong(aFrom, "available_balance"));
  }

  [Fact]
  public async Task Transfer_same_reference_id_is_idempotent_and_does_not_duplicate_history()
  {
    var from = NewId("ACC");
    var to = NewId("ACC");

    await CreateAccount(from, initialBalance: 10_000, creditLimit: 0);
    await CreateAccount(to, initialBalance: 0, creditLimit: 0);

    var before = await GetAccountTransactions(from);

    var referenceId = NewId("TXN");
    var tx = new Dictionary<string, object?>
    {
      ["operation"] = "transfer",
      ["account_id"] = from,
      ["target_account_id"] = to,
      ["amount"] = 1_000,
      ["currency"] = "BRL",
      ["reference_id"] = referenceId,
      ["metadata"] = new Dictionary<string, object?> { ["k"] = "v" }
    };

    var b1 = await PostTxExpect2xx(Clone(tx));
    var mid = await GetAccountTransactions(from);

    var b2 = await PostTxExpect2xx(Clone(tx));
    var after = await GetAccountTransactions(from);

    Assert.Equal(GetString(b1, "transaction_id"), GetString(b2, "transaction_id"));
    Assert.Equal(GetString(b1, "status"), GetString(b2, "status"));
    Assert.Equal(GetLong(b1, "balance"), GetLong(b2, "balance"));

    Assert.True(mid.Count >= before.Count + 1, $"Esperava pelo menos +1 no histórico. before={before.Count}, mid={mid.Count}");
    Assert.Equal(mid.Count, after.Count);

    var aFrom = await GetAccount(from);
    var aTo = await GetAccount(to);

    Assert.Equal(9_000, GetLong(aFrom, "balance"));
    Assert.Equal(1_000, GetLong(aTo, "balance"));
  }

  [Fact]
  public async Task Concurrent_reversals_on_same_related_reference_apply_only_once()
  {
    var acc = NewId("ACC");
    await CreateAccount(acc, initialBalance: 10_000, creditLimit: 0);

    var debitRef = NewId("TXN");
    var d = await PostTxExpect2xx(new Dictionary<string, object?>
    {
      ["operation"] = "debit",
      ["account_id"] = acc,
      ["amount"] = 1_000,
      ["currency"] = "BRL",
      ["reference_id"] = debitRef,
      ["metadata"] = new Dictionary<string, object?>()
    });
    Assert.Equal("success", GetString(d, "status"));

    var gate = NewStartGate();

    var r1 = RunAfter(gate, () => PostTxExpect2xx(new Dictionary<string, object?>
    {
      ["operation"] = "reversal",
      ["account_id"] = acc,
      ["amount"] = 1_000,
      ["currency"] = "BRL",
      ["reference_id"] = NewId("TXN"),
      ["related_reference_id"] = debitRef,
      ["metadata"] = new Dictionary<string, object?>()
    }));

    var r2 = RunAfter(gate, () => PostTxExpect2xx(new Dictionary<string, object?>
    {
      ["operation"] = "reversal",
      ["account_id"] = acc,
      ["amount"] = 1_000,
      ["currency"] = "BRL",
      ["reference_id"] = NewId("TXN"),
      ["related_reference_id"] = debitRef,
      ["metadata"] = new Dictionary<string, object?>()
    }));

    gate.SetResult(true);
    await Task.WhenAll(r1, r2);

    var s1 = GetString(r1.Result, "status");
    var s2 = GetString(r2.Result, "status");

    Assert.Contains(s1, new[] { "success", "failed" });
    Assert.Contains(s2, new[] { "success", "failed" });

    var a = await GetAccount(acc);
    Assert.Equal(10_000, GetLong(a, "balance"));
    Assert.Equal(0, GetLong(a, "reserved_balance"));
    Assert.Equal(10_000, GetLong(a, "available_balance"));
  }

  [Fact]
  public async Task Transfer_read_your_writes_after_response()
  {
    var from = NewId("ACC");
    var to = NewId("ACC");

    await CreateAccount(from, initialBalance: 10_000, creditLimit: 0);
    await CreateAccount(to, initialBalance: 0, creditLimit: 0);

    var resp = await PostTxExpect2xx(new Dictionary<string, object?>
    {
      ["operation"] = "transfer",
      ["account_id"] = from,
      ["target_account_id"] = to,
      ["amount"] = 7_000,
      ["currency"] = "BRL",
      ["reference_id"] = NewId("TXN"),
      ["metadata"] = new Dictionary<string, object?>()
    });

    Assert.Equal("success", GetString(resp, "status"));

    var aFrom = await GetAccount(from);
    var aTo = await GetAccount(to);

    Assert.Equal(3_000, GetLong(aFrom, "balance"));
    Assert.Equal(0, GetLong(aFrom, "reserved_balance"));
    Assert.Equal(3_000, GetLong(aFrom, "available_balance"));

    Assert.Equal(7_000, GetLong(aTo, "balance"));
    Assert.Equal(0, GetLong(aTo, "reserved_balance"));
    Assert.Equal(7_000, GetLong(aTo, "available_balance"));
  }

  [Fact]
  public async Task Concurrent_same_reference_id_only_applies_once_and_history_does_not_grow()
  {
    var acc = NewId("ACC");
    await CreateAccount(acc, initialBalance: 10_000, creditLimit: 0);

    var before = await GetAccountTransactions(acc);

    var refId = NewId("TXN");
    var template = new Dictionary<string, object?>
    {
      ["operation"] = "debit",
      ["account_id"] = acc,
      ["amount"] = 1_000,
      ["currency"] = "BRL",
      ["reference_id"] = refId,
      ["metadata"] = new Dictionary<string, object?> { ["k"] = "v" }
    };

    var gate = NewStartGate();
    var tasks = Enumerable.Range(0, 10)
      .Select(_ => RunAfter(gate, () => PostTxExpect2xx(Clone(template))))
      .ToArray();

    gate.SetResult(true);
    await Task.WhenAll(tasks);

    var txIds = tasks.Select(t => GetString(t.Result, "transaction_id")).Distinct(StringComparer.Ordinal).ToList();
    Assert.Single(txIds);

    var a = await GetAccount(acc);
    Assert.Equal(9_000, GetLong(a, "balance"));

    var after = await GetAccountTransactions(acc);
    Assert.Equal(before.Count + 1, after.Count);
  }

  [Fact]
  public async Task Concurrent_same_reference_id_reserve_only_once()
  {
    var acc = NewId("ACC");
    await CreateAccount(acc, initialBalance: 10_000, creditLimit: 0);

    var before = await GetAccountTransactions(acc);

    var refId = NewId("TXN");
    var template = new Dictionary<string, object?>
    {
      ["operation"] = "reserve",
      ["account_id"] = acc,
      ["amount"] = 2_000,
      ["currency"] = "BRL",
      ["reference_id"] = refId,
      ["metadata"] = new Dictionary<string, object?>()
    };

    var gate = NewStartGate();
    var tasks = Enumerable.Range(0, 10)
      .Select(_ => RunAfter(gate, () => PostTxExpect2xx(Clone(template))))
      .ToArray();

    gate.SetResult(true);
    await Task.WhenAll(tasks);

    var txIds = tasks.Select(t => GetString(t.Result, "transaction_id")).Distinct(StringComparer.Ordinal).ToList();
    Assert.Single(txIds);

    var a = await GetAccount(acc);
    Assert.Equal(10_000, GetLong(a, "balance"));
    Assert.Equal(2_000, GetLong(a, "reserved_balance"));
    Assert.Equal(8_000, GetLong(a, "available_balance"));

    var after = await GetAccountTransactions(acc);
    Assert.Equal(before.Count + 1, after.Count);
  }

  [Fact]
  public async Task Opposing_transfers_do_not_deadlock_or_500_and_preserve_total()
  {
    var a1 = NewId("ACC");
    var a2 = NewId("ACC");

    await CreateAccount(a1, initialBalance: 10_000, creditLimit: 0);
    await CreateAccount(a2, initialBalance: 10_000, creditLimit: 0);

    var gate = NewStartGate();

    var t1 = RunAfter(gate, () => PostTxExpect2xx(new Dictionary<string, object?>
    {
      ["operation"] = "transfer",
      ["account_id"] = a1,
      ["target_account_id"] = a2,
      ["amount"] = 2_000,
      ["currency"] = "BRL",
      ["reference_id"] = NewId("TXN"),
      ["metadata"] = new Dictionary<string, object?>()
    }));

    var t2 = RunAfter(gate, () => PostTxExpect2xx(new Dictionary<string, object?>
    {
      ["operation"] = "transfer",
      ["account_id"] = a2,
      ["target_account_id"] = a1,
      ["amount"] = 2_000,
      ["currency"] = "BRL",
      ["reference_id"] = NewId("TXN"),
      ["metadata"] = new Dictionary<string, object?>()
    }));

    gate.SetResult(true);
    await Task.WhenAll(t1, t2);

    Assert.Contains(GetString(t1.Result, "status"), new[] { "success", "failed" });
    Assert.Contains(GetString(t2.Result, "status"), new[] { "success", "failed" });

    var b1 = await GetAccount(a1);
    var b2 = await GetAccount(a2);

    var bal1 = GetLong(b1, "balance");
    var bal2 = GetLong(b2, "balance");

    Assert.Equal(20_000, bal1 + bal2);
    Assert.InRange(bal1, 8_000, 12_000);
    Assert.InRange(bal2, 8_000, 12_000);
    Assert.Equal(0, GetLong(b1, "reserved_balance"));
    Assert.Equal(0, GetLong(b2, "reserved_balance"));
  }

  private async Task CreateAccount(string accountId, long initialBalance, long creditLimit, string currency = "BRL")
  {
    var createAcc = new Dictionary<string, object?>
    {
      ["client_id"] = "CLI-TEST",
      ["account_id"] = accountId,
      ["initial_balance"] = initialBalance,
      ["credit_limit"] = creditLimit,
      ["currency"] = currency
    };

    var resp = await _client.PostAsJsonAsync("/api/accounts", createAcc);
    await DumpIfNotExpected(resp, HttpStatusCode.Created, "POST /api/accounts");
    Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
  }

  private async Task<Dictionary<string, object?>> GetAccount(string accountId)
  {
    var payload = await _client.GetFromJsonAsync<Dictionary<string, object?>>($"/api/accounts/{accountId}");
    Assert.NotNull(payload);
    return payload!;
  }

  private async Task<List<Dictionary<string, object?>>> GetAccountTransactions(string accountId)
  {
    var payload = await _client.GetFromJsonAsync<List<Dictionary<string, object?>>>($"/api/accounts/{accountId}/transactions");
    return payload ?? new List<Dictionary<string, object?>>();
  }

  private async Task<(HttpStatusCode StatusCode, Dictionary<string, object?> Body)> PostTx(Dictionary<string, object?> tx)
  {
    var resp = await _client.PostAsJsonAsync("/api/transactions", tx);
    var bodyText = await resp.Content.ReadAsStringAsync();

    if (resp.StatusCode == HttpStatusCode.InternalServerError)
    {
      DumpResponseKnownBody(resp, $"POST /api/transactions (op={tx["operation"]}, ref={tx["reference_id"]}) - 500", bodyText);
      Assert.True(false, "Servidor retornou 500.");
    }

    var is2xx = (int)resp.StatusCode >= 200 && (int)resp.StatusCode <= 299;
    var isBizError =
      resp.StatusCode == HttpStatusCode.BadRequest ||
      resp.StatusCode == HttpStatusCode.NotFound ||
      resp.StatusCode == HttpStatusCode.UnprocessableEntity;

    if (!is2xx && !isBizError)
    {
      DumpResponseKnownBody(resp, $"POST /api/transactions (op={tx["operation"]}, ref={tx["reference_id"]}) - unexpected", bodyText);
      Assert.True(false, $"Status inesperado: {(int)resp.StatusCode} {resp.ReasonPhrase}");
    }

    Dictionary<string, object?>? body = null;
    try
    {
      body = JsonSerializer.Deserialize<Dictionary<string, object?>>(bodyText, new JsonSerializerOptions
      {
        PropertyNameCaseInsensitive = true
      });
    }
    catch
    {
      DumpResponseKnownBody(resp, $"POST /api/transactions (op={tx["operation"]}, ref={tx["reference_id"]}) - invalid json", bodyText);
      Assert.True(false, "Resposta não é JSON válido.");
    }

    Assert.NotNull(body);
    return (resp.StatusCode, body!);
  }

  private async Task<Dictionary<string, object?>> PostTxExpect2xx(Dictionary<string, object?> tx)
  {
    var (status, body) = await PostTx(tx);

    if ((int)status < 200 || (int)status > 299)
    {
      var st = GetString(body, "status");
      if (!string.Equals(st, "failed", StringComparison.OrdinalIgnoreCase) &&
          !string.Equals(st, "pending", StringComparison.OrdinalIgnoreCase))
      {
        _output.WriteLine($"[POST /api/transactions (op={tx["operation"]}, ref={tx["reference_id"]}) - non-2xx] Status: {(int)status} {status}");
        _output.WriteLine("Body:");
        _output.WriteLine(JsonSerializer.Serialize(body));
      }
    }

    return body;
  }

  private static TaskCompletionSource<bool> NewStartGate()
    => new(TaskCreationOptions.RunContinuationsAsynchronously);

  private static Task<T> RunAfter<T>(TaskCompletionSource<bool> gate, Func<Task<T>> work)
    => Task.Run(async () => { await gate.Task; return await work(); });

  private static Dictionary<string, object?> Clone(Dictionary<string, object?> src)
  {
    var d = new Dictionary<string, object?>(src.Count, StringComparer.Ordinal);
    foreach (var kv in src)
    {
      if (kv.Value is Dictionary<string, object?> md)
        d[kv.Key] = new Dictionary<string, object?>(md, StringComparer.Ordinal);
      else
        d[kv.Key] = kv.Value;
    }
    return d;
  }

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

  private static long GetLong(Dictionary<string, object?> d, string key)
  {
    if (!d.TryGetValue(key, out var v) || v is null)
      throw new InvalidOperationException($"Key '{key}' is null or missing.");

    if (v is long l) return l;
    if (v is int i) return i;

    if (v is JsonElement je)
    {
      if (je.ValueKind == JsonValueKind.Number && je.TryGetInt64(out var n)) return n;
      if (je.ValueKind == JsonValueKind.String && long.TryParse(je.GetString(), out var s)) return s;
      if (long.TryParse(je.ToString(), out var t)) return t;
    }

    if (v is string str && long.TryParse(str, out var p)) return p;

    throw new InvalidOperationException($"Key '{key}' is not a number. Type={v.GetType().Name}, Value={v}");
  }

  private async Task DumpIfNotExpected(HttpResponseMessage resp, HttpStatusCode expected, string context)
  {
    if (resp.StatusCode != expected)
      await DumpResponse(resp, $"{context} - expected {(int)expected}");
  }

  private async Task DumpResponse(HttpResponseMessage resp, string context)
  {
    string body;
    try { body = await resp.Content.ReadAsStringAsync(); }
    catch (Exception ex) { body = $"<failed to read body: {ex.GetType().Name}: {ex.Message}>"; }

    _output.WriteLine($"[{context}] Status: {(int)resp.StatusCode} {resp.ReasonPhrase}");
    _output.WriteLine("Headers:");
    foreach (var h in resp.Headers)
      _output.WriteLine($"  {h.Key}: {string.Join(", ", h.Value)}");
    _output.WriteLine("Body:");
    _output.WriteLine(string.IsNullOrWhiteSpace(body) ? "<empty>" : body);
  }

  private void DumpResponseKnownBody(HttpResponseMessage resp, string context, string body)
  {
    _output.WriteLine($"[{context}] Status: {(int)resp.StatusCode} {resp.ReasonPhrase}");
    _output.WriteLine("Headers:");
    foreach (var h in resp.Headers)
      _output.WriteLine($"  {h.Key}: {string.Join(", ", h.Value)}");
    _output.WriteLine("Body:");
    _output.WriteLine(string.IsNullOrWhiteSpace(body) ? "<empty>" : body);
  }

  private sealed class CustomFactory
    : WebApplicationFactory<global::PagueVeloz.TransactionProcessor.Api.Json.SnakeCaseLowerNamingPolicy>
  {
    private readonly string _connectionString;
    private readonly ITestOutputHelper _output;

    public CustomFactory(string connectionString, ITestOutputHelper output)
    {
      _connectionString = connectionString;
      _output = output;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
      builder.UseEnvironment("Test");

      builder.ConfigureAppConfiguration((ctx, cfg) =>
      {
        var dict = new Dictionary<string, string?>
        {
          ["ConnectionStrings:SqlServer"] = _connectionString,
          ["RabbitMq:Host"] = "localhost"
        };
        cfg.AddInMemoryCollection(dict);
      });

      builder.ConfigureServices(services =>
      {
        services.AddSingleton<IStartupFilter>(new TestExceptionCaptureStartupFilter(_output));
      });

      builder.ConfigureLogging(logging =>
      {
        logging.ClearProviders();
        logging.SetMinimumLevel(LogLevel.Debug);
        logging.AddProvider(new XunitLoggerProvider(_output));
      });

      builder.UseSetting(WebHostDefaults.DetailedErrorsKey, "true");
      builder.UseSetting("ASPNETCORE_DETAILEDERRORS", "true");
    }
  }

  private sealed class TestExceptionCaptureStartupFilter : IStartupFilter
  {
    private readonly ITestOutputHelper _output;
    public TestExceptionCaptureStartupFilter(ITestOutputHelper output) => _output = output;

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
      return app =>
      {
        app.Use(async (ctx, nxt) =>
        {
          try
          {
            await nxt();
          }
          catch (Exception ex)
          {
            _output.WriteLine("=== UNHANDLED EXCEPTION CAPTURED BY TEST MIDDLEWARE ===");
            _output.WriteLine(ex.ToString());

            if (!ctx.Response.HasStarted)
            {
              ctx.Response.Clear();
              ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
              ctx.Response.ContentType = "application/json";

              var payload = JsonSerializer.Serialize(new
              {
                error = "test_unhandled_exception",
                exception = ex.GetType().FullName,
                message = ex.Message
              });

              await ctx.Response.WriteAsync(payload);
            }
          }
        });

        next(app);
      };
    }
  }

  private sealed class XunitLoggerProvider : ILoggerProvider
  {
    private readonly ITestOutputHelper _output;
    public XunitLoggerProvider(ITestOutputHelper output) => _output = output;
    public ILogger CreateLogger(string categoryName) => new XunitLogger(_output, categoryName);
    public void Dispose() { }
  }

  private sealed class XunitLogger : ILogger
  {
    private readonly ITestOutputHelper _output;
    private readonly string _category;

    public XunitLogger(ITestOutputHelper output, string category)
    {
      _output = output;
      _category = category;
    }

    public IDisposable BeginScope<TState>(TState state) => NullScope.Instance;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
      LogLevel logLevel,
      EventId eventId,
      TState state,
      Exception? exception,
      Func<TState, Exception?, string> formatter)
    {
      _output.WriteLine($"{logLevel} {_category}: {formatter(state, exception)}");
      if (exception is not null)
        _output.WriteLine(exception.ToString());
    }

    private sealed class NullScope : IDisposable
    {
      public static readonly NullScope Instance = new();
      public void Dispose() { }
    }
  }
}
