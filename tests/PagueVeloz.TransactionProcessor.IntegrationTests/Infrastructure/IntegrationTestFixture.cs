using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using RabbitMQ.Client;
using PagueVeloz.TransactionProcessor.Application;
using PagueVeloz.TransactionProcessor.Infrastructure;
using PagueVeloz.TransactionProcessor.Infrastructure.Persistence;
using PagueVeloz.TransactionProcessor.Worker;
using Xunit;

namespace PagueVeloz.TransactionProcessor.IntegrationTests.Infrastructure;

public sealed class IntegrationTestFixture : IAsyncLifetime
{
  private readonly MsSqlContainer _db;
  private readonly IContainer _rabbit;
  private CustomApiFactory? _apiFactory;

  public HttpClient Client { get; private set; } = default!;
  public string SqlConnectionString => _db.GetConnectionString();
  public string RabbitHost { get; private set; } = "localhost";
  public int RabbitPort { get; private set; }
  public string ExchangeName { get; } = "tx.events";

  public IntegrationTestFixture()
  {
    _db = new MsSqlBuilder()
      .WithPassword("Your_password123")
      .Build();

    _rabbit = new ContainerBuilder()
      .WithImage("rabbitmq:3-management")
      .WithEnvironment("RABBITMQ_DEFAULT_USER", "guest")
      .WithEnvironment("RABBITMQ_DEFAULT_PASS", "guest")
      .WithPortBinding(5672, true)
      .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5672))
      .Build();
  }

  public async Task InitializeAsync()
  {
    await _db.StartAsync();
    await _rabbit.StartAsync();

    RabbitHost = _rabbit.Hostname;
    RabbitPort = _rabbit.GetMappedPublicPort(5672);

    _apiFactory = new CustomApiFactory(SqlConnectionString, RabbitHost, RabbitPort, ExchangeName);
    Client = _apiFactory.CreateClient();

    await EnsureDatabaseCreatedAsync();
  }

  public async Task DisposeAsync()
  {
    Client.Dispose();
    _apiFactory?.Dispose();
    await _rabbit.DisposeAsync();
    await _db.DisposeAsync();
  }

  public async Task EnsureDatabaseCreatedAsync()
  {
    await using var sp = BuildWorkerServiceProvider(rabbitHost: RabbitHost, rabbitPort: RabbitPort);
    using var scope = sp.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<TransactionDbContext>();
    await db.Database.EnsureCreatedAsync();
  }

  public ServiceProvider BuildWorkerServiceProvider(string rabbitHost, int rabbitPort, string? exchange = null)
  {
    var dict = new Dictionary<string, string?>
    {
      ["ConnectionStrings:SqlServer"] = SqlConnectionString,
      ["RabbitMq:Host"] = rabbitHost,
      ["RabbitMq:Port"] = rabbitPort.ToString(),
      ["RabbitMq:Username"] = "guest",
      ["RabbitMq:Password"] = "guest",
      ["RabbitMq:VirtualHost"] = "/",
      ["RabbitMq:Exchange"] = exchange ?? ExchangeName,
      ["Outbox:BatchSize"] = "200"
    };

    var config = new ConfigurationBuilder()
      .AddInMemoryCollection(dict)
      .Build();

    var services = new ServiceCollection();
    services.AddSingleton<IConfiguration>(config);
    services.AddApplication();
    services.AddInfrastructure(config);

    services.AddScoped<OutboxPublisher>();
    services.AddScoped<QueuedCommandProcessor>();

    return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
  }

  public async Task<Dictionary<string, object?>> CreateAccountAsync(string accountId, long initialBalance, long creditLimit, string currency = "BRL")
  {
    var createAcc = new Dictionary<string, object?>
    {
      ["client_id"] = "CLI-TEST",
      ["account_id"] = accountId,
      ["initial_balance"] = initialBalance,
      ["credit_limit"] = creditLimit,
      ["currency"] = currency
    };

    var resp = await Client.PostAsJsonAsync("/api/accounts", createAcc);
    var body = await resp.Content.ReadAsStringAsync();

    if (resp.StatusCode != HttpStatusCode.Created)
      throw new InvalidOperationException($"CreateAccount failed: {(int)resp.StatusCode} {resp.ReasonPhrase}. Body={body}");

    var payload = await resp.Content.ReadFromJsonAsync<Dictionary<string, object?>>();
    return payload ?? throw new InvalidOperationException("Empty account payload.");
  }

  public async Task<Dictionary<string, object?>> ProcessTransactionAsync(Dictionary<string, object?> tx)
  {
    var resp = await Client.PostAsJsonAsync("/api/transactions", tx);
    var body = await resp.Content.ReadAsStringAsync();

    if (!resp.IsSuccessStatusCode)
      throw new InvalidOperationException($"ProcessTransaction failed: {(int)resp.StatusCode} {resp.ReasonPhrase}. Body={body}");

    var payload = await resp.Content.ReadFromJsonAsync<Dictionary<string, object?>>();
    return payload ?? throw new InvalidOperationException("Empty transaction payload.");
  }

  public async Task<Dictionary<string, object?>> GetAccountAsync(string accountId)
  {
    var payload = await Client.GetFromJsonAsync<Dictionary<string, object?>>($"/api/accounts/{accountId}");
    return payload ?? throw new InvalidOperationException("Account not found / empty payload.");
  }

  public async Task<List<Dictionary<string, object?>>> GetAccountTransactionsAsync(Guid accountId)
  {
    var payload = await Client.GetFromJsonAsync<List<Dictionary<string, object?>>>($"/api/accounts/{accountId:D}/transactions");
    return payload ?? throw new InvalidOperationException("Transactions not found / empty payload.");
  }

  public RabbitMqTestQueue CreateRabbitQueue(string routingKey)
  {
    var factory = new ConnectionFactory
    {
      HostName = RabbitHost,
      Port = RabbitPort,
      UserName = "guest",
      Password = "guest",
      VirtualHost = "/",
      DispatchConsumersAsync = true,
      AutomaticRecoveryEnabled = true
    };

    var connection = factory.CreateConnection();
    var channel = connection.CreateModel();

    channel.ExchangeDeclare(exchange: ExchangeName, type: ExchangeType.Topic, durable: true, autoDelete: false);

    var q = channel.QueueDeclare(queue: "", durable: false, exclusive: true, autoDelete: true);
    channel.QueueBind(queue: q.QueueName, exchange: ExchangeName, routingKey: routingKey);

    return new RabbitMqTestQueue(connection, channel, q.QueueName);
  }

  public sealed class RabbitMqTestQueue : IDisposable
  {
    private readonly IConnection _conn;
    private readonly IModel _ch;
    public string QueueName { get; }

    internal RabbitMqTestQueue(IConnection conn, IModel ch, string queueName)
    {
      _conn = conn;
      _ch = ch;
      QueueName = queueName;
    }

    public async Task<(string RoutingKey, string Body, IDictionary<string, object?> Headers)> ReceiveOneAsync(TimeSpan timeout)
    {
      var sw = Stopwatch.StartNew();
      while (sw.Elapsed < timeout)
      {
        var result = _ch.BasicGet(queue: QueueName, autoAck: true);
        if (result is not null)
        {
          var body = Encoding.UTF8.GetString(result.Body.ToArray());
          var headers = result.BasicProperties?.Headers?.ToDictionary(kv => kv.Key, kv => kv.Value) ?? new Dictionary<string, object?>();
          return (result.RoutingKey, body, headers);
        }

        await Task.Delay(50);
      }

      throw new TimeoutException($"Timed out waiting for RabbitMQ message on queue '{QueueName}'.");
    }

    public void Dispose()
    {
      try { _ch.Close(); } catch { }
      try { _conn.Close(); } catch { }
      try { _ch.Dispose(); } catch { }
      try { _conn.Dispose(); } catch { }
    }
  }

  private sealed class CustomApiFactory : WebApplicationFactory<Api.Json.SnakeCaseLowerNamingPolicy>
  {
    private readonly string _cs;
    private readonly string _rabbitHost;
    private readonly int _rabbitPort;
    private readonly string _exchange;

    public CustomApiFactory(string connectionString, string rabbitHost, int rabbitPort, string exchange)
    {
      _cs = connectionString;
      _rabbitHost = rabbitHost;
      _rabbitPort = rabbitPort;
      _exchange = exchange;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
      builder.UseEnvironment("Test");

      builder.ConfigureAppConfiguration((ctx, cfg) =>
      {
        var dict = new Dictionary<string, string?>
        {
          ["ConnectionStrings:SqlServer"] = _cs,
          ["RabbitMq:Host"] = _rabbitHost,
          ["RabbitMq:Port"] = _rabbitPort.ToString(),
          ["RabbitMq:Username"] = "guest",
          ["RabbitMq:Password"] = "guest",
          ["RabbitMq:VirtualHost"] = "/",
          ["RabbitMq:Exchange"] = _exchange
        };

        cfg.AddInMemoryCollection(dict);
      });
    }
  }
}

