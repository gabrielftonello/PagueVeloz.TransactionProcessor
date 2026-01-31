using MediatR;
using PagueVeloz.TransactionProcessor.Application.Contracts.Transactions;

namespace PagueVeloz.TransactionProcessor.Application.Commands.EnqueueTransaction;

public sealed record EnqueueTransactionCommand(TransactionRequest Request) : IRequest<TransactionResponse>;
