using System.Text;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Serilog;

namespace PagueVeloz.TransactionProcessor.EventConsumer;

public sealed class RabbitMqConsumerWorker : BackgroundService
{
  private readonly RabbitMqConsumerOptions _options;
  private readonly ElasticsearchIndexer _indexer;

  private IConnection? _connection;
  private IModel? _channel;

  public RabbitMqConsumerWorker(IOptions<RabbitMqConsumerOptions> options, ElasticsearchIndexer indexer)
  {
    _options = options.Value;
    _indexer = indexer;
  }

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    await ConnectWithRetryAsync(stoppingToken);

    if (_channel is null)
      throw new InvalidOperationException("RabbitMQ channel not initialized.");

    _channel.BasicQos(0, 50, global: false);

    var consumer = new AsyncEventingBasicConsumer(_channel);
    consumer.Received += async (_, ea) =>
    {
      var now = DateTimeOffset.UtcNow;
      var body = Encoding.UTF8.GetString(ea.Body.ToArray());
      var headers = ea.BasicProperties?.Headers?
        .ToDictionary(
          kv => kv.Key,
          kv => kv.Value is byte[] b ? Encoding.UTF8.GetString(b) : kv.Value?.ToString())
        ?? new Dictionary<string, string?>();

      var routingKey = ea.RoutingKey;

      Log.Information("Consumed integration event {EventType} deliveryTag={DeliveryTag} bytes={Bytes}",
        routingKey, ea.DeliveryTag, ea.Body.Length);

      if (_indexer.IsEnabled)
      {
        await _indexer.IndexAsync(new
        {
          @timestamp = now,
          event_type = routingKey,
          exchange = ea.Exchange,
          queue = _options.Queue,
          delivery_tag = ea.DeliveryTag,
          headers,
          payload = TryParseJsonOrRaw(body)
        }, stoppingToken);
      }

      Log.Information("Event payload {EventType}: {Payload}", routingKey, body);

      _channel.BasicAck(ea.DeliveryTag, multiple: false);
    };

    _channel.BasicConsume(queue: _options.Queue, autoAck: false, consumer: consumer);

    Log.Information("RabbitMQ consumer started. exchange={Exchange} queue={Queue} bindingKey={BindingKey} elastic={Elastic}",
      _options.Exchange, _options.Queue, _options.BindingKey, _indexer.IsEnabled);

    while (!stoppingToken.IsCancellationRequested)
      await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
  }

  private async Task ConnectWithRetryAsync(CancellationToken ct)
  {
    var factory = new ConnectionFactory
    {
      HostName = _options.Host,
      Port = _options.Port,
      UserName = _options.Username,
      Password = _options.Password,
      VirtualHost = _options.VirtualHost,
      DispatchConsumersAsync = true,
      AutomaticRecoveryEnabled = true
    };

    for (var attempt = 1; attempt <= 60; attempt++)
    {
      try
      {
        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();

        _channel.ExchangeDeclare(exchange: _options.Exchange, type: ExchangeType.Topic, durable: true, autoDelete: false);
        _channel.QueueDeclare(queue: _options.Queue, durable: true, exclusive: false, autoDelete: false);
        _channel.QueueBind(queue: _options.Queue, exchange: _options.Exchange, routingKey: _options.BindingKey);

        return;
      }
      catch (Exception ex) when (attempt < 60)
      {
        var delay = TimeSpan.FromMilliseconds(Math.Min(10_000, 200 * Math.Pow(2, Math.Min(attempt - 1, 6))));
        Log.Warning(ex, "RabbitMQ not ready (attempt {Attempt}). Retrying in {DelayMs}ms...",
          attempt, (int)delay.TotalMilliseconds);
        await Task.Delay(delay, ct);
      }
    }

    throw new InvalidOperationException("RabbitMQ connection retry budget exhausted.");
  }

  private static object TryParseJsonOrRaw(string text)
  {
    try
    {
      return System.Text.Json.JsonSerializer.Deserialize<object>(text) ?? text;
    }
    catch
    {
      return text;
    }
  }

  public override void Dispose()
  {
    try { _channel?.Close(); } catch { }
    try { _connection?.Close(); } catch { }
    try { _channel?.Dispose(); } catch { }
    try { _connection?.Dispose(); } catch { }
    base.Dispose();
  }
}
