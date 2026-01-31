using FluentValidation;

namespace PagueVeloz.TransactionProcessor.Application.Commands.CreateAccount;

public sealed class CreateAccountCommandValidator : AbstractValidator<CreateAccountCommand>
{
  public CreateAccountCommandValidator()
  {
    RuleFor(x => x.Request).SetValidator(new CreateAccountRequestValidator());
  }
}
