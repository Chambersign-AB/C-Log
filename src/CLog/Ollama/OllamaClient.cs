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

        var (reached, content) = await SendAsync(request, cancellationToken);
        if (!reached)
        {
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

    public async Task<string?> AskAsync(
        string prompt,
        CancellationToken cancellationToken = default,
        string? model = null)
    {
        // No JSON format here: the answer is one plain word, and asking for JSON makes a small
        // model wrap it in an object of its own invention.
        var request = new ChatRequest
        {
            Model = string.IsNullOrWhiteSpace(model) ? _options.Model : model,
            Stream = false,
            Format = null,
            Messages = [new ChatMessage { Role = "user", Content = prompt }]
        };

        var (reached, content) = await SendAsync(request, cancellationToken);
        return reached ? content ?? "" : null;
    }

    private async Task<(bool Reached, string? Content)> SendAsync(
        ChatRequest request,
        CancellationToken cancellationToken)
    {
        // Set here, on the one path every request takes, so no call can be sent without them:
        // the same error must get the same verdict whenever it is asked about.
        request.Options = new SamplingOptions { Temperature = _options.Temperature, Seed = _options.Seed };

        try
        {
            using var response = await _http.PostAsJsonAsync("api/chat", request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Ollama answered {StatusCode} for model {Model}; leaving the error unjudged",
                    (int)response.StatusCode,
                    request.Model);
                return (false, null);
            }

            var body = await response.Content.ReadFromJsonAsync<ChatResponse>(cancellationToken);
            return (true, body?.Message?.Content);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            // HttpClient reports its own timeout this way. The size is logged because it is the
            // usual cause: a long analysis prompt on a CPU does not finish in time, and without
            // the numbers that looks the same as a model that is down.
            var characters = request.Messages.Sum(m => m.Content.Length);
            _logger.LogWarning(
                "Ollama model {Model} did not answer within {Timeout} s. The request was {Lines} line(s), {Characters} characters, about {Tokens} tokens; leaving the error unjudged",
                request.Model,
                (int)_http.Timeout.TotalSeconds,
                request.Messages.Sum(m => m.Content.Split('\n').Length),
                characters,
                EstimateTokens(characters));
            return (false, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            // Ollama not running, a model still loading, or a truncated body: all recoverable,
            // all worth one warning and a retry on the next cycle.
            _logger.LogWarning(ex, "Could not reach Ollama at {Url}; leaving the error unjudged", _options.Url);
            return (false, null);
        }
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

    /// <summary>
    /// A rule of thumb of four characters to a token, not the model's own count: Ollama only
    /// reports that in an answer, and a request that timed out has none.
    /// </summary>
    internal static int EstimateTokens(int characters) => (characters + 3) / 4;

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
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Format { get; set; } = "json";

        [JsonPropertyName("options")]
        public SamplingOptions Options { get; set; } = new();
    }

    private sealed class SamplingOptions
    {
        [JsonPropertyName("temperature")]
        public double Temperature { get; set; }

        [JsonPropertyName("seed")]
        public int Seed { get; set; }
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
