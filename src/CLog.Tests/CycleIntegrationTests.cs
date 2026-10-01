using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using CLog.Configuration;
using CLog.Knowledge;
using CLog.Ollama;
using CLog.Output;
using CLog.Seq;
using CLog.Storage;
using CLog.Tests.Fakes;
using CLog.Triage;

namespace CLog.Tests;

/// <summary>
/// One whole cycle with the real Seq client, the real Ollama client, the real SQLite store and
/// the real output sink — only the two HTTP boundaries are stubbed. The unit tests each cover a
/// part; this one covers them composing.
/// </summary>
public sealed class CycleIntegrationTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "c-log-tests", Guid.NewGuid().ToString("N"));

    private const string SeqBody =
        """
        [
          {
            "Id": "event-1",
            "Timestamp": "2026-09-30T08:00:00.0000000Z",
            "Level": "Error",
            "RenderedMessage": "Could not save order 4711 for anna@example.com",
            "MessageTemplateTokens": [
              { "Text": "Could not save order " },
              { "PropertyName": "OrderId", "RawText": "{OrderId}" },
              { "Text": " for " },
              { "PropertyName": "Email", "RawText": "{Email}" }
            ],
            "Exception": "System.InvalidOperationException: deadlock\n   at System.Data.Run()\n   at Contoso.Orders.OrderRepository.Save(Order order)",
            "Properties": [
              { "Name": "firstName", "Value": "Anna" },
              { "Name": "PersonalId", "Value": "900101-1234" },
              { "Name": "OrderId", "Value": 4711 }
            ]
          }
        ]
        """;

    private const string OllamaBody =
        """
        { "message": { "role": "assistant",
          "content": "{\"verdict\": \"ANALYZE\", \"reason\": \"a deadlock on save is a real defect\"}" } }
        """;

    private (TriageService Service, string LogPath) Build(
        StubHttpMessageHandler seqHandler,
        StubHttpMessageHandler ollamaHandler,
        string rulesJson = """{ "ignore": [] }""")
    {
        var logPath = Path.Combine(_directory, "triage.jsonl");
        var options = Options.Create(new CLogOptions
        {
            Seq = new SeqOptions { ApiKey = "test-key" },
            Ollama = new OllamaOptions { Model = "mistral" },
            // These tests stub Ollama with a JSON verdict, which is the SingleCall protocol.
            Triage = new TriageOptions { Mode = TriageMode.SingleCall },
            MaxJudgementsPerRun = 10,
            LookbackMinutes = 10
        });

        var store = new SqliteFingerprintStore(
            Path.Combine(_directory, "clog.db"), new TestLogger<SqliteFingerprintStore>());
        store.InitializeAsync().GetAwaiter().GetResult();

        var service = new TriageService(
            new SeqClient(new HttpClient(seqHandler), options, new TestLogger<SeqClient>()),
            store,
            new OllamaClient(new HttpClient(ollamaHandler), options, new TestLogger<OllamaClient>()),
            new ConsoleAndFileTriageSink(logPath, new TestLogger<ConsoleAndFileTriageSink>()),
            new StaticKnowledgeSource("## Deadlock on save\nRestart the sync job."),
            new StaticRuleSetProvider(RuleSet.Parse(rulesJson)),
            options,
            new TestLogger<TriageService>());

        return (service, logPath);
    }

    [Fact]
    public async Task An_error_travels_from_seq_through_the_model_to_the_output()
    {
        var ollama = new StubHttpMessageHandler(HttpStatusCode.OK, OllamaBody);
        var (service, logPath) = Build(new StubHttpMessageHandler(HttpStatusCode.OK, SeqBody), ollama);

        var result = await service.RunOnceAsync();

        Assert.Equal(1, result.EventsFetched);
        Assert.Equal(1, result.Judged);
        Assert.Equal(0, result.JudgementFailures);

        var line = Assert.Single(await File.ReadAllLinesAsync(logPath));
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;

        Assert.Equal("Analyze", root.GetProperty("Verdict").GetString());
        Assert.Equal("System.InvalidOperationException", root.GetProperty("ExceptionType").GetString());
        Assert.Equal(
            "Contoso.Orders.OrderRepository.Save(Order order)",
            root.GetProperty("TopFrame").GetString());
        Assert.Contains("real defect", root.GetProperty("Reason").GetString());
    }

    [Fact]
    public async Task No_personal_data_leaves_the_process_or_lands_in_the_output()
    {
        var ollama = new StubHttpMessageHandler(HttpStatusCode.OK, OllamaBody);
        var (service, logPath) = Build(new StubHttpMessageHandler(HttpStatusCode.OK, SeqBody), ollama);

        await service.RunOnceAsync();

        var sentToOllama = Assert.Single(ollama.RequestBodies);
        var written = await File.ReadAllTextAsync(logPath);

        foreach (var secret in new[] { "anna@example.com", "Anna", "900101-1234" })
        {
            Assert.DoesNotContain(secret, sentToOllama);
            Assert.DoesNotContain(secret, written);
        }
    }

    [Fact]
    public async Task A_rule_stops_the_error_before_any_request_reaches_ollama()
    {
        var ollama = new StubHttpMessageHandler(HttpStatusCode.OK, OllamaBody);
        var (service, _) = Build(
            new StubHttpMessageHandler(HttpStatusCode.OK, SeqBody),
            ollama,
            rulesJson: """{ "ignore": [ { "id": "deadlocks", "topFrameContains": "OrderRepository" } ] }""");

        var result = await service.RunOnceAsync();

        Assert.Equal(1, result.FilteredByRules);
        Assert.Equal(0, ollama.CallCount);
    }

    [Fact]
    public async Task The_same_error_is_judged_once_across_cycles()
    {
        var ollama = new StubHttpMessageHandler(HttpStatusCode.OK, OllamaBody);
        var (service, logPath) = Build(new StubHttpMessageHandler(HttpStatusCode.OK, SeqBody), ollama);

        await service.RunOnceAsync();
        await service.RunOnceAsync();

        Assert.Equal(1, ollama.CallCount);
        Assert.Single(await File.ReadAllLinesAsync(logPath));
    }

    [Fact]
    public async Task An_unusable_answer_from_ollama_still_produces_a_record()
    {
        var ollama = new StubHttpMessageHandler(
            HttpStatusCode.OK, """{ "message": { "content": "I am not sure about this one." } }""");
        var (service, logPath) = Build(new StubHttpMessageHandler(HttpStatusCode.OK, SeqBody), ollama);

        var result = await service.RunOnceAsync();

        Assert.Equal(1, result.JudgementFailures);
        var line = Assert.Single(await File.ReadAllLinesAsync(logPath));
        Assert.Contains("JudgementFailed", line);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // A locked file in the temp directory is not worth failing a test over.
        }
    }
}
