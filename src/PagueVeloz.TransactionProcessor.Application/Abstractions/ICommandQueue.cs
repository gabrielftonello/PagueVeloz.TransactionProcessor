using PagueVeloz.TransactionProcessor.Domain.Transactions;

namespace PagueVeloz.TransactionProcessor.Application.Abstractions;

public interface ICommandQueue
{
  Task EnqueueAsync(QueuedTransactionCommand cmd, CancellationToken ct);
  Task<QueuedTransactionCommand?> TryDequeueAsync(CancellationToken ct);
  Task MarkProcessedAsync(Guid commandId, string status, string? errorMessage, CancellationToken ct);
}

public sealed record QueuedTransactionCommand(
  Guid CommandId,
  string PayloadJson,
  DateTimeOffset EnqueuedAt
);
