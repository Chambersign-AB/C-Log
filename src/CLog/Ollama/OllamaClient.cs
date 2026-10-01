using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CLog.Configuration;
using CLog.Model;

namespace CLog.Ollama;

/// <summary>Talks to a local Ollama instance over /api/chat.</summary>
public sealed class OllamaClient : IOllamaClient
{
    private const string SystemPrompt =
        """
        You triage error logs for a .NET team. You are given one error and the team's list of
        known errors. Decide which of three things the error is:

        NOISE   - not worth a human's attention: an expected timeout, a cancelled request,
                  a client sending bad input, a health probe failing during a deploy.
        KNOWN   - already described in the known errors list. Say which entry it matches.
        ANALYZE - anything else: it looks like a real defect and a human should look at it.

        Answer with one JSON object and nothing else, in exactly this shape:
        {"verdict": "NOISE|KNOWN|ANALYZE", "reason": "...", "known_solution": "..."}

        reason is one or two sentences. known_solution holds the fix from the known errors
        list when the verdict is KNOWN, and an empty string otherwise. The error has had
        personal data removed before reaching you; placeholders like [EMAIL] are expected.
        """;

    private readonly HttpClient _http;
    private readonly OllamaOptions _options;
    private readonly ILogger<OllamaClient> _logger;

    public OllamaClient(HttpClient http, IOptions<CLogOptions> options, ILogger<OllamaClient> logger)
    {
        _options = options.Value.Ollama;
        _logger = logger;
        _http = http;

        _http.BaseAddress = new Uri(_options.Url.TrimEnd('/') + "/");
        _http.Timeout = TimeSpan.FromSeconds(Math.Max(5, _options.TimeoutSeconds));
    }

    public async Task<TriageVerdict?> JudgeAsync(
        string errorReport,
        string knownErrors,
        CancellationToken cancellationToken = default)
    {
        var request = new ChatRequest
        {
            Model = _options.Model,
            Stream = false,
            Format = "json",
            Messages =
            [
                new ChatMessage { Role = "system", Content = SystemPrompt },
                new ChatMessage { Role = "user", Content = BuildUserPrompt(errorReport, knownErrors) }
            ]
        };

        string? content;
        try
        {
            using var response = await _http.PostAsJsonAsync("api/chat", request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Ollama answered {StatusCode} for model {Model}; leaving the error unjudged",
                    (int)response.StatusCode,
                    _options.Model);
                return null;
            }

            var body = await response.Content.ReadFromJsonAsync<ChatResponse>(cancellationToken);
            content = body?.Message?.Content;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // Ollama not running, a model still loading, or a truncated body: all recoverable,
            // all worth one warning and a retry on the next cycle.
            _logger.LogWarning(ex, "Could not reach Ollama at {Url}; leaving the error unjudged", _options.Url);
            return null;
        }

        if (VerdictParser.TryParse(content, out var verdict, out var error))
        {
            return verdict;
        }

        _logger.LogWarning(
            "Unusable answer from Ollama ({Error}). Raw answer: {Answer}",
            error,
            Truncate(content, 500));
        return null;
    }

    internal static string BuildUserPrompt(string errorReport, string knownErrors)
    {
        var known = string.IsNullOrWhiteSpace(knownErrors)
            ? "(the list is empty - nothing is known yet)"
            : knownErrors.Trim();

        return $"""
                # Known errors
                {known}

                # The error to judge
                {errorReport.Trim()}
                """;
    }

    private static string Truncate(string? text, int max)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "(empty)";
        }

        return text.Length <= max ? text : text[..max] + "...";
    }

    private sealed class ChatRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = "";

        [JsonPropertyName("messages")]
        public List<ChatMessage> Messages { get; set; } = [];

        [JsonPropertyName("stream")]
        public bool Stream { get; set; }

        /// <summary>Asks Ollama to constrain the answer to JSON. Advisory, so we still parse defensively.</summary>
        [JsonPropertyName("format")]
        public string Format { get; set; } = "json";
    }

    private sealed class ChatMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = "";

        [JsonPropertyName("content")]
        public string Content { get; set; } = "";
    }

    private sealed class ChatResponse
    {
        [JsonPropertyName("message")]
        public ChatMessage? Message { get; set; }
    }
}
