using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PagueVeloz.TransactionProcessor.Application.Abstractions;
using PagueVeloz.TransactionProcessor.Infrastructure.Persistence;

namespace PagueVeloz.TransactionProcessor.Infrastructure;

public sealed class SqlServerUnitOfWork : IUnitOfWork
{
  private readonly TransactionDbContext _db;

  public SqlServerUnitOfWork(TransactionDbContext db) => _db = db;

  public async Task<ITransactionContext> BeginTransactionAsync(IsolationLevel isolationLevel, CancellationToken ct)
  {
    var tx = await _db.Database.BeginTransactionAsync(isolationLevel, ct);
    return new EfTransactionContext(tx);
  }

  public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);

  public void ClearChangeTracker() => _db.ChangeTracker.Clear();

  private sealed class EfTransactionContext : ITransactionContext
  {
    private readonly IDbContextTransaction _tx;
    private int _completed;

    public EfTransactionContext(IDbContextTransaction tx) => _tx = tx;

    public Task CommitAsync(CancellationToken ct)
    {
      if (Interlocked.Exchange(ref _completed, 1) == 1) return Task.CompletedTask;
      return _tx.CommitAsync(ct);
    }

    public Task RollbackAsync(CancellationToken ct)
    {
      if (Interlocked.Exchange(ref _completed, 1) == 1) return Task.CompletedTask;
      return _tx.RollbackAsync(ct);
    }

    public ValueTask DisposeAsync() => _tx.DisposeAsync();
  }
}
