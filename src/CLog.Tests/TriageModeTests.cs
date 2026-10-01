using CLog.Configuration;
using CLog.Model;
using CLog.Tests.Fakes;

namespace CLog.Tests;

/// <summary>TriageService choosing between the two-question judgement and the original single call.</summary>
public class TriageModeTests
{
    private const string KnownErrors =
        """
        ## Deadlock when saving an order
        **Solution** Restart the sync job.
        """;

    [Fact]
    public void Two_step_is_the_default_mode()
    {
        Assert.Equal(TriageMode.TwoStep, new CLogOptions().Triage.Mode);
    }

    [Fact]
    public async Task In_two_step_mode_the_verdict_comes_from_yes_no_questions()
    {
        var harness = TriageHarness.Build(
            [TestEvents.Error()],
            knownErrors: KnownErrors,
            mode: TriageMode.TwoStep,
            reply: _ => "Ja");

        var result = await harness.Service.RunOnceAsync();

        Assert.Equal(1, result.Judged);
        Assert.Equal(0, result.JudgementFailures);
        Assert.Equal(0, harness.Ollama.CallCount);
        Assert.Single(harness.Ollama.Questions);

        var record = Assert.Single(harness.Sink.Records);
        Assert.Equal(TriageOutcome.Judged, record.Outcome);
        Assert.Equal(Verdict.Known, record.Verdict);
        Assert.Equal("Restart the sync job.", record.KnownSolution);
    }

    [Fact]
    public async Task In_two_step_mode_an_unreadable_answer_is_recorded_as_ANALYZE_not_as_a_failure()
    {
        var harness = TriageHarness.Build(
            [TestEvents.Error()],
            knownErrors: KnownErrors,
            mode: TriageMode.TwoStep,
            reply: _ => "I am not sure about this one.");

        var result = await harness.Service.RunOnceAsync();

        Assert.Equal(0, result.JudgementFailures);
        var record = Assert.Single(harness.Sink.Records);
        Assert.Equal(TriageOutcome.Judged, record.Outcome);
        Assert.Equal(Verdict.Analyze, record.Verdict);
    }

    [Fact]
    public async Task In_two_step_mode_an_unreachable_model_is_a_failed_judgement()
    {
        var harness = TriageHarness.Build([TestEvents.Error()], mode: TriageMode.TwoStep, reply: _ => null);

        var result = await harness.Service.RunOnceAsync();

        Assert.Equal(1, result.JudgementFailures);
        Assert.Equal(TriageOutcome.JudgementFailed, Assert.Single(harness.Sink.Records).Outcome);
    }

    [Fact]
    public async Task In_two_step_mode_the_budget_counts_errors_not_questions()
    {
        // Each error costs two questions here; a budget of two must still cover two errors.
        var harness = TriageHarness.Build(
            TriageHarness.DistinctErrors(3),
            knownErrors: KnownErrors,
            maxJudgements: 2,
            mode: TriageMode.TwoStep);

        var result = await harness.Service.RunOnceAsync();

        Assert.Equal(2, result.Judged);
        Assert.Equal(1, result.Deferred);
        Assert.Equal(4, harness.Ollama.Questions.Count);
    }

    [Fact]
    public async Task In_single_call_mode_the_original_three_way_prompt_is_used()
    {
        var harness = TriageHarness.Build(
            [TestEvents.Error()],
            answer: new TriageVerdict(Verdict.Noise, "a cancelled request", null),
            knownErrors: KnownErrors,
            mode: TriageMode.SingleCall);

        await harness.Service.RunOnceAsync();

        Assert.Equal(1, harness.Ollama.CallCount);
        Assert.Empty(harness.Ollama.Questions);
        Assert.Equal(Verdict.Noise, Assert.Single(harness.Sink.Records).Verdict);
    }
}
