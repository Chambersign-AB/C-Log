using System.Text.Json;
using CLog.Model;
using CLog.Output;
using CLog.Tests.Fakes;

namespace CLog.Tests;

public sealed class TriageSinkTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "c-log-tests", Guid.NewGuid().ToString("N"));

    private string LogPath => Path.Combine(_directory, "triage.jsonl");

    private ConsoleAndFileTriageSink NewSink(TestLogger<ConsoleAndFileTriageSink> logger) =>
        new(LogPath, logger);

    private static TriageRecord Record(string fingerprint = "abc123") => new()
    {
        At = new DateTimeOffset(2026, 9, 30, 8, 0, 0, TimeSpan.Zero),
        Fingerprint = fingerprint,
        ExceptionType = "System.InvalidOperationException",
        Template = "Order {} could not be saved",
        TopFrame = TestEvents.TopApplicationFrame,
        SampleEventId = "event-1",
        OccurrenceCount = 3,
        Outcome = TriageOutcome.Judged,
        Verdict = Verdict.Analyze,
        Reason = "looks like a real defect"
    };

    [Fact]
    public async Task A_record_is_appended_as_one_json_line()
    {
        var sink = NewSink(new TestLogger<ConsoleAndFileTriageSink>());

        await sink.WriteAsync(Record());

        var line = Assert.Single(await File.ReadAllLinesAsync(LogPath));
        using var document = JsonDocument.Parse(line);
        Assert.Equal("abc123", document.RootElement.GetProperty("Fingerprint").GetString());
        Assert.Equal("Analyze", document.RootElement.GetProperty("Verdict").GetString());
        Assert.Equal(3, document.RootElement.GetProperty("OccurrenceCount").GetInt32());
    }

    [Fact]
    public async Task Records_accumulate_one_per_line()
    {
        var sink = NewSink(new TestLogger<ConsoleAndFileTriageSink>());

        await sink.WriteAsync(Record("first"));
        await sink.WriteAsync(Record("second"));

        Assert.Equal(2, (await File.ReadAllLinesAsync(LogPath)).Length);
    }

    [Fact]
    public async Task Fields_with_nothing_in_them_are_left_out()
    {
        var sink = NewSink(new TestLogger<ConsoleAndFileTriageSink>());

        await sink.WriteAsync(Record());

        var line = Assert.Single(await File.ReadAllLinesAsync(LogPath));
        Assert.DoesNotContain("KnownSolution", line);
        Assert.DoesNotContain("RuleId", line);
    }

    [Fact]
    public async Task Every_record_also_goes_to_the_console()
    {
        var logger = new TestLogger<ConsoleAndFileTriageSink>();
        var sink = NewSink(logger);

        await sink.WriteAsync(Record());

        Assert.Contains(logger.Entries, e => e.Message.Contains("ANALYZE") && e.Message.Contains("abc123"));
    }

    [Fact]
    public async Task A_filtered_record_names_the_rule_on_the_console()
    {
        var logger = new TestLogger<ConsoleAndFileTriageSink>();
        var sink = NewSink(logger);

        await sink.WriteAsync(Record() with
        {
            Outcome = TriageOutcome.FilteredByRule,
            Verdict = null,
            RuleId = "cancelled-requests"
        });

        Assert.Contains(logger.Entries, e => e.Message.Contains("cancelled-requests"));
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
