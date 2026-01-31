using MediatR;
using PagueVeloz.TransactionProcessor.Application.Contracts.Accounts;

namespace PagueVeloz.TransactionProcessor.Application.Commands.CreateAccount;

public sealed record CreateAccountCommand(CreateAccountRequest Request) : IRequest<AccountResponse>;
