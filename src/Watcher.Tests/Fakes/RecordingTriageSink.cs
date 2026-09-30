using Watcher.Model;
using Watcher.Output;

namespace Watcher.Tests.Fakes;

public sealed class RecordingTriageSink : ITriageSink
{
    private readonly List<TriageRecord> _records = [];

    public IReadOnlyList<TriageRecord> Records => _records;

    public Task WriteAsync(TriageRecord record, CancellationToken cancellationToken = default)
    {
        _records.Add(record);
        return Task.CompletedTask;
    }
}
