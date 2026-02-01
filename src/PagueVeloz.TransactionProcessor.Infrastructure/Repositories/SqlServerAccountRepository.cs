using Microsoft.EntityFrameworkCore;
using PagueVeloz.TransactionProcessor.Application.Abstractions;
using PagueVeloz.TransactionProcessor.Domain.Accounts;
using PagueVeloz.TransactionProcessor.Infrastructure.Persistence;
using PagueVeloz.TransactionProcessor.Infrastructure.Persistence.Entities;

namespace PagueVeloz.TransactionProcessor.Infrastructure.Repositories;

public sealed class SqlServerAccountRepository : IAccountRepository
{
  private readonly TransactionDbContext _db;

  public SqlServerAccountRepository(TransactionDbContext db) => _db = db;

  public async Task<Account?> GetAsync(string accountId, CancellationToken ct)
  {
    var e = await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.AccountId == accountId, ct);
    return e is null ? null : Map(e);
  }

  public async Task<Account?> GetForUpdateAsync(string accountId, CancellationToken ct)
  {
    var e = await _db.Accounts
      .FromSqlInterpolated($"SELECT * FROM Accounts WITH (UPDLOCK, ROWLOCK) WHERE AccountId = {accountId}")
      .AsNoTracking()
      .FirstOrDefaultAsync(ct);

    return e is null ? null : Map(e);
  }

  public async Task<IReadOnlyList<Account>> GetForUpdateAsync(
    IReadOnlyList<string> accountIdsInLockOrder,
    CancellationToken ct)
  {
    var result = new List<Account>(accountIdsInLockOrder.Count);

    foreach (var id in accountIdsInLockOrder)
    {
      var acc = await GetForUpdateAsync(id, ct);
      if (acc is not null)
        result.Add(acc);
    }

    return result;
  }

  public Task AddAsync(Account account, CancellationToken ct)
  {
    _db.Accounts.Add(new AccountEntity
    {
      AccountId = account.AccountId,
      ClientId = account.ClientId,
      Currency = account.Currency,
      Balance = account.Balance,
      ReservedBalance = account.ReservedBalance,
      CreditLimit = account.CreditLimit,
      Status = (int)account.Status,
      LedgerSequence = account.LedgerSequence
    });

    return Task.CompletedTask;
  }

  public Task UpdateAsync(Account account, CancellationToken ct)
  {
    var tracked = _db.Accounts.Local.FirstOrDefault(x => x.AccountId == account.AccountId);

    if (tracked is null)
    {
      tracked = new AccountEntity { AccountId = account.AccountId };
      _db.Attach(tracked);
    }

    tracked.Balance = account.Balance;
    tracked.ReservedBalance = account.ReservedBalance;
    tracked.CreditLimit = account.CreditLimit;
    tracked.Status = (int)account.Status;
    tracked.LedgerSequence = account.LedgerSequence;

    var entry = _db.Entry(tracked);

    entry.Property(x => x.Balance).IsModified = true;
    entry.Property(x => x.ReservedBalance).IsModified = true;
    entry.Property(x => x.CreditLimit).IsModified = true;
    entry.Property(x => x.Status).IsModified = true;
    entry.Property(x => x.LedgerSequence).IsModified = true;

    return Task.CompletedTask;
  }

  private static Account Map(AccountEntity e) =>
    new(
      accountId: e.AccountId,
      clientId: e.ClientId,
      currency: e.Currency,
      balance: e.Balance,
      reservedBalance: e.ReservedBalance,
      creditLimit: e.CreditLimit,
      status: (AccountStatus)e.Status,
      ledgerSequence: e.LedgerSequence
    );
}
