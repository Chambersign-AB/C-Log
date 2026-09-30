using System.Net;
using Microsoft.Extensions.Options;
using Watcher.Configuration;
using Watcher.Model;
using Watcher.Ollama;
using Watcher.Tests.Fakes;

namespace Watcher.Tests;

public class OllamaClientTests
{
    private static (OllamaClient Client, TestLogger<OllamaClient> Logger) Build(StubHttpMessageHandler handler)
    {
        var logger = new TestLogger<OllamaClient>();
        var options = Options.Create(new WatcherOptions
        {
            Ollama = new OllamaOptions { Url = "http://localhost:11434", Model = "mistral" }
        });

        return (new OllamaClient(new HttpClient(handler), options, logger), logger);
    }

    private static string ChatResponse(string content) =>
        System.Text.Json.JsonSerializer.Serialize(new { message = new { role = "assistant", content } });

    [Fact]
    public async Task A_well_formed_answer_becomes_a_verdict()
    {
        var body = ChatResponse("""{"verdict": "ANALYZE", "reason": "looks like a real bug"}""");
        var (client, _) = Build(new StubHttpMessageHandler(HttpStatusCode.OK, body));

        var verdict = await client.JudgeAsync("some error", "known errors");

        Assert.NotNull(verdict);
        Assert.Equal(Verdict.Analyze, verdict.Verdict);
    }

    [Fact]
    public async Task Invalid_json_from_ollama_is_logged_and_does_not_throw()
    {
        var body = ChatResponse("Sorry, I could not decide.");
        var (client, logger) = Build(new StubHttpMessageHandler(HttpStatusCode.OK, body));

        var verdict = await client.JudgeAsync("some error", "known errors");

        Assert.Null(verdict);
        Assert.Contains(logger.Warnings, w => w.Message.Contains("Unusable answer"));
    }

    [Fact]
    public async Task A_truncated_answer_is_logged_and_does_not_throw()
    {
        var body = ChatResponse("""{"verdict": "ANAL""");
        var (client, logger) = Build(new StubHttpMessageHandler(HttpStatusCode.OK, body));

        Assert.Null(await client.JudgeAsync("some error", ""));
        Assert.NotEmpty(logger.Warnings);
    }

    [Fact]
    public async Task A_malformed_envelope_is_logged_and_does_not_throw()
    {
        var (client, logger) = Build(new StubHttpMessageHandler(HttpStatusCode.OK, "this is not json at all"));

        Assert.Null(await client.JudgeAsync("some error", ""));
        Assert.NotEmpty(logger.Warnings);
    }

    [Fact]
    public async Task An_error_status_is_logged_and_does_not_throw()
    {
        var (client, logger) = Build(new StubHttpMessageHandler(HttpStatusCode.InternalServerError, "boom"));

        Assert.Null(await client.JudgeAsync("some error", ""));
        Assert.NotEmpty(logger.Warnings);
    }

    [Fact]
    public async Task An_unreachable_ollama_is_logged_and_does_not_throw()
    {
        var (client, logger) = Build(StubHttpMessageHandler.Unreachable());

        Assert.Null(await client.JudgeAsync("some error", ""));
        Assert.Contains(logger.Warnings, w => w.Message.Contains("Could not reach Ollama"));
    }

    [Fact]
    public async Task The_request_names_the_configured_model_and_asks_for_json()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, ChatResponse("""{"verdict":"NOISE"}"""));
        var (client, _) = Build(handler);

        await client.JudgeAsync("some error", "known errors");

        var request = Assert.Single(handler.RequestBodies);
        Assert.Contains("\"model\":\"mistral\"", request);
        Assert.Contains("\"format\":\"json\"", request);
        Assert.Contains("\"stream\":false", request);
        Assert.Equal("http://localhost:11434/api/chat", handler.LastRequestUri?.ToString());
    }

    [Fact]
    public void The_prompt_carries_both_the_error_and_the_known_errors()
    {
        var prompt = OllamaClient.BuildUserPrompt("the error report", "the known errors");

        Assert.Contains("the error report", prompt);
        Assert.Contains("the known errors", prompt);
    }
}
