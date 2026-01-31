using MediatR;
using PagueVeloz.TransactionProcessor.Application.Contracts.Accounts;

namespace PagueVeloz.TransactionProcessor.Application.Queries.GetAccount;

public sealed record GetAccountQuery(string AccountId) : IRequest<AccountResponse?>;
