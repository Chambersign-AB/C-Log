using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using CLog.Analysis;
using CLog.Configuration;
using CLog.Ollama;
using CLog.Tests.Fakes;

namespace CLog.Tests;

/// <summary>Every request to Ollama carries the temperature and seed, so a judgement is repeatable.</summary>
public class OllamaClientSamplingTests
{
    private const string Answer = """{ "message": { "role": "assistant", "content": "{\"verdict\": \"NOISE\"}" } }""";

    private readonly StubHttpMessageHandler _handler = new(HttpStatusCode.OK, Answer);

    private OllamaClient Client(OllamaOptions? ollama = null) => new(
        new HttpClient(_handler),
        Options.Create(new CLogOptions { Ollama = ollama ?? new OllamaOptions() }),
        new TestLogger<OllamaClient>());

    private JsonElement SentOptions()
    {
        using var request = JsonDocument.Parse(Assert.Single(_handler.RequestBodies));
        return request.RootElement.GetProperty("options").Clone();
    }

    [Fact]
    public async Task A_yes_no_question_is_asked_at_temperature_zero_with_seed_42_by_default()
    {
        await Client().AskAsync("the question");

        Assert.Equal(0, SentOptions().GetProperty("temperature").GetDouble());
        Assert.Equal(42, SentOptions().GetProperty("seed").GetInt32());
    }

    [Fact]
    public async Task The_three_way_judgement_is_asked_with_the_same_settings()
    {
        await Client().JudgeAsync("some error", "known errors");

        Assert.Equal(0, SentOptions().GetProperty("temperature").GetDouble());
        Assert.Equal(42, SentOptions().GetProperty("seed").GetInt32());
    }

    [Fact]
    public async Task The_analysis_is_asked_with_the_same_settings()
    {
        var options = Options.Create(new CLogOptions { Analysis = new AnalysisOptions { Model = "mistral-small" } });
        var model = new OllamaAnalysisModel(
            new OllamaClient(new HttpClient(_handler), options, new TestLogger<OllamaClient>()), options);

        await model.CompleteAsync("the analysis prompt");

        Assert.Equal(0, SentOptions().GetProperty("temperature").GetDouble());
        Assert.Equal(42, SentOptions().GetProperty("seed").GetInt32());
    }

    [Fact]
    public async Task Temperature_and_seed_come_from_configuration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CLog:Ollama:Temperature"] = "0.7",
                ["CLog:Ollama:Seed"] = "7"
            })
            .Build();
        var configured = configuration.GetSection(CLogOptions.SectionName).Get<CLogOptions>()!.Ollama;

        await Client(configured).AskAsync("the question");

        Assert.Equal(0.7, SentOptions().GetProperty("temperature").GetDouble());
        Assert.Equal(7, SentOptions().GetProperty("seed").GetInt32());
    }

    [Fact]
    public void The_shipped_settings_ask_for_temperature_zero_and_seed_42()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "C-Log.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var shipped = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(directory.FullName, "src", "CLog", "appsettings.json"))
            .Build()
            .GetSection(CLogOptions.SectionName)
            .Get<CLogOptions>()!
            .Ollama;

        Assert.Equal(0, shipped.Temperature);
        Assert.Equal(42, shipped.Seed);
    }
}
