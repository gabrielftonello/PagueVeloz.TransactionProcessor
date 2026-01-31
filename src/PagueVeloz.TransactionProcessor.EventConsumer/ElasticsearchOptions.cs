namespace PagueVeloz.TransactionProcessor.EventConsumer;


public sealed record ElasticsearchOptions
{
  public string? Url { get; init; }

  public string IndexPrefix { get; init; } = "pv-events";
}
