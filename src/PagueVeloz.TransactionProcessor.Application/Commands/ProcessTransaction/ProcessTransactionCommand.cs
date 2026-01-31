using MediatR;
using PagueVeloz.TransactionProcessor.Application.Contracts.Transactions;

namespace PagueVeloz.TransactionProcessor.Application.Commands.ProcessTransaction;

public sealed record ProcessTransactionCommand(TransactionRequest Request) : IRequest<TransactionResponse>;
