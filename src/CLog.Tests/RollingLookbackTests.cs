using CLog.Configuration;
using CLog.Seq;
using CLog.Tests.Fakes;

namespace CLog.Tests;

/// <summary>
/// The fetch window rolls from the last fetch whose errors all got an outcome, so nothing
/// logged during a long cycle falls outside it. LookbackMinutes only covers the first fetch.
/// </summary>
public class RollingLookbackTests
{
    private static readonly TimeSpan Lookback = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan Overlap = TimeSpan.FromMinutes(1);

    private readonly ManualTimeProvider _time = new();

    private void Wait(int minutes) => _time.Tick(TimeSpan.FromMinutes(minutes));

    [Fact]
    public async Task The_first_fetch_after_a_start_reaches_back_the_configured_lookback()
    {
        var harness = TriageHarness.Build([TestEvents.Error()], time: _time);
        var start = _time.GetUtcNow();

        await harness.Service.RunOnceAsync();

        Assert.Equal(start - Lookback, Assert.Single(harness.Seq.Sinces));
    }

    [Fact]
    public async Task The_next_fetch_starts_at_the_last_completed_one_however_long_ago_that_was()
    {
        var harness = TriageHarness.Build([TestEvents.Error()], time: _time);
        var firstFetch = _time.GetUtcNow();

        await harness.Service.RunOnceAsync();
        Wait(47);
        await harness.Service.RunOnceAsync();

        Assert.Equal(firstFetch - Overlap, harness.Seq.Sinces[1]);
    }

    [Fact]
    public async Task An_error_logged_during_a_long_cycle_is_fetched_by_the_next_one()
    {
        var start = _time.GetUtcNow();
        var harness = TriageHarness.Build(
            [TestEvents.MessageOnly("Subsystem a failed to start", id: "before") with { Timestamp = start.AddMinutes(-2) }],
            time: _time);
        harness.Seq.HonourSince = true;

        await harness.Service.RunOnceAsync();

        // Logged three minutes into a cycle that then ran for half an hour: older than any
        // fixed ten-minute lookback by the time the next cycle asks.
        harness.Seq.Add(
            TestEvents.MessageOnly("Subsystem b failed to start", id: "during") with { Timestamp = start.AddMinutes(3) });
        Wait(30);
        var second = await harness.Service.RunOnceAsync();

        Assert.Equal(1, second.Judged);
        Assert.Equal(new[] { "before", "during" }, harness.Sink.Records.Select(r => r.SampleEventId));
    }

    [Fact]
    public async Task An_empty_fetch_still_moves_the_window()
    {
        var harness = TriageHarness.Build([], time: _time);
        var firstFetch = _time.GetUtcNow();

        await harness.Service.RunOnceAsync();
        Wait(20);
        await harness.Service.RunOnceAsync();

        Assert.Equal(firstFetch - Overlap, harness.Seq.Sinces[1]);
    }

    [Fact]
    public async Task A_failed_fetch_does_not_move_the_window()
    {
        var harness = TriageHarness.Build([TestEvents.Error()], time: _time);
        var lastGoodFetch = _time.GetUtcNow();
        await harness.Service.RunOnceAsync();

        Wait(5);
        harness.Seq.FailNextFetches = 1;
        await Assert.ThrowsAsync<SeqUnavailableException>(() => harness.Service.RunOnceAsync());

        Wait(5);
        await harness.Service.RunOnceAsync();

        Assert.Equal(lastGoodFetch - Overlap, harness.Seq.Sinces[2]);
    }

    [Fact]
    public async Task Errors_deferred_by_the_budget_keep_the_window_open_until_they_are_judged()
    {
        var harness = TriageHarness.Build(TriageHarness.DistinctErrors(4), maxJudgements: 2, time: _time);
        harness.Seq.HonourSince = true;

        var first = await harness.Service.RunOnceAsync();
        Wait(30);
        var secondFetch = _time.GetUtcNow();
        var second = await harness.Service.RunOnceAsync();
        Wait(5);
        await harness.Service.RunOnceAsync();

        Assert.Equal(2, first.Deferred);
        Assert.Equal(harness.Seq.Sinces[0], harness.Seq.Sinces[1]);
        Assert.Equal(2, second.Judged);
        Assert.Equal(4, harness.Sink.Records.Count);

        // Everything fetched has an outcome now, so the window moves on.
        Assert.Equal(secondFetch - Overlap, harness.Seq.Sinces[2]);
    }

    [Fact]
    public async Task A_cycle_cut_short_keeps_the_window_open()
    {
        var failed = false;
        var harness = TriageHarness.Build(
            [TestEvents.Error()],
            mode: TriageMode.TwoStep,
            reply: _ =>
            {
                if (failed)
                {
                    return "Nej";
                }

                failed = true;
                throw new InvalidOperationException("the model fell over");
            },
            time: _time);
        harness.Seq.HonourSince = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Service.RunOnceAsync());
        Wait(30);
        var second = await harness.Service.RunOnceAsync();

        Assert.Equal(harness.Seq.Sinces[0], harness.Seq.Sinces[1]);
        Assert.Equal(1, second.Judged);
    }

    [Fact]
    public async Task A_result_that_could_not_be_written_keeps_the_window_open()
    {
        var harness = TriageHarness.Build([TestEvents.Error()], time: _time);
        harness.Seq.HonourSince = true;
        harness.Sink.FailNextWrites = 1;

        await harness.Service.RunOnceAsync();
        Wait(30);
        await harness.Service.RunOnceAsync();

        Assert.Equal(harness.Seq.Sinces[0], harness.Seq.Sinces[1]);
        Assert.Single(harness.Sink.Records);
    }
}
