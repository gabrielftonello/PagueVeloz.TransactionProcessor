using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using PagueVeloz.TransactionProcessor.Application.Behaviors;
using PagueVeloz.TransactionProcessor.Application.Commands.CreateAccount;
using PagueVeloz.TransactionProcessor.Application.Commands.ProcessTransaction;

namespace PagueVeloz.TransactionProcessor.Application;

public static class DependencyInjection
{
  public static IServiceCollection AddApplication(this IServiceCollection services)
  {
    services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<CreateAccountCommand>());
    services.AddValidatorsFromAssemblyContaining<CreateAccountCommandValidator>();

    services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

    return services;
  }
}
