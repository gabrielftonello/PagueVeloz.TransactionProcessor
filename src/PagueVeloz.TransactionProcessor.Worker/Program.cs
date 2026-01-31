using System.Text.Json;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using PagueVeloz.TransactionProcessor.Application;
using PagueVeloz.TransactionProcessor.Application.Commands.ProcessTransaction;
using PagueVeloz.TransactionProcessor.Application.Contracts.Transactions;
using PagueVeloz.TransactionProcessor.Infrastructure;
using PagueVeloz.TransactionProcessor.Infrastructure.Persistence;
using PagueVeloz.TransactionProcessor.Worker;
using Serilog;
using Serilog.Formatting.Compact;

var builder = Host.CreateApplicationBuilder(args);

var logCfg = new LoggerConfiguration()
  .Enrich.FromLogContext()
  .WriteTo.Console(new RenderedCompactJsonFormatter());

var logPath = builder.Configuration["Logging:FilePath"];
if (!string.IsNullOrWhiteSpace(logPath))
{
  logCfg.WriteTo.File(
    formatter: new RenderedCompactJsonFormatter(),
    path: logPath,
    rollingInterval: RollingInterval.Day,
    shared: true,
    flushToDiskInterval: TimeSpan.FromSeconds(1));
}

Log.Logger = logCfg.CreateLogger();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddOpenTelemetry()
  .WithTracing(tracerProviderBuilder =>
  {
    tracerProviderBuilder.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("PagueVeloz.TransactionProcessor.Worker"));

    var otlp = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
    if (!string.IsNullOrWhiteSpace(otlp))
      tracerProviderBuilder.AddOtlpExporter(o => o.Endpoint = new Uri(otlp));
  });

builder.Services.AddOpenTelemetry()
  .WithMetrics(meterProviderBuilder =>
  {
    meterProviderBuilder
      .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("PagueVeloz.TransactionProcessor.Worker"))
      .AddRuntimeInstrumentation()
      .AddProcessInstrumentation();

    var otlp = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
    if (!string.IsNullOrWhiteSpace(otlp))
      meterProviderBuilder.AddOtlpExporter(o => o.Endpoint = new Uri(otlp));
  });

builder.Services.AddScoped<OutboxPublisher>();
builder.Services.AddHostedService<OutboxPublisherHostedService>();
builder.Services.AddScoped<QueuedCommandProcessor>();
builder.Services.AddHostedService<QueuedCommandHostedService>();

builder.Logging.ClearProviders();
builder.Logging.AddSerilog(Log.Logger, dispose: true);

var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
  var db = scope.ServiceProvider.GetRequiredService<TransactionDbContext>();
  await db.Database.EnsureCreatedAsync();
}

await host.RunAsync();

internal sealed class OutboxPublisherHostedService : BackgroundService
{
  private readonly IServiceProvider _sp;
  private readonly Serilog.ILogger _log = Log.ForContext<OutboxPublisherHostedService>();

  public OutboxPublisherHostedService(IServiceProvider sp) => _sp = sp;

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    var pollMs = 500;

    while (!stoppingToken.IsCancellationRequested)
    {
      try
      {
        using var scope = _sp.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<OutboxPublisher>();
        await processor.RunOnceAsync(stoppingToken);
      }
      catch (Exception ex)
      {
        _log.Error(ex, "Outbox publisher loop error");
      }

      await Task.Delay(pollMs, stoppingToken);
    }
  }
}

internal sealed class QueuedCommandHostedService : BackgroundService
{
  private readonly IServiceProvider _sp;
  private readonly Serilog.ILogger _log = Log.ForContext<QueuedCommandHostedService>();

  public QueuedCommandHostedService(IServiceProvider sp) => _sp = sp;

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    var pollMs = 250;

    while (!stoppingToken.IsCancellationRequested)
    {
      try
      {
        using var scope = _sp.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<QueuedCommandProcessor>();
        await processor.RunOnceAsync(stoppingToken);
      }
      catch (Exception ex)
      {
        _log.Error(ex, "Command processor loop error");
      }

      await Task.Delay(pollMs, stoppingToken);
    }
  }
}
