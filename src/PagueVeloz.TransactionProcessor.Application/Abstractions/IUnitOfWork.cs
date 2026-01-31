using System.Data;

namespace PagueVeloz.TransactionProcessor.Application.Abstractions;

public interface IUnitOfWork
{
  Task<ITransactionContext> BeginTransactionAsync(IsolationLevel isolationLevel, CancellationToken ct);
  Task SaveChangesAsync(CancellationToken ct);
  void ClearChangeTracker();
}

public interface ITransactionContext : IAsyncDisposable
{
  Task CommitAsync(CancellationToken ct);
  Task RollbackAsync(CancellationToken ct);
}
