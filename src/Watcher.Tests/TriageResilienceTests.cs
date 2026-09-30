using Watcher.Model;
using Watcher.Tests.Fakes;

namespace Watcher.Tests;

/// <summary>The budget, the privacy guarantee, and what happens when the model misbehaves.</summary>
public class TriageResilienceTests
{
    [Fact]
    public async Task The_budget_caps_how_many_errors_are_judged_in_one_cycle()
    {
        var harness = TriageHarness.Build(TriageHarness.DistinctErrors(5), maxJudgements: 2);

        var result = await harness.Service.RunOnceAsync();

        Assert.Equal(5, result.Fingerprints);
        Assert.Equal(2, result.Judged);
        Assert.Equal(3, result.Deferred);
        Assert.Equal(2, harness.Ollama.CallCount);
    }

    [Fact]
    public async Task An_error_deferred_by_the_budget_is_judged_on_the_next_cycle()
    {
        // A deferred error must not be marked as seen, or it would never be judged at all.
        var harness = TriageHarness.Build(TriageHarness.DistinctErrors(4), maxJudgements: 2);

        await harness.Service.RunOnceAsync();
        var second = await harness.Service.RunOnceAsync();

        Assert.Equal(2, second.Judged);
        Assert.Equal(4, harness.Ollama.CallCount);
        Assert.Equal(4, harness.Sink.Records.Count);
    }

    [Fact]
    public async Task A_rule_filtered_error_never_spends_the_budget()
    {
        var harness = TriageHarness.Build(
            TriageHarness.DistinctErrors(3),
            rulesJson: """{ "ignore": [ { "id": "all-of-them", "messageContains": "failed to start" } ] }""",
            maxJudgements: 1);

        var result = await harness.Service.RunOnceAsync();

        Assert.Equal(3, result.FilteredByRules);
        Assert.Equal(0, result.Deferred);
        Assert.Equal(0, harness.Ollama.CallCount);
    }

    [Fact]
    public async Task An_unusable_answer_is_recorded_and_the_cycle_finishes()
    {
        var harness = TriageHarness.Build(
            [TestEvents.Error(id: "a"), TestEvents.MessageOnly("Something else broke", id: "b")]);
        harness.Ollama.Answer = null;

        var result = await harness.Service.RunOnceAsync();

        Assert.Equal(2, result.Judged);
        Assert.Equal(2, result.JudgementFailures);
        Assert.Equal(2, harness.Sink.Records.Count);
        Assert.All(harness.Sink.Records, r => Assert.Equal(TriageOutcome.JudgementFailed, r.Outcome));
    }

    [Fact]
    public async Task No_personal_data_reaches_the_model()
    {
        var harness = TriageHarness.Build(
        [
            TestEvents.Error(
                template: "",
                rendered: "Could not save order for anna.svensson@example.com, name=Anna Svensson",
                exception: "System.Exception: lookup of 900101-1234 failed\n   at Contoso.Orders.Lookup.Find()",
                properties: new Dictionary<string, string?>
                {
                    ["firstName"] = "Anna",
                    ["Email"] = "anna.svensson@example.com"
                })
        ]);

        await harness.Service.RunOnceAsync();

        var report = Assert.Single(harness.Ollama.Reports);
        Assert.DoesNotContain("anna.svensson@example.com", report);
        Assert.DoesNotContain("Anna", report);
        Assert.DoesNotContain("Svensson", report);
        Assert.DoesNotContain("900101-1234", report);
    }

    [Fact]
    public async Task No_personal_data_reaches_the_output()
    {
        var harness = TriageHarness.Build(
        [
            TestEvents.Error(
                template: "",
                rendered: "Failed for anna@example.com with id 900101-1234")
        ]);

        await harness.Service.RunOnceAsync();

        var record = Assert.Single(harness.Sink.Records);
        Assert.DoesNotContain("anna@example.com", record.Template);
        Assert.DoesNotContain("900101-1234", record.Template);
    }

    [Fact]
    public async Task The_known_errors_document_is_sent_with_every_judgement()
    {
        var harness = TriageHarness.Build(
            [TestEvents.Error()],
            knownErrors: "## Deadlock on save\nRestart the sync job.");

        await harness.Service.RunOnceAsync();

        Assert.Contains("Restart the sync job.", Assert.Single(harness.Ollama.KnownErrors));
    }

    [Fact]
    public async Task A_known_verdict_carries_the_solution_to_the_output()
    {
        var harness = TriageHarness.Build(
            [TestEvents.Error()],
            answer: new TriageVerdict(Verdict.Known, "matches the deadlock entry", "Restart the sync job"));

        await harness.Service.RunOnceAsync();

        var record = Assert.Single(harness.Sink.Records);
        Assert.Equal(Verdict.Known, record.Verdict);
        Assert.Equal("Restart the sync job", record.KnownSolution);
    }
}
