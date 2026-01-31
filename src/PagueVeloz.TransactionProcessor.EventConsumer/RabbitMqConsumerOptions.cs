namespace PagueVeloz.TransactionProcessor.EventConsumer;

public sealed record RabbitMqConsumerOptions
{
  public string Host { get; init; } = "localhost";

  public int Port { get; init; } = 5672;

  public string Username { get; init; } = "guest";

  public string Password { get; init; } = "guest";

  public string VirtualHost { get; init; } = "/";

  public string Exchange { get; init; } = "tx.events";

  public string Queue { get; init; } = "tx.events.audit";

  public string BindingKey { get; init; } = "#";
}
