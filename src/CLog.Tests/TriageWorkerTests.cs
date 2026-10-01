using System.Text.Json;
using Microsoft.Extensions.Options;
using CLog.Configuration;
using CLog.Knowledge;
using CLog.Output;
using CLog.Tests.Fakes;
using CLog.Triage;

namespace CLog.Tests;

/// <summary>
/// The worker with a model slower than its polling interval: the real worker, service and
/// output file, with time and the model under the test's control.
/// </summary>
public sealed class TriageWorkerTests : IDisposable
{
    private const string SkippedPoll = "Cycle still running, skipping poll";

    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "c-log-tests", Guid.NewGuid().ToString("N"));

    private readonly ManualTimeProvider _time = new();
    private readonly GatedOllamaClient _ollama = new();
    private readonly FakeSeqClient _seq = new([.. TriageHarness.DistinctErrors(3)]);
    private readonly TestLogger<TriageWorker> _log = new();

    private string LogPath => Path.Combine(_directory, "triage.jsonl");

    private TriageWorker BuildWorker()
    {
        var options = Options.Create(new CLogOptions { IntervalMinutes = 1, MaxJudgementsPerRun = 10 });
        var store = new InMemoryFingerprintStore();

        var service = new TriageService(
            _seq,
            store,
            _ollama,
            new ConsoleAndFileTriageSink(LogPath, new TestLogger<ConsoleAndFileTriageSink>()),
            new StaticKnowledgeSource(""),
            new StaticRuleSetProvider(RuleSet.Parse("""{ "ignore": [] }""")),
            options,
            new TestLogger<TriageService>(),
            _time);

        return new TriageWorker(service, store, options, _log, _time);
    }

    private int SkippedPolls() => _log.Entries.Count(e => e.Message == SkippedPoll);

    private string[] WrittenLines()
    {
        if (!File.Exists(LogPath))
        {
            return [];
        }

        // Shared for writing: the worker appends while the test looks, and a reader that
        // locked the file would make the sink's append fail and the line go missing.
        using var stream = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static async Task Eventually(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "timed out waiting for " + what);
            await Task.Delay(10);
        }
    }

    /// <summary>Judges all three errors, with the interval elapsing while each one is with the model.</summary>
    private async Task JudgeThreeErrorsSlowlyAsync()
    {
        for (var error = 1; error <= 3; error++)
        {
            await _ollama.WaitUntilAskedAsync();

            _time.Tick(Interval);
            var expected = error;
            await Eventually(() => SkippedPolls() == expected, $"poll {expected} to be skipped");

            _ollama.Answer("Nej");
        }

        await Eventually(() => WrittenLines().Length == 3, "three lines in triage.jsonl");
    }

    [Fact]
    public async Task Three_errors_with_a_model_slower_than_the_interval_all_reach_the_triage_log()
    {
        using var worker = BuildWorker();
        await worker.StartAsync(CancellationToken.None);

        await JudgeThreeErrorsSlowlyAsync();
        await worker.StopAsync(CancellationToken.None);

        var records = WrittenLines().Select(line => JsonDocument.Parse(line).RootElement).ToList();
        Assert.Equal(3, records.Count);
        Assert.Equal(3, records.Select(r => r.GetProperty("Fingerprint").GetString()).Distinct().Count());
        Assert.All(records, r => Assert.Equal("Judged", r.GetProperty("Outcome").GetString()));
        Assert.All(records, r => Assert.Equal("Analyze", r.GetProperty("Verdict").GetString()));
    }

    [Fact]
    public async Task A_poll_falling_due_during_a_cycle_is_skipped_and_logged_not_run_alongside_it()
    {
        using var worker = BuildWorker();
        await worker.StartAsync(CancellationToken.None);

        await JudgeThreeErrorsSlowlyAsync();
        await worker.StopAsync(CancellationToken.None);

        // Three intervals elapsed during the one cycle, and Seq was polled for that cycle only.
        Assert.Equal(3, SkippedPolls());
        Assert.Equal(1, _seq.CallCount);
    }

    [Fact]
    public async Task Polling_resumes_once_the_slow_cycle_is_done_and_reports_nothing_twice()
    {
        using var worker = BuildWorker();
        await worker.StartAsync(CancellationToken.None);
        await JudgeThreeErrorsSlowlyAsync();

        // Keep ticking: a tick that lands before the cycle has fully wound down is skipped,
        // the first one after it starts a new cycle.
        await Eventually(
            () =>
            {
                _time.Tick(Interval);
                return _seq.CallCount >= 2;
            },
            "a second poll");
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(3, WrittenLines().Length);
    }

    public void Dispose()
    {
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
