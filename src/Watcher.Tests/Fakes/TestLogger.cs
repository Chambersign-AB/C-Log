using Microsoft.Extensions.Logging;

namespace Watcher.Tests.Fakes;

public sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

/// <summary>Captures log output so a test can assert that a failure was reported, not swallowed.</summary>
public sealed class TestLogger<T> : ILogger<T>
{
    private readonly List<LogEntry> _entries = [];

    public IReadOnlyList<LogEntry> Entries => _entries;

    public IEnumerable<LogEntry> Warnings => _entries.Where(e => e.Level >= LogLevel.Warning);

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        _entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
