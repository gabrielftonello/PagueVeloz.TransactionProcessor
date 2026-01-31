using System.Data;
using System.Text.Json;
using MediatR;
using PagueVeloz.TransactionProcessor.Application.Abstractions;
using PagueVeloz.TransactionProcessor.Application.Commands.ProcessTransaction;
using PagueVeloz.TransactionProcessor.Application.Contracts.Transactions;

namespace PagueVeloz.TransactionProcessor.Worker;

internal sealed class QueuedCommandProcessor
{
  private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

  private readonly IUnitOfWork _uow;
  private readonly ICommandQueue _queue;
  private readonly IMediator _mediator;
  private readonly Serilog.ILogger _log = Serilog.Log.ForContext<QueuedCommandProcessor>();

  public QueuedCommandProcessor(IUnitOfWork uow, ICommandQueue queue, IMediator mediator)
  {
    _uow = uow;
    _queue = queue;
    _mediator = mediator;
  }

  public async Task RunOnceAsync(CancellationToken ct)
  {
    QueuedTransactionCommand? cmd;

    await using (var tx = await _uow.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct))
    {
      cmd = await _queue.TryDequeueAsync(ct);
      if (cmd is null)
      {
        await tx.CommitAsync(ct);
        return;
      }

      await _uow.SaveChangesAsync(ct);
      await tx.CommitAsync(ct);
    }

    try
    {
      var req = JsonSerializer.Deserialize<TransactionRequest>(cmd.PayloadJson, JsonOptions)
        ?? throw new InvalidOperationException("Invalid queued payload.");

      await _mediator.Send(new ProcessTransactionCommand(req), ct);

      await _queue.MarkProcessedAsync(cmd.CommandId, "Done", null, ct);
      await _uow.SaveChangesAsync(ct);
    }
    catch (Exception ex)
    {
      await _queue.MarkProcessedAsync(cmd.CommandId, "Failed", ex.Message, ct);
      await _uow.SaveChangesAsync(ct);

      _log.Error(ex, "Queued command {CommandId} failed", cmd.CommandId);
    }
  }
}
