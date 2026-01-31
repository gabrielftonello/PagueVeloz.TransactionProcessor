using MediatR;
using PagueVeloz.TransactionProcessor.Application.Contracts.Transactions;

namespace PagueVeloz.TransactionProcessor.Application.Queries.GetAccountTransactions;

public sealed record GetAccountTransactionsQuery(string AccountId)
  : IRequest<IReadOnlyList<TransactionResponse>>;
