using Microsoft.EntityFrameworkCore;
using PagueVeloz.TransactionProcessor.Application.Abstractions;
using PagueVeloz.TransactionProcessor.Infrastructure.Persistence;
using PagueVeloz.TransactionProcessor.Infrastructure.Persistence.Entities;

namespace PagueVeloz.TransactionProcessor.Infrastructure.Repositories;

public sealed class SqlServerCommandQueue : ICommandQueue
{
  private readonly TransactionDbContext _db;

  public SqlServerCommandQueue(TransactionDbContext db) => _db = db;

  public Task EnqueueAsync(QueuedTransactionCommand cmd, CancellationToken ct)
  {
    _db.QueuedCommands.Add(new QueuedCommandEntity
    {
      CommandId = cmd.CommandId,
      PayloadJson = cmd.PayloadJson,
      EnqueuedAt = cmd.EnqueuedAt,
      Status = "Pending"
    });
    return Task.CompletedTask;
  }

  public async Task<QueuedTransactionCommand?> TryDequeueAsync(CancellationToken ct)
  {
    var entity = await _db.QueuedCommands
      .FromSqlRaw("SELECT TOP(1) * FROM QueuedCommands WITH (UPDLOCK, READPAST, ROWLOCK) WHERE Status = 'Pending' ORDER BY EnqueuedAt")
      .FirstOrDefaultAsync(ct);

    if (entity is null)
      return null;

    entity.Status = "Processing";
    return new QueuedTransactionCommand(entity.CommandId, entity.PayloadJson, entity.EnqueuedAt);
  }

  public async Task MarkProcessedAsync(Guid commandId, string status, string? errorMessage, CancellationToken ct)
  {
    var entity = await _db.QueuedCommands.SingleAsync(x => x.CommandId == commandId, ct);
    entity.Status = status;
    entity.ErrorMessage = errorMessage;
    entity.ProcessedAt = DateTimeOffset.UtcNow;
  }
}
