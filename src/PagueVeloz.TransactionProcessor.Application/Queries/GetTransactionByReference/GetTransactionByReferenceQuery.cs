using MediatR;
using PagueVeloz.TransactionProcessor.Application.Contracts.Transactions;

namespace PagueVeloz.TransactionProcessor.Application.Queries.GetTransactionByReference;

public sealed record GetTransactionByReferenceQuery(string ReferenceId) : IRequest<TransactionResponse?>;
