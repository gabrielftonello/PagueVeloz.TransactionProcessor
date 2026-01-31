using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PagueVeloz.TransactionProcessor.Application.Abstractions;
using PagueVeloz.TransactionProcessor.Infrastructure.Messaging;
using PagueVeloz.TransactionProcessor.Infrastructure.Outbox;
using PagueVeloz.TransactionProcessor.Infrastructure.Persistence;
using PagueVeloz.TransactionProcessor.Infrastructure.Repositories;

namespace PagueVeloz.TransactionProcessor.Infrastructure;

public static class DependencyInjection
{
  public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
  {
    services.AddDbContext<TransactionDbContext>(opt =>
    {
      opt.UseSqlServer(config.GetConnectionString("SqlServer"));
    });

    services.AddScoped<IUnitOfWork, SqlServerUnitOfWork>();
    services.AddSingleton<IClock, SystemClock>();

    services.AddScoped<IAccountRepository, SqlServerAccountRepository>();
    services.AddScoped<ITransactionStore, SqlServerTransactionStore>();
    services.AddScoped<ILedgerStore, SqlServerLedgerStore>();
    services.AddScoped<IOutboxStore, SqlServerOutboxStore>();
    services.AddScoped<ICommandQueue, SqlServerCommandQueue>();

    services.AddScoped<IOutboxProcessingStore, SqlServerOutboxProcessingStore>();

    services.AddSingleton(sp =>
    {
      var section = config.GetSection("RabbitMq");
      var options = new RabbitMqOptions(
        Host: section.GetValue<string>("Host") ?? "rabbitmq",
        Port: section.GetValue<int?>("Port") ?? 5672,
        Username: section.GetValue<string>("Username") ?? "guest",
        Password: section.GetValue<string>("Password") ?? "guest",
        VirtualHost: section.GetValue<string>("VirtualHost") ?? "/",
        Exchange: section.GetValue<string>("Exchange") ?? "tx.events"
      );
      return options;
    });

    services.AddSingleton<IEventPublisher>(sp =>
    {
      var options = sp.GetRequiredService<RabbitMqOptions>();
      return new RabbitMqEventPublisher(options);
    });

    return services;
  }
}
