using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CLog.Configuration;

namespace CLog.Analysis;

/// <summary>Files issues through GitHub's REST API. No GitHub Action and no CLI are involved.</summary>
public sealed class GitHubIssueTracker : IIssueTracker
{
    private readonly HttpClient _http;
    private readonly GitHubOptions _options;
    private readonly ILogger<GitHubIssueTracker> _logger;

    public GitHubIssueTracker(HttpClient http, IOptions<CLogOptions> options, ILogger<GitHubIssueTracker> logger)
    {
        _options = options.Value.Analysis.GitHub;
        _logger = logger;
        _http = http;

        _http.BaseAddress = new Uri(_options.ApiUrl.TrimEnd('/') + "/");
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CLog", "1.0"));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        if (!string.IsNullOrWhiteSpace(_options.Token))
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.Token);
        }
    }

    public async Task<int?> CreateIssueAsync(string title, string body, CancellationToken cancellationToken = default)
    {
        var created = await PostAsync(
            $"repos/{_options.Repository}/issues",
            new IssueRequest { Title = title, Body = body, Labels = [_options.Label] },
            cancellationToken);

        return created?.Number;
    }

    public async Task<bool> AddCommentAsync(int issueNumber, string body, CancellationToken cancellationToken = default) =>
        await PostAsync(
            $"repos/{_options.Repository}/issues/{issueNumber}/comments",
            new CommentRequest { Body = body },
            cancellationToken) is not null;

    private async Task<Created?> PostAsync<T>(string path, T payload, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.PostAsJsonAsync(path, payload, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "GitHub answered {StatusCode} for {Path}; nothing was filed",
                    (int)response.StatusCode,
                    path);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<Created>(cancellationToken) ?? new Created();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Could not reach GitHub at {Url}; nothing was filed", _options.ApiUrl);
            return null;
        }
    }

    private sealed class IssueRequest
    {
        [JsonPropertyName("title")]
        public string Title { get; set; } = "";

        [JsonPropertyName("body")]
        public string Body { get; set; } = "";

        [JsonPropertyName("labels")]
        public List<string> Labels { get; set; } = [];
    }

    private sealed class CommentRequest
    {
        [JsonPropertyName("body")]
        public string Body { get; set; } = "";
    }

    private sealed class Created
    {
        [JsonPropertyName("number")]
        public int? Number { get; set; }
    }
}
