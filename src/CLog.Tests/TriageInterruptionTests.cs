using Microsoft.Extensions.Options;
using CLog.Configuration;
using CLog.Knowledge;
using CLog.Model;
using CLog.Output;
using CLog.Tests.Fakes;
using CLog.Triage;

namespace CLog.Tests;

/// <summary>
/// A fingerprint is marked seen only after its outcome is written, so an error whose
/// judgement is cut short — a restart, a shutdown, a fault — is still new on the next cycle.
/// </summary>
public class TriageInterruptionTests
{
    /// <summary>Notes, at the moment a record is written, whether its fingerprint was already marked seen.</summary>
    private sealed class ProbingSink(InMemoryFingerprintStore store) : ITriageSink
    {
        public List<(TriageOutcome Outcome, bool AlreadySeen)> Writes { get; } = [];

        public Task WriteAsync(TriageRecord record, CancellationToken cancellationToken = default)
        {
            Writes.Add((record.Outcome, store.Seen.ContainsKey(record.Fingerprint)));
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task A_judgement_cut_short_by_a_fault_leaves_the_error_new_for_the_next_cycle()
    {
        var questions = 0;
        var harness = TriageHarness.Build(
            TriageHarness.DistinctErrors(3),
            mode: TriageMode.TwoStep,
            reply: _ => ++questions == 2 ? throw new InvalidOperationException("the model fell over") : "Nej");

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Service.RunOnceAsync());

        // The first error has its outcome. The second was with the model, the third never reached.
        Assert.Single(harness.Sink.Records);
        Assert.Single(harness.Store.Seen);

        var second = await harness.Service.RunOnceAsync();

        Assert.Equal(2, second.Judged);
        Assert.Equal(3, harness.Sink.Records.Count);
        Assert.Equal(3, harness.Sink.Records.Select(r => r.Fingerprint).Distinct().Count());
    }

    [Fact]
    public async Task A_judgement_cut_short_by_shutdown_leaves_the_error_new_for_the_next_start()
    {
        using var shutdown = new CancellationTokenSource();
        var interrupted = false;
        var harness = TriageHarness.Build(
            [TestEvents.Error()],
            mode: TriageMode.TwoStep,
            reply: _ =>
            {
                if (interrupted)
                {
                    return "Nej";
                }

                interrupted = true;
                shutdown.Cancel();
                throw new OperationCanceledException(shutdown.Token);
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.Service.RunOnceAsync(shutdown.Token));

        Assert.Empty(harness.Store.Seen);
        Assert.Empty(harness.Sink.Records);

        var afterRestart = await harness.Service.RunOnceAsync();

        Assert.Equal(1, afterRestart.Judged);
        Assert.Equal(Verdict.Analyze, Assert.Single(harness.Sink.Records).Verdict);
    }

    [Fact]
    public async Task No_fingerprint_is_marked_seen_before_its_outcome_is_written()
    {
        var store = new InMemoryFingerprintStore();
        var sink = new ProbingSink(store);
        var filtered = TestEvents.Error(
            id: "filtered",
            template: "",
            rendered: "Health probe timed out",
            exception: "System.TimeoutException: probe timed out\n   at Contoso.Health.Probe.Check()");

        var service = new TriageService(
            new FakeSeqClient(filtered, TestEvents.Error(id: "judged"), TestEvents.MessageOnly("Unreachable model", id: "failed")),
            store,
            new RecordingOllamaClient
            {
                Reply = prompt => prompt.Contains("Unreachable model", StringComparison.Ordinal) ? null : "Nej"
            },
            sink,
            new StaticKnowledgeSource(""),
            new StaticRuleSetProvider(RuleSet.Parse(
                """{ "ignore": [ { "id": "probes", "exceptionType": "System.TimeoutException" } ] }""")),
            Options.Create(new CLogOptions()),
            new TestLogger<TriageService>());

        await service.RunOnceAsync();

        Assert.Equal(
            new[] { TriageOutcome.FilteredByRule, TriageOutcome.Judged, TriageOutcome.JudgementFailed },
            sink.Writes.Select(w => w.Outcome));
        Assert.All(sink.Writes, w => Assert.False(w.AlreadySeen));
        Assert.Equal(3, store.Seen.Count);
    }

    [Fact]
    public async Task A_failed_judgement_is_an_outcome_and_is_not_retried_every_cycle()
    {
        var harness = TriageHarness.Build([TestEvents.Error()], mode: TriageMode.TwoStep, reply: _ => null);

        await harness.Service.RunOnceAsync();
        var second = await harness.Service.RunOnceAsync();

        Assert.Equal(0, second.Judged);
        Assert.Equal(TriageOutcome.JudgementFailed, Assert.Single(harness.Sink.Records).Outcome);
        Assert.Single(harness.Ollama.Questions);
    }

    [Fact]
    public async Task An_error_that_already_has_an_outcome_is_still_counted_each_cycle()
    {
        var harness = TriageHarness.Build([TestEvents.Error()]);

        await harness.Service.RunOnceAsync();
        await harness.Service.RunOnceAsync();
        await harness.Service.RunOnceAsync();

        Assert.Equal(3, Assert.Single(harness.Store.Seen).Value);
        Assert.Single(harness.Sink.Records);
    }
}
