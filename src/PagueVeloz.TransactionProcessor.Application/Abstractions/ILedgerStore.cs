using PagueVeloz.TransactionProcessor.Domain.Events;

namespace PagueVeloz.TransactionProcessor.Application.Abstractions;

public interface ILedgerStore
{
  Task AppendAsync(AccountEvent evt, CancellationToken ct);
}
