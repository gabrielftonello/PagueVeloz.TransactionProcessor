using FluentValidation;
using PagueVeloz.TransactionProcessor.Application.Commands.ProcessTransaction;

namespace PagueVeloz.TransactionProcessor.Application.Commands.EnqueueTransaction;

public sealed class EnqueueTransactionCommandValidator : AbstractValidator<EnqueueTransactionCommand>
{
  public EnqueueTransactionCommandValidator()
  {
    RuleFor(x => x.Request).SetValidator(new TransactionRequestValidator());
  }
}
