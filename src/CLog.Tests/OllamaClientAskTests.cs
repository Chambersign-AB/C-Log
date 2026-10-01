using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using CLog.Configuration;
using CLog.Ollama;
using CLog.Tests.Fakes;

namespace CLog.Tests;

/// <summary>The plain-text question path of OllamaClient, used by the two-step judgement.</summary>
public class OllamaClientAskTests
{
    private static (OllamaClient Client, TestLogger<OllamaClient> Logger) Build(
        StubHttpMessageHandler handler,
        string model = "mistral")
    {
        var logger = new TestLogger<OllamaClient>();
        var options = Options.Create(new CLogOptions
        {
            Ollama = new OllamaOptions { Url = "http://localhost:11434", Model = model }
        });

        return (new OllamaClient(new HttpClient(handler), options, logger), logger);
    }

    private static string ChatResponse(string? content) =>
        JsonSerializer.Serialize(new { message = new { role = "assistant", content } });

    [Fact]
    public async Task The_answer_is_returned_as_the_model_wrote_it()
    {
        var (client, _) = Build(new StubHttpMessageHandler(HttpStatusCode.OK, ChatResponse(" Ja.\n")));

        Assert.Equal(" Ja.\n", await client.AskAsync("Är detta ett känt fel?"));
    }

    [Fact]
    public async Task The_request_carries_the_prompt_and_the_configured_model_and_does_not_ask_for_json()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, ChatResponse("Nej"));
        var (client, _) = Build(handler, model: "mistral-small");

        await client.AskAsync("the question");

        using var request = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
        var root = request.RootElement;

        Assert.Equal("mistral-small", root.GetProperty("model").GetString());
        Assert.False(root.GetProperty("stream").GetBoolean());
        Assert.False(root.TryGetProperty("format", out _));

        var message = Assert.Single(root.GetProperty("messages").EnumerateArray());
        Assert.Equal("user", message.GetProperty("role").GetString());
        Assert.Equal("the question", message.GetProperty("content").GetString());
    }

    [Fact]
    public async Task An_empty_answer_is_an_answer_not_an_outage()
    {
        var (client, _) = Build(new StubHttpMessageHandler(HttpStatusCode.OK, ChatResponse(null)));

        Assert.Equal("", await client.AskAsync("the question"));
    }

    [Fact]
    public async Task An_unreachable_ollama_is_logged_and_gives_no_answer()
    {
        var (client, logger) = Build(StubHttpMessageHandler.Unreachable());

        Assert.Null(await client.AskAsync("the question"));
        Assert.Contains(logger.Warnings, w => w.Message.Contains("Could not reach Ollama"));
    }

    [Fact]
    public async Task An_error_status_is_logged_and_gives_no_answer()
    {
        var (client, logger) = Build(new StubHttpMessageHandler(HttpStatusCode.InternalServerError, "boom"));

        Assert.Null(await client.AskAsync("the question"));
        Assert.NotEmpty(logger.Warnings);
    }
}
