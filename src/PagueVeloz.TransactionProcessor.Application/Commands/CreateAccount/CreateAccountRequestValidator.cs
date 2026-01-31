using FluentValidation;
using PagueVeloz.TransactionProcessor.Application.Contracts.Accounts;

namespace PagueVeloz.TransactionProcessor.Application.Commands.CreateAccount;

public sealed class CreateAccountRequestValidator : AbstractValidator<CreateAccountRequest>
{
  public CreateAccountRequestValidator()
  {
    RuleFor(x => x.ClientId).NotEmpty().MaximumLength(64);
    RuleFor(x => x.AccountId).MaximumLength(64);
    RuleFor(x => x.Currency).NotEmpty().Length(3, 3);
    RuleFor(x => x.InitialBalance).GreaterThanOrEqualTo(0);
    RuleFor(x => x.CreditLimit).GreaterThanOrEqualTo(0);
  }
}
