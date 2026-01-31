using MediatR;
using PagueVeloz.TransactionProcessor.Application.Abstractions;
using PagueVeloz.TransactionProcessor.Application.Contracts.Accounts;

namespace PagueVeloz.TransactionProcessor.Application.Queries.GetAccount;

public sealed class GetAccountHandler : IRequestHandler<GetAccountQuery, AccountResponse?>
{
  private readonly IAccountRepository _accounts;

  public GetAccountHandler(IAccountRepository accounts) => _accounts = accounts;

  public async Task<AccountResponse?> Handle(GetAccountQuery request, CancellationToken ct)
  {
    var acc = await _accounts.GetAsync(request.AccountId, ct);
    if (acc is null) return null;

    return new AccountResponse(
      AccountId: acc.AccountId,
      ClientId: acc.ClientId,
      Currency: acc.Currency,
      Balance: acc.Balance,
      ReservedBalance: acc.ReservedBalance,
      AvailableBalance: acc.AvailableBalance,
      CreditLimit: acc.CreditLimit,
      Status: acc.Status.ToString().ToLowerInvariant()
    );
  }
}
