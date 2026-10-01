using Microsoft.Extensions.Logging;
using CLog.Model;
using CLog.Output;
using CLog.Tests.Fakes;

namespace CLog.Tests;

/// <summary>What the file sink does when triage.jsonl is held by another program.</summary>
public sealed class TriageSinkRetryTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "c-log-tests", Guid.NewGuid().ToString("N"));

    private readonly TestLogger<ConsoleAndFileTriageSink> _logger = new();
    private int _waits;

    private string LogPath => Path.Combine(_directory, "triage.jsonl");

    /// <summary>A sink that does not really wait between attempts; <paramref name="onWait"/> runs instead.</summary>
    private ConsoleAndFileTriageSink NewSink(Action? onWait = null) =>
        new(LogPath, _logger, (_, _) =>
        {
            _waits++;
            onWait?.Invoke();
            return Task.CompletedTask;
        });

    private static TriageRecord Record() => new()
    {
        At = new DateTimeOffset(2026, 9, 30, 8, 0, 0, TimeSpan.Zero),
        Fingerprint = "abc123",
        ExceptionType = "System.InvalidOperationException",
        Template = "Order {} could not be saved",
        Outcome = TriageOutcome.Judged,
        Verdict = Verdict.Analyze,
        Reason = "looks like a real defect"
    };

    /// <summary>Another program holding the file so that nobody else may write to it.</summary>
    private FileStream LockFile() => new(LogPath, FileMode.OpenOrCreate, FileAccess.Read, FileShare.Read);

    [Fact]
    public async Task A_written_record_is_reported_as_written()
    {
        var sink = NewSink();

        Assert.True(await sink.WriteAsync(Record()));
        Assert.Equal(0, _waits);
    }

    [Fact]
    public async Task A_file_locked_for_a_moment_is_written_once_it_is_released()
    {
        var sink = NewSink(onWait: () => _lock?.Dispose());
        _lock = LockFile();

        Assert.True(await sink.WriteAsync(Record()));

        Assert.Equal(1, _waits);
        Assert.Contains("abc123", Assert.Single(await File.ReadAllLinesAsync(LogPath)));
        Assert.DoesNotContain(_logger.Entries, e => e.Level >= LogLevel.Error);
    }

    [Fact]
    public async Task A_file_that_stays_locked_is_given_up_on_after_three_retries_and_reported_as_unwritten()
    {
        var sink = NewSink();
        _lock = LockFile();

        Assert.False(await sink.WriteAsync(Record()));

        Assert.Equal(3, _waits);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("not recorded"));

        _lock.Dispose();
        Assert.Empty(await File.ReadAllLinesAsync(LogPath));
    }

    [Fact]
    public async Task A_reader_that_shares_the_file_does_not_get_in_the_way()
    {
        var sink = NewSink();
        await sink.WriteAsync(Record());

        // How a tail or a log viewer opens a file it expects to grow.
        using var reader = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        Assert.True(await sink.WriteAsync(Record()));
        Assert.Equal(0, _waits);
    }

    private FileStream? _lock;

    public void Dispose()
    {
        _lock?.Dispose();
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
