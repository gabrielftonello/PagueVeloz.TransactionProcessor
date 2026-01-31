namespace PagueVeloz.TransactionProcessor.Infrastructure.Messaging;

public interface IEventPublisher
{
  Task PublishAsync(string eventType, string payloadJson, IDictionary<string, object?> headers, CancellationToken ct);
}
