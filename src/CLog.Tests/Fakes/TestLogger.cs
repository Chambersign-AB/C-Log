using Microsoft.Extensions.Logging;

namespace CLog.Tests.Fakes;

public sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

/// <summary>Captures log output so a test can assert that a failure was reported, not swallowed.</summary>
public sealed class TestLogger<T> : ILogger<T>
{
    private readonly List<LogEntry> _entries = [];

    /// <summary>A snapshot, so a test can read it while a background worker is still logging.</summary>
    public IReadOnlyList<LogEntry> Entries
    {
        get
        {
            lock (_entries)
            {
                return [.. _entries];
            }
        }
    }

    public IEnumerable<LogEntry> Warnings => Entries.Where(e => e.Level >= LogLevel.Warning);

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var entry = new LogEntry(logLevel, formatter(state, exception), exception);
        lock (_entries)
        {
            _entries.Add(entry);
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
