using CLog.Model;
using CLog.Tests.Fakes;

namespace CLog.Tests;

public class TriageServiceTests
{
    [Fact]
    public async Task A_rule_filters_an_error_before_the_model_is_asked()
    {
        var harness = TriageHarness.Build(
            [TestEvents.Error(template: "", rendered: "Order 4711 could not be saved")],
            rulesJson:
            """
            { "ignore": [ {
                "id": "known-noise",
                "reason": "the retry handles this",
                "exceptionType": "System.InvalidOperationException"
            } ] }
            """);

        var result = await harness.Service.RunOnceAsync();

        Assert.Equal(0, harness.Ollama.CallCount);
        Assert.Equal(1, result.FilteredByRules);
        Assert.Equal(0, result.Judged);

        var record = Assert.Single(harness.Sink.Records);
        Assert.Equal(TriageOutcome.FilteredByRule, record.Outcome);
        Assert.Equal("known-noise", record.RuleId);
    }

    [Fact]
    public async Task An_error_no_rule_covers_reaches_the_model()
    {
        var harness = TriageHarness.Build([TestEvents.Error()]);

        var result = await harness.Service.RunOnceAsync();

        Assert.Equal(1, harness.Ollama.CallCount);
        Assert.Equal(1, result.Judged);

        var record = Assert.Single(harness.Sink.Records);
        Assert.Equal(TriageOutcome.Judged, record.Outcome);
        Assert.Equal(Verdict.Analyze, record.Verdict);
    }

    [Fact]
    public async Task Only_the_unfiltered_errors_reach_the_model()
    {
        var ignored = TestEvents.Error(
            id: "ignored",
            template: "",
            rendered: "Health probe timed out",
            exception: "System.TimeoutException: probe timed out\n   at Contoso.Health.Probe.Check()");

        var harness = TriageHarness.Build(
            [ignored, TestEvents.Error(id: "kept")],
            rulesJson: """{ "ignore": [ { "id": "probes", "exceptionType": "System.TimeoutException" } ] }""");

        var result = await harness.Service.RunOnceAsync();

        Assert.Equal(1, result.FilteredByRules);
        Assert.Equal(1, harness.Ollama.CallCount);
        Assert.Contains("InvalidOperationException", Assert.Single(harness.Ollama.Reports));
    }

    [Fact]
    public async Task A_filtered_error_is_reported_once_not_every_cycle()
    {
        var harness = TriageHarness.Build(
            [TestEvents.Error()],
            rulesJson: """{ "ignore": [ { "id": "quiet", "exceptionType": "System.InvalidOperationException" } ] }""");

        await harness.Service.RunOnceAsync();
        await harness.Service.RunOnceAsync();

        Assert.Single(harness.Sink.Records);
    }

    [Fact]
    public async Task Events_sharing_a_fingerprint_are_judged_once_and_counted()
    {
        var events = Enumerable.Range(1, 5)
            .Select(i => TestEvents.Error(
                id: $"event-{i}",
                template: "Order {OrderId} could not be saved",
                rendered: $"Order {i} could not be saved"))
            .ToList();

        var harness = TriageHarness.Build(events);
        var result = await harness.Service.RunOnceAsync();

        Assert.Equal(5, result.EventsFetched);
        Assert.Equal(1, result.Fingerprints);
        Assert.Equal(1, harness.Ollama.CallCount);
        Assert.Equal(5, Assert.Single(harness.Sink.Records).OccurrenceCount);
    }

    [Fact]
    public async Task An_error_already_seen_is_not_judged_again()
    {
        var harness = TriageHarness.Build([TestEvents.Error()]);

        await harness.Service.RunOnceAsync();
        var second = await harness.Service.RunOnceAsync();

        Assert.Equal(1, harness.Ollama.CallCount);
        Assert.Equal(0, second.Judged);
        Assert.Single(harness.Sink.Records);
    }

    [Fact]
    public async Task An_empty_batch_does_nothing()
    {
        var harness = TriageHarness.Build([]);

        var result = await harness.Service.RunOnceAsync();

        Assert.Equal(0, result.EventsFetched);
        Assert.Equal(0, harness.Ollama.CallCount);
        Assert.Empty(harness.Sink.Records);
    }
}
