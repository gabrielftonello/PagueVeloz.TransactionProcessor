using FluentValidation;

namespace PagueVeloz.TransactionProcessor.Application.Commands.ProcessTransaction;

public sealed class ProcessTransactionCommandValidator : AbstractValidator<ProcessTransactionCommand>
{
  public ProcessTransactionCommandValidator()
  {
    RuleFor(x => x.Request).SetValidator(new TransactionRequestValidator());
  }
}
