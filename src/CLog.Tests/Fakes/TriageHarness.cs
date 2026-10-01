using Microsoft.Extensions.Options;
using CLog.Configuration;
using CLog.Knowledge;
using CLog.Model;
using CLog.Triage;

namespace CLog.Tests.Fakes;

/// <summary>A TriageService wired entirely to fakes, so a cycle runs with no Seq and no Ollama.</summary>
public sealed record TriageHarness(
    TriageService Service,
    RecordingOllamaClient Ollama,
    RecordingTriageSink Sink,
    InMemoryFingerprintStore Store)
{
    public static TriageHarness Build(
        IEnumerable<SeqEvent> events,
        string rulesJson = """{ "ignore": [] }""",
        TriageVerdict? answer = null,
        int maxJudgements = 10,
        string knownErrors = "# Known errors",
        // SingleCall unless a test asks otherwise: the cycle tests script the model with a
        // ready-made verdict, which is what that mode consumes. The product default is TwoStep.
        TriageMode mode = TriageMode.SingleCall,
        Func<string, string?>? reply = null)
    {
        var ollama = new RecordingOllamaClient(answer ?? new TriageVerdict(Verdict.Analyze, "a real bug", null));
        if (reply is not null)
        {
            ollama.Reply = reply;
        }

        var sink = new RecordingTriageSink();
        var store = new InMemoryFingerprintStore();

        var options = Options.Create(new CLogOptions
        {
            MaxJudgementsPerRun = maxJudgements,
            LookbackMinutes = 10,
            Triage = new TriageOptions { Mode = mode }
        });

        var service = new TriageService(
            new FakeSeqClient([.. events]),
            store,
            ollama,
            sink,
            new StaticKnowledgeSource(knownErrors),
            new StaticRuleSetProvider(RuleSet.Parse(rulesJson)),
            options,
            new TestLogger<TriageService>());

        return new TriageHarness(service, ollama, sink, store);
    }

    /// <summary>Distinct errors, each with its own fingerprint.</summary>
    public static List<SeqEvent> DistinctErrors(int count) =>
        [.. Enumerable.Range(1, count).Select(i =>
            TestEvents.MessageOnly($"Subsystem {(char)('a' + i)} failed to start", id: $"event-{i}"))];
}
