using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Polly;
using PagueVeloz.TransactionProcessor.Application.Abstractions;
using PagueVeloz.TransactionProcessor.Infrastructure.Messaging;
using PagueVeloz.TransactionProcessor.Infrastructure.Outbox;

namespace PagueVeloz.TransactionProcessor.Worker;

internal sealed class OutboxPublisher
{
  private readonly IUnitOfWork _uow;
  private readonly IOutboxProcessingStore _outbox;
  private readonly IEventPublisher _publisher;
  private readonly IConfiguration _config;
  private readonly Serilog.ILogger _log = Serilog.Log.ForContext<OutboxPublisher>();

  private readonly IAsyncPolicy _circuitBreaker;

  public OutboxPublisher(IUnitOfWork uow, IOutboxProcessingStore outbox, IEventPublisher publisher, IConfiguration config)
  {
    _uow = uow;
    _outbox = outbox;
    _publisher = publisher;
    _config = config;

    _circuitBreaker = Policy
      .Handle<Exception>()
      .CircuitBreakerAsync(
        exceptionsAllowedBeforeBreaking: 5,
        durationOfBreak: TimeSpan.FromSeconds(30));
  }

  public async Task RunOnceAsync(CancellationToken ct)
  {
    var batchSize = _config.GetValue<int?>("Outbox:BatchSize") ?? 50;

    await using var tx = await _uow.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);

    var batch = await _outbox.FetchBatchForProcessingAsync(batchSize, ct);
    if (batch.Count == 0)
    {
      await tx.CommitAsync(ct);
      return;
    }

    foreach (var evt in batch)
    {
      try
      {
        await _circuitBreaker.ExecuteAsync(async () =>
        {
          var headers = new Dictionary<string, object?>
          {
            ["event_id"] = evt.EventId.ToString(),
            ["aggregate_id"] = evt.AggregateId,
            ["occurred_at"] = evt.OccurredAt.UtcDateTime.ToString("O")
          };

          await _publisher.PublishAsync(evt.EventType, evt.PayloadJson, headers, ct);
        });

        await _outbox.MarkProcessedAsync(evt.EventId, DateTimeOffset.UtcNow, ct);
      }
      catch (Exception ex)
      {
        var attempts = evt.Attempts + 1;
        var next = ComputeNextAttemptUtc(attempts);
        await _outbox.MarkFailedAsync(evt.EventId, attempts, next, ex.Message, ct);

        _log.Warning(ex, "Failed to publish outbox event {EventId} attempt={Attempts} next={Next}",
          evt.EventId, attempts, next);
      }
    }

    await _uow.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
  }

  private static DateTimeOffset ComputeNextAttemptUtc(int attempts)
  {
    var baseSeconds = 1;
    var maxSeconds = 60;

    var exp = Math.Min(maxSeconds, baseSeconds * Math.Pow(2, Math.Min(attempts, 10)));
    var jitterMs = RandomNumberGenerator.GetInt32(0, 250);
    return DateTimeOffset.UtcNow.AddSeconds(exp).AddMilliseconds(jitterMs);
  }
}
