using FluentValidation;
using PagueVeloz.TransactionProcessor.Application.Contracts.Transactions;

namespace PagueVeloz.TransactionProcessor.Application.Commands.ProcessTransaction;

public sealed class TransactionRequestValidator : AbstractValidator<TransactionRequest>
{
  public TransactionRequestValidator()
  {
    RuleFor(x => x.Operation).NotEmpty().MaximumLength(16);
    RuleFor(x => x.AccountId).NotEmpty().MaximumLength(64);
    RuleFor(x => x.Amount).GreaterThan(0);
    RuleFor(x => x.Currency).NotEmpty().Length(3, 3);
    RuleFor(x => x.ReferenceId).NotEmpty().MaximumLength(128);

    When(x => x.Operation.Trim().Equals("transfer", StringComparison.OrdinalIgnoreCase), () =>
    {
      RuleFor(x => x.TargetAccountId).NotEmpty().MaximumLength(64);
    });

    When(x => x.Operation.Trim().Equals("reversal", StringComparison.OrdinalIgnoreCase), () =>
    {
      RuleFor(x => x.RelatedReferenceId).NotEmpty().MaximumLength(128);
    });

    When(x => x.Operation.Trim().Equals("capture", StringComparison.OrdinalIgnoreCase), () =>
    {
      RuleFor(x => x.RelatedReferenceId).NotEmpty().MaximumLength(128);
    });
  }
}
