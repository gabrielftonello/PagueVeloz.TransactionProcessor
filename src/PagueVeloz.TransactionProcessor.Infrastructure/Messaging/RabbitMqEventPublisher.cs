using System.Text;
using RabbitMQ.Client;

namespace PagueVeloz.TransactionProcessor.Infrastructure.Messaging;

public sealed class RabbitMqEventPublisher : IEventPublisher, IDisposable
{
  private readonly RabbitMqOptions _options;
  private readonly string _exchange;
  private readonly object _sync = new();
  private IConnection? _connection;
  private IModel? _channel;

  public RabbitMqEventPublisher(RabbitMqOptions options)
  {
    _options = options;
    _exchange = options.Exchange;
  }

  public Task PublishAsync(string eventType, string payloadJson, IDictionary<string, object?> headers, CancellationToken ct)
  {
    // IModel is not thread-safe; serialize publish per instance.
    lock (_sync)
    {
      var channel = GetOrCreateChannel_NoLock();

      var props = channel.CreateBasicProperties();
      props.DeliveryMode = 2; // persistent
      props.ContentType = "application/json";
      props.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
      props.Headers = headers
        .Where(kv => kv.Value is not null)
        .ToDictionary(kv => kv.Key, kv => (object)kv.Value!);

      var body = Encoding.UTF8.GetBytes(payloadJson);

      channel.BasicPublish(
        exchange: _exchange,
        routingKey: eventType,
        basicProperties: props,
        body: body);
    }

    return Task.CompletedTask;
  }

  /// <summary>
  /// Lazily opens the connection/channel on first publish. Must be called under <see cref="_sync"/>.
  /// </summary>
  private IModel GetOrCreateChannel_NoLock()
  {
    if (_channel is not null && _connection is not null && _connection.IsOpen)
      return _channel;

    DisposeConnection_NoThrow();

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

    _connection = factory.CreateConnection();
    _channel = _connection.CreateModel();
    _channel.ExchangeDeclare(exchange: _exchange, type: ExchangeType.Topic, durable: true, autoDelete: false);
    return _channel;
  }

  public void Dispose()
  {
    lock (_sync)
    {
      DisposeConnection_NoThrow();
    }
  }

  private void DisposeConnection_NoThrow()
  {
    try { _channel?.Close(); } catch { }
    try { _connection?.Close(); } catch { }
    try { _channel?.Dispose(); } catch { }
    try { _connection?.Dispose(); } catch { }
    _channel = null;
    _connection = null;
  }
}

public sealed record RabbitMqOptions(
  string Host,
  int Port,
  string Username,
  string Password,
  string VirtualHost,
  string Exchange
);
