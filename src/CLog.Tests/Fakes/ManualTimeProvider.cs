namespace CLog.Tests.Fakes;

/// <summary>
/// Time that only moves when a test says so. Timers created from it never fire on their own,
/// so a test decides exactly when the worker's polling interval elapses.
/// </summary>
public sealed class ManualTimeProvider : TimeProvider
{
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _now = new(2026, 9, 30, 8, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(callback, state);
        lock (_timers)
        {
            _timers.Add(timer);
        }

        return timer;
    }

    /// <summary>Moves the clock forward and fires every live timer once.</summary>
    public void Tick(TimeSpan elapsed)
    {
        _now += elapsed;

        ManualTimer[] timers;
        lock (_timers)
        {
            timers = [.. _timers];
        }

        foreach (var timer in timers)
        {
            timer.Fire();
        }
    }

    private sealed class ManualTimer(TimerCallback callback, object? state) : ITimer
    {
        private volatile bool _disposed;

        public void Fire()
        {
            if (!_disposed)
            {
                callback(state);
            }
        }

        public bool Change(TimeSpan dueTime, TimeSpan period) => !_disposed;

        public void Dispose() => _disposed = true;

        public ValueTask DisposeAsync()
        {
            _disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
