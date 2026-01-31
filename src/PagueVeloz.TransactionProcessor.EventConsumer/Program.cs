using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Formatting.Compact;

namespace PagueVeloz.TransactionProcessor.EventConsumer;

public static class Program
{
  public static async Task Main(string[] args)
  {
    var builder = Host.CreateApplicationBuilder(args);

    builder.Logging.ClearProviders();

    Log.Logger = new LoggerConfiguration()
      .ReadFrom.Configuration(builder.Configuration)
      .Enrich.FromLogContext()
      .WriteTo.Console(new RenderedCompactJsonFormatter())
      .CreateLogger();

    var logPath = builder.Configuration["Logging:FilePath"];
    if (!string.IsNullOrWhiteSpace(logPath))
    {
      Log.Logger = new LoggerConfiguration()
        .ReadFrom.Configuration(builder.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console(new RenderedCompactJsonFormatter())
        .WriteTo.File(new RenderedCompactJsonFormatter(), logPath, rollingInterval: RollingInterval.Day, shared: true, flushToDiskInterval: TimeSpan.FromSeconds(1))
        .CreateLogger();
    }

    builder.Services.AddSerilog();

    builder.Services.Configure<RabbitMqConsumerOptions>(builder.Configuration.GetSection("RabbitMq"));
    builder.Services.Configure<ElasticsearchOptions>(builder.Configuration.GetSection("Elasticsearch"));

    builder.Services.AddHttpClient<ElasticsearchIndexer>();
    builder.Services.AddHostedService<RabbitMqConsumerWorker>();

    using var host = builder.Build();
    await host.RunAsync();
  }
}
