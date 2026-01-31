using PagueVeloz.TransactionProcessor.Application.Abstractions;

namespace PagueVeloz.TransactionProcessor.Infrastructure;

public sealed class SystemClock : IClock
{
  public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
