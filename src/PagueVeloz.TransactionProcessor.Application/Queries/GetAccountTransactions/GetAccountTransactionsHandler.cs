using MediatR;
using PagueVeloz.TransactionProcessor.Application.Abstractions;
using PagueVeloz.TransactionProcessor.Application.Contracts.Transactions;
using PagueVeloz.TransactionProcessor.Domain.Transactions;

namespace PagueVeloz.TransactionProcessor.Application.Queries.GetAccountTransactions;

public sealed class GetAccountTransactionsHandler(ITransactionStore transactionStore)
  : IRequestHandler<GetAccountTransactionsQuery, IReadOnlyList<TransactionResponse>>
{
  public async Task<IReadOnlyList<TransactionResponse>> Handle(
    GetAccountTransactionsQuery request,
    CancellationToken ct)
  {
    var txs = await transactionStore.ListByAccountIdAsync(request.AccountId, ct);

    return txs.Select(Map).ToList();
  }

  private static TransactionResponse Map(PersistedTransaction tx)
    => new(
      TransactionId: tx.TransactionId,
      Status: tx.Status.ToString().ToLowerInvariant(),
      Balance: tx.BalanceAfter,
      ReservedBalance: tx.ReservedBalanceAfter,
      AvailableBalance: tx.AvailableBalanceAfter,
      Timestamp: tx.Timestamp.ToUniversalTime().ToString("O"),
      ErrorMessage: tx.ErrorMessage
    );
}
