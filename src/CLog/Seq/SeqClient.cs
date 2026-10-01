using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CLog.Configuration;
using CLog.Model;

namespace CLog.Seq;

/// <summary>Reads Error events from Seq's HTTP API.</summary>
public sealed class SeqClient : ISeqClient
{
    public const string ApiKeyHeader = "X-Seq-ApiKey";

    private readonly HttpClient _http;
    private readonly SeqOptions _options;
    private readonly ILogger<SeqClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public SeqClient(HttpClient http, IOptions<CLogOptions> options, ILogger<SeqClient> logger)
    {
        _options = options.Value.Seq;
        _logger = logger;
        _http = http;

        _http.BaseAddress = new Uri(_options.Url.TrimEnd('/') + "/");
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _http.DefaultRequestHeaders.Remove(ApiKeyHeader);
            _http.DefaultRequestHeaders.Add(ApiKeyHeader, _options.ApiKey);
        }
    }

    public async Task<IReadOnlyList<SeqEvent>> GetErrorEventsAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken = default)
    {
        var requestUri = BuildRequestUri(since);
        _logger.LogDebug("Fetching errors from Seq: {RequestUri}", requestUri);

        JsonElement payload;
        try
        {
            using var response = await _http.GetAsync(requestUri, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new SeqUnavailableException(Describe(response.StatusCode));
            }

            payload = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw new SeqUnavailableException($"Seq at {_options.Url} could not be read: {ex.Message}", ex);
        }

        var events = ReadEvents(payload);

        _logger.LogInformation("Seq returned {Count} error event(s) since {Since:u}", events.Count, since);
        return events;
    }

    /// <summary>A wrong or missing API key is the likeliest setup mistake, so it gets named.</summary>
    private string Describe(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
            $"Seq at {_options.Url} rejected the API key ({(int)statusCode}). "
            + "Set CLog:Seq:ApiKey through user-secrets or the environment.",
        _ => $"Seq at {_options.Url} answered {(int)statusCode}"
    };

    internal string BuildRequestUri(DateTimeOffset since)
    {
        var filter = "@Level = 'Error'";
        if (!string.IsNullOrWhiteSpace(_options.Filter))
        {
            filter = $"({filter}) and ({_options.Filter})";
        }

        var query = new List<string>
        {
            "count=" + Math.Max(1, _options.MaxEvents),
            "render=true",
            "fromDateUtc=" + Uri.EscapeDataString(since.UtcDateTime.ToString("O")),
            "filter=" + Uri.EscapeDataString(filter)
        };

        return "api/events?" + string.Join('&', query);
    }

    /// <summary>
    /// Seq answers with a bare array on some endpoints and an object holding "Events" on
    /// others; both shapes are accepted so a Seq upgrade does not silence the watcher.
    /// </summary>
    private List<SeqEvent> ReadEvents(JsonElement payload)
    {
        var source = payload.ValueKind switch
        {
            JsonValueKind.Array => payload,
            JsonValueKind.Object when payload.TryGetProperty("Events", out var wrapped) => wrapped,
            _ => default
        };

        if (source.ValueKind != JsonValueKind.Array)
        {
            _logger.LogWarning("Unexpected response shape from Seq: {Kind}", payload.ValueKind);
            return [];
        }

        var events = new List<SeqEvent>();
        foreach (var element in source.EnumerateArray())
        {
            try
            {
                var dto = element.Deserialize<SeqEventDto>(JsonOptions);
                if (dto is not null)
                {
                    events.Add(dto.ToSeqEvent());
                }
            }
            catch (JsonException ex)
            {
                // One malformed event must not cost us the rest of the batch.
                _logger.LogWarning(ex, "Skipping a Seq event that could not be parsed");
            }
        }

        return events;
    }
}
