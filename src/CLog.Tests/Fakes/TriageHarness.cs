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
    InMemoryFingerprintStore Store,
    FakeSeqClient Seq)
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
        Func<string, string?>? reply = null,
        TimeProvider? time = null)
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

        var seq = new FakeSeqClient([.. events]);
        var service = new TriageService(
            seq,
            store,
            ollama,
            sink,
            new StaticKnowledgeSource(knownErrors),
            new StaticRuleSetProvider(RuleSet.Parse(rulesJson)),
            options,
            new TestLogger<TriageService>(),
            time);

        return new TriageHarness(service, ollama, sink, store, seq);
    }

    /// <summary>Distinct errors, each with its own fingerprint.</summary>
    public static List<SeqEvent> DistinctErrors(int count) =>
        [.. Enumerable.Range(1, count).Select(i =>
            TestEvents.MessageOnly($"Subsystem {(char)('a' + i)} failed to start", id: $"event-{i}"))];
}
