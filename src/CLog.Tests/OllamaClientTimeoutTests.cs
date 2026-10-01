using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CLog.Analysis;
using CLog.Configuration;
using CLog.Ollama;
using CLog.Tests.Fakes;

namespace CLog.Tests;

/// <summary>A model that does not answer in time is reported with the size of what it was sent.</summary>
public class OllamaClientTimeoutTests
{
    // Three lines, 28 characters.
    private const string Prompt = "line one\nline two\nline three";

    private readonly TestLogger<OllamaClient> _logger = new();

    /// <summary>What HttpClient throws when its own timeout runs out.</summary>
    private static StubHttpMessageHandler TimingOut() => new(HttpStatusCode.OK, "")
    {
        ThrowOnSend = new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.", new TimeoutException())
    };

    private OllamaClient Client(StubHttpMessageHandler handler, IOptions<CLogOptions>? options = null) =>
        new(new HttpClient(handler), options ?? Options.Create(new CLogOptions()), _logger);

    [Fact]
    public async Task A_timeout_is_a_warning_that_says_how_much_was_sent()
    {
        var answer = await Client(TimingOut()).AskAsync(Prompt);

        Assert.Null(answer);
        var warning = Assert.Single(_logger.Warnings);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Contains("did not answer within 300 s", warning.Message);
        Assert.Contains("3 line(s), 28 characters, about 7 tokens", warning.Message);
    }

    [Fact]
    public async Task A_timeout_from_the_analysis_model_names_that_model_and_the_size_of_its_prompt()
    {
        var options = Options.Create(new CLogOptions
        {
            Ollama = new OllamaOptions { Model = "mistral", TimeoutSeconds = 120 },
            Analysis = new AnalysisOptions { Model = "mistral-small" }
        });
        var prompt = string.Join('\n', Enumerable.Repeat("0123456789", 400));
        var model = new OllamaAnalysisModel(Client(TimingOut(), options), options);

        var answer = await model.CompleteAsync(prompt);

        Assert.Null(answer);
        var warning = Assert.Single(_logger.Warnings);
        Assert.Contains("mistral-small did not answer within 120 s", warning.Message);
        Assert.Contains("400 line(s), 4399 characters, about 1100 tokens", warning.Message);
    }

    [Fact]
    public async Task The_prompt_itself_is_not_written_to_the_log()
    {
        await Client(TimingOut()).AskAsync(Prompt);

        Assert.DoesNotContain("line two", Assert.Single(_logger.Warnings).Message);
    }

    [Fact]
    public async Task A_model_that_cannot_be_reached_is_not_reported_as_a_timeout()
    {
        var answer = await Client(StubHttpMessageHandler.Unreachable()).AskAsync(Prompt);

        Assert.Null(answer);
        var warning = Assert.Single(_logger.Warnings);
        Assert.Contains("Could not reach Ollama", warning.Message);
        Assert.DoesNotContain("did not answer within", warning.Message);
    }

    [Fact]
    public async Task A_shutdown_during_a_question_is_not_mistaken_for_a_timeout()
    {
        using var stopping = new CancellationTokenSource();
        await stopping.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Client(TimingOut()).AskAsync(Prompt, stopping.Token));

        Assert.Empty(_logger.Warnings);
    }
}
