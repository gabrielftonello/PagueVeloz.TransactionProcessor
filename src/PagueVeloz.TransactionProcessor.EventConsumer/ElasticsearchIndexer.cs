using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace PagueVeloz.TransactionProcessor.EventConsumer;

public sealed class ElasticsearchIndexer
{
  private readonly HttpClient _http;
  private readonly ElasticsearchOptions _options;

  public ElasticsearchIndexer(HttpClient http, IOptions<ElasticsearchOptions> options)
  {
    _http = http;
    _options = options.Value;
  }

  public bool IsEnabled => !string.IsNullOrWhiteSpace(_options.Url);

  public async Task IndexAsync(object document, CancellationToken ct)
  {
    if (!IsEnabled)
      return;

    var urlBase = _options.Url!.TrimEnd('/');
    var indexName = $"{_options.IndexPrefix}-{DateTimeOffset.UtcNow:yyyy.MM.dd}";
    var uri = new Uri($"{urlBase}/{indexName}/_doc");

    for (var attempt = 1; attempt <= 3; attempt++)
    {
      try
      {
        var resp = await _http.PostAsJsonAsync(uri, document, ct);
        if (resp.IsSuccessStatusCode)
          return;

        var body = await resp.Content.ReadAsStringAsync(ct);
        throw new HttpRequestException($"Elasticsearch indexing failed (HTTP {(int)resp.StatusCode}): {body}");
      }
      catch when (attempt < 3)
      {
        await Task.Delay(TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt - 1)), ct);
      }
    }
  }
}
