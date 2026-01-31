using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PagueVeloz.TransactionProcessor.IntegrationTests.Infrastructure;
using PagueVeloz.TransactionProcessor.Infrastructure.Persistence;
using PagueVeloz.TransactionProcessor.Worker;
using Xunit;

namespace PagueVeloz.TransactionProcessor.IntegrationTests.Messaging;

[Collection(IntegrationCollection.Name)]
public sealed class OutboxAndRabbitMqTests
{
  private readonly IntegrationTestFixture _fx;

  public OutboxAndRabbitMqTests(IntegrationTestFixture fx) => _fx = fx;

  [Fact]
  public async Task Outbox_publisher_emits_transaction_processed_event_to_rabbitmq()
  {
    var acc = NewId("ACC");
    await _fx.CreateAccountAsync(acc, initialBalance: 0, creditLimit: 0);

    const string refId = "TXN-OUTBOX-001";
    await _fx.ProcessTransactionAsync(Tx("credit", acc, 10_000, refId));

    using var queue = _fx.CreateRabbitQueue(routingKey: "transaction.processed");

    await RunOutboxOnceAsync(rabbitHost: _fx.RabbitHost, rabbitPort: _fx.RabbitPort);

    var msg = await ReceiveForReferenceAsync(queue, refId, TimeSpan.FromSeconds(10));
    Assert.Equal("transaction.processed", msg.RoutingKey);

    using var doc = JsonDocument.Parse(msg.Body);
    Assert.Equal(refId, doc.RootElement.GetProperty("referenceId").GetString());
    Assert.Equal(acc, doc.RootElement.GetProperty("accountId").GetString());
    Assert.Equal("credit", doc.RootElement.GetProperty("operation").GetString());
    Assert.Equal("success", doc.RootElement.GetProperty("status").GetString());
  }

  [Fact]
  public async Task Idempotent_reference_id_does_not_enqueue_duplicate_outbox_event()
  {
    var acc = NewId("ACC");
    await _fx.CreateAccountAsync(acc, initialBalance: 0, creditLimit: 0);

    await using var sp = _fx.BuildWorkerServiceProvider(_fx.RabbitHost, _fx.RabbitPort);
    await using var scope = sp.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<TransactionDbContext>();

    var before = await db.OutboxEvents.CountAsync();

    const string refId = "TXN-IDEMP-OUTBOX-001";
    var first = await _fx.ProcessTransactionAsync(Tx("credit", acc, 1_000, refId));
    Assert.Equal("success", first["status"]?.ToString());

    var afterFirst = await db.OutboxEvents.CountAsync();
    Assert.Equal(before + 1, afterFirst);

    var second = await _fx.ProcessTransactionAsync(Tx("credit", acc, 1_000, refId));
    Assert.Equal(first["transaction_id"]?.ToString(), second["transaction_id"]?.ToString());

    var afterSecond = await db.OutboxEvents.CountAsync();
    Assert.Equal(afterFirst, afterSecond);
  }

  [Fact]
  public async Task Outbox_retry_marks_failed_then_succeeds_after_next_attempt_is_due()
  {
    var acc = NewId("ACC");
    await _fx.CreateAccountAsync(acc, initialBalance: 0, creditLimit: 0);
    const string refId = "TXN-OUTBOX-RETRY-001";
    await _fx.ProcessTransactionAsync(Tx("credit", acc, 10_000, refId));

    await RunOutboxOnceAsync(rabbitHost: "127.0.0.1", rabbitPort: 1);

    Guid eventId;

    await using (var sp = _fx.BuildWorkerServiceProvider(_fx.RabbitHost, _fx.RabbitPort))
    await using (var scope = sp.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<TransactionDbContext>();

      var row = await db.OutboxEvents
        .Where(x => x.AggregateId == acc && x.ProcessedAt == null)
        .OrderByDescending(x => x.OccurredAt)
        .FirstAsync();

      Assert.Equal(1, row.Attempts);
      Assert.False(string.IsNullOrWhiteSpace(row.LastError));
      Assert.True(row.NextAttemptAt > DateTimeOffset.UtcNow.AddMilliseconds(-500));

      eventId = row.EventId;

      await db.Database.ExecuteSqlRawAsync(
        "UPDATE OutboxEvents SET NextAttemptAt = SYSUTCDATETIME() WHERE EventId = {0}",
        eventId);
    }

    using var queue = _fx.CreateRabbitQueue(routingKey: "transaction.processed");
    await RunOutboxOnceAsync(rabbitHost: _fx.RabbitHost, rabbitPort: _fx.RabbitPort);

    var msg = await ReceiveForReferenceAsync(queue, refId, TimeSpan.FromSeconds(10));
    using var doc = JsonDocument.Parse(msg.Body);
    Assert.Equal(refId, doc.RootElement.GetProperty("referenceId").GetString());

    await using (var sp2 = _fx.BuildWorkerServiceProvider(_fx.RabbitHost, _fx.RabbitPort))
    await using (var scope2 = sp2.CreateAsyncScope())
    {
      var db = scope2.ServiceProvider.GetRequiredService<TransactionDbContext>();
      var e = await db.OutboxEvents.SingleAsync(x => x.EventId == eventId);
      Assert.NotNull(e.ProcessedAt);
    }
  }

  [Fact]
  public async Task Outbox_publisher_does_not_republish_already_processed_event()
  {
    var acc = NewId("ACC");
    await _fx.CreateAccountAsync(acc, initialBalance: 0, creditLimit: 0);

    var refId = "TXN-OUTBOX-NOREPUB-001-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
    await _fx.ProcessTransactionAsync(Tx("credit", acc, 10_000, refId));

    using var queue = _fx.CreateRabbitQueue(routingKey: "transaction.processed");

    await RunOutboxOnceAsync(rabbitHost: _fx.RabbitHost, rabbitPort: _fx.RabbitPort);
    _ = await ReceiveForReferenceAsync(queue, refId, TimeSpan.FromSeconds(10));

    await using (var sp = _fx.BuildWorkerServiceProvider(_fx.RabbitHost, _fx.RabbitPort))
    await using (var scope = sp.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<TransactionDbContext>();
      var latest = await db.OutboxEvents
        .Where(x => x.AggregateId == acc)
        .OrderByDescending(x => x.OccurredAt)
        .FirstAsync();

      Assert.NotNull(latest.ProcessedAt);
    }

    await RunOutboxOnceAsync(rabbitHost: _fx.RabbitHost, rabbitPort: _fx.RabbitPort);

    await Assert.ThrowsAsync<TimeoutException>(async () =>
    {
      _ = await ReceiveForReferenceAsync(queue, refId, TimeSpan.FromSeconds(1));
    });
  }

  [Fact]
  public async Task Outbox_publisher_emits_events_for_multiple_transactions_and_consumer_can_parse_all()
  {
    var acc = NewId("ACC");
    await _fx.CreateAccountAsync(acc, initialBalance: 0, creditLimit: 0);

    var r1 = "TXN-OUTBOX-MANY-1-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
    var r2 = "TXN-OUTBOX-MANY-2-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
    var r3 = "TXN-OUTBOX-MANY-3-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    await _fx.ProcessTransactionAsync(Tx("credit", acc, 100, r1));
    await _fx.ProcessTransactionAsync(Tx("credit", acc, 200, r2));
    await _fx.ProcessTransactionAsync(Tx("credit", acc, 300, r3));

    using var queue = _fx.CreateRabbitQueue(routingKey: "transaction.processed");

    await RunOutboxOnceAsync(rabbitHost: _fx.RabbitHost, rabbitPort: _fx.RabbitPort);

    var msgs = await ReceiveAllForReferenceIdsAsync(
      queue,
      new HashSet<string>(StringComparer.Ordinal) { r1, r2, r3 },
      TimeSpan.FromSeconds(10));

    Assert.Equal(3, msgs.Count);

    foreach (var kv in msgs)
    {
      Assert.Equal("transaction.processed", kv.Value.RoutingKey);

      using var doc = JsonDocument.Parse(kv.Value.Body);
      Assert.Equal(kv.Key, doc.RootElement.GetProperty("referenceId").GetString());
      Assert.Equal(acc, doc.RootElement.GetProperty("accountId").GetString());
      Assert.Equal("credit", doc.RootElement.GetProperty("operation").GetString());
      Assert.Equal("success", doc.RootElement.GetProperty("status").GetString());
    }
  }

  [Fact]
  public async Task Consumer_receive_autoacks_message_and_queue_becomes_empty()
  {
    var acc = NewId("ACC");
    await _fx.CreateAccountAsync(acc, initialBalance: 0, creditLimit: 0);

    var refId = "TXN-OUTBOX-CONSUME-001-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
    await _fx.ProcessTransactionAsync(Tx("credit", acc, 10_000, refId));

    using var queue = _fx.CreateRabbitQueue(routingKey: "transaction.processed");

    await RunOutboxOnceAsync(rabbitHost: _fx.RabbitHost, rabbitPort: _fx.RabbitPort);

    _ = await ReceiveForReferenceAsync(queue, refId, TimeSpan.FromSeconds(10));

    await AssertQueueEmptyAsync(queue, TimeSpan.FromSeconds(1));
  }

  [Fact]
  public async Task Outbox_publisher_skips_event_when_next_attempt_at_is_in_the_future()
  {
    var acc = NewId("ACC");
    await _fx.CreateAccountAsync(acc, initialBalance: 0, creditLimit: 0);

    var refId = "TXN-OUTBOX-NOTDUE-001-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
    await _fx.ProcessTransactionAsync(Tx("credit", acc, 10_000, refId));

    Guid eventId;

    await using (var sp = _fx.BuildWorkerServiceProvider(_fx.RabbitHost, _fx.RabbitPort))
    await using (var scope = sp.CreateAsyncScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<TransactionDbContext>();

      var row = await db.OutboxEvents
        .Where(x => x.AggregateId == acc && x.ProcessedAt == null)
        .OrderByDescending(x => x.OccurredAt)
        .FirstAsync();

      eventId = row.EventId;

      await db.Database.ExecuteSqlRawAsync(
        "UPDATE OutboxEvents SET NextAttemptAt = DATEADD(minute, 10, SYSUTCDATETIME()) WHERE EventId = {0}",
        eventId);
    }

    using var queue = _fx.CreateRabbitQueue(routingKey: "transaction.processed");

    await RunOutboxOnceAsync(rabbitHost: _fx.RabbitHost, rabbitPort: _fx.RabbitPort);

    await Assert.ThrowsAsync<TimeoutException>(async () =>
    {
      _ = await ReceiveForReferenceAsync(queue, refId, TimeSpan.FromSeconds(1));
    });

    await using (var sp2 = _fx.BuildWorkerServiceProvider(_fx.RabbitHost, _fx.RabbitPort))
    await using (var scope2 = sp2.CreateAsyncScope())
    {
      var db = scope2.ServiceProvider.GetRequiredService<TransactionDbContext>();
      var e = await db.OutboxEvents.SingleAsync(x => x.EventId == eventId);

      Assert.Null(e.ProcessedAt);
      Assert.True(e.NextAttemptAt > DateTimeOffset.UtcNow);
    }
  }

  private async Task RunOutboxOnceAsync(string rabbitHost, int rabbitPort)
  {
    await using var sp = _fx.BuildWorkerServiceProvider(rabbitHost, rabbitPort);
    await using var scope = sp.CreateAsyncScope();
    var outbox = scope.ServiceProvider.GetRequiredService<OutboxPublisher>();
    await outbox.RunOnceAsync(CancellationToken.None);
  }

  private static async Task<(string RoutingKey, string Body, IDictionary<string, object?> Headers)> ReceiveForReferenceAsync(
    IntegrationTestFixture.RabbitMqTestQueue queue,
    string expectedReferenceId,
    TimeSpan timeout)
  {
    var deadline = DateTimeOffset.UtcNow.Add(timeout);

    while (true)
    {
      var remaining = deadline - DateTimeOffset.UtcNow;
      if (remaining <= TimeSpan.Zero)
        throw new TimeoutException($"Timed out waiting for referenceId='{expectedReferenceId}'.");

      var msg = await queue.ReceiveOneAsync(remaining);

      try
      {
        using var doc = JsonDocument.Parse(msg.Body);
        if (doc.RootElement.TryGetProperty("referenceId", out var refProp) &&
            string.Equals(refProp.GetString(), expectedReferenceId, StringComparison.Ordinal))
          return msg;
      }
      catch
      {
      }
    }
  }

  private static async Task<Dictionary<string, (string RoutingKey, string Body, IDictionary<string, object?> Headers)>> ReceiveAllForReferenceIdsAsync(
    IntegrationTestFixture.RabbitMqTestQueue queue,
    HashSet<string> expectedReferenceIds,
    TimeSpan timeout)
  {
    var deadline = DateTimeOffset.UtcNow.Add(timeout);
    var remaining = new HashSet<string>(expectedReferenceIds, StringComparer.Ordinal);
    var found = new Dictionary<string, (string RoutingKey, string Body, IDictionary<string, object?> Headers)>(StringComparer.Ordinal);

    while (remaining.Count > 0)
    {
      var left = deadline - DateTimeOffset.UtcNow;
      if (left <= TimeSpan.Zero)
        throw new TimeoutException($"Timed out waiting for referenceIds=[{string.Join(", ", remaining)}].");

      var msg = await queue.ReceiveOneAsync(left);

      try
      {
        using var doc = JsonDocument.Parse(msg.Body);
        if (doc.RootElement.TryGetProperty("referenceId", out var refProp))
        {
          var rid = refProp.GetString() ?? "";
          if (remaining.Remove(rid))
            found[rid] = msg;
        }
      }
      catch
      {
      }
    }

    return found;
  }

  private static async Task AssertQueueEmptyAsync(IntegrationTestFixture.RabbitMqTestQueue queue, TimeSpan timeout)
  {
    await Assert.ThrowsAsync<TimeoutException>(async () =>
    {
      _ = await queue.ReceiveOneAsync(timeout);
    });
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
