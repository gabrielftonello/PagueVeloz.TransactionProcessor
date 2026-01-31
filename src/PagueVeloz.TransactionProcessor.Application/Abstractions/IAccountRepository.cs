using PagueVeloz.TransactionProcessor.Domain.Accounts;

namespace PagueVeloz.TransactionProcessor.Application.Abstractions;

public interface IAccountRepository
{
  Task<Account?> GetAsync(string accountId, CancellationToken ct);
  Task<Account?> GetForUpdateAsync(string accountId, CancellationToken ct);
  Task<IReadOnlyList<Account>> GetForUpdateAsync(IReadOnlyList<string> accountIdsInLockOrder, CancellationToken ct);
  Task AddAsync(Account account, CancellationToken ct);
  Task UpdateAsync(Account account, CancellationToken ct);
}
