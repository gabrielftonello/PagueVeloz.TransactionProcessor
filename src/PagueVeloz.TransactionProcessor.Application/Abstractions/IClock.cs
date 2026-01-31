namespace PagueVeloz.TransactionProcessor.Application.Abstractions;

public interface IClock
{
  DateTimeOffset UtcNow { get; }
}
