using CLog.Model;
using CLog.Output;

namespace CLog.Tests.Fakes;

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
