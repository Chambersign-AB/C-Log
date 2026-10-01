using CLog.Model;
using CLog.Tests.Fakes;

namespace CLog.Tests;

/// <summary>
/// A result that could not be written is not an outcome: the error is left unmarked, so it
/// is reported again instead of being remembered as handled.
/// </summary>
public class TriageUnwrittenResultTests
{
    [Fact]
    public async Task A_judged_error_whose_result_could_not_be_written_is_reported_again()
    {
        var harness = TriageHarness.Build([TestEvents.Error()]);
        harness.Sink.FailNextWrites = 1;

        var first = await harness.Service.RunOnceAsync();

        Assert.Equal(1, first.Unreported);
        Assert.Empty(harness.Store.Seen);
        Assert.Empty(harness.Sink.Records);

        var second = await harness.Service.RunOnceAsync();

        Assert.Equal(0, second.Unreported);
        Assert.Equal(1, second.Judged);
        Assert.Equal(TriageOutcome.Judged, Assert.Single(harness.Sink.Records).Outcome);
        Assert.Single(harness.Store.Seen);
    }

    [Fact]
    public async Task A_filtered_error_whose_result_could_not_be_written_is_reported_again()
    {
        var harness = TriageHarness.Build(
            [TestEvents.Error()],
            rulesJson: """{ "ignore": [ { "id": "quiet", "exceptionType": "System.InvalidOperationException" } ] }""");
        harness.Sink.FailNextWrites = 1;

        var first = await harness.Service.RunOnceAsync();

        Assert.Equal(1, first.Unreported);
        Assert.Empty(harness.Store.Seen);

        await harness.Service.RunOnceAsync();

        Assert.Equal(TriageOutcome.FilteredByRule, Assert.Single(harness.Sink.Records).Outcome);
        Assert.Single(harness.Store.Seen);
    }

    [Fact]
    public async Task One_unwritten_result_does_not_stop_the_rest_of_the_cycle()
    {
        var harness = TriageHarness.Build(TriageHarness.DistinctErrors(3));
        harness.Sink.FailNextWrites = 1;

        var result = await harness.Service.RunOnceAsync();

        Assert.Equal(1, result.Unreported);
        Assert.Equal(2, harness.Sink.Records.Count);
        Assert.Equal(2, harness.Store.Seen.Count);
    }
}
