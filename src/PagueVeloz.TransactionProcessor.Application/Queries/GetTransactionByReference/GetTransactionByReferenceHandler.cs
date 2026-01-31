using MediatR;
using PagueVeloz.TransactionProcessor.Application.Abstractions;
using PagueVeloz.TransactionProcessor.Application.Contracts.Transactions;

namespace PagueVeloz.TransactionProcessor.Application.Queries.GetTransactionByReference;

public sealed class GetTransactionByReferenceHandler : IRequestHandler<GetTransactionByReferenceQuery, TransactionResponse?>
{
  private readonly ITransactionStore _txStore;

  public GetTransactionByReferenceHandler(ITransactionStore txStore) => _txStore = txStore;

  public async Task<TransactionResponse?> Handle(GetTransactionByReferenceQuery request, CancellationToken ct)
  {
    var tx = await _txStore.GetByReferenceIdAsync(request.ReferenceId, ct);
    if (tx is null) return null;

    return new TransactionResponse(
      TransactionId: tx.TransactionId,
      Status: tx.Status.ToString().ToLowerInvariant(),
      Balance: tx.BalanceAfter,
      ReservedBalance: tx.ReservedBalanceAfter,
      AvailableBalance: tx.AvailableBalanceAfter,
      Timestamp: tx.Timestamp.UtcDateTime.ToString("O"),
      ErrorMessage: tx.ErrorMessage
    );
  }
}
