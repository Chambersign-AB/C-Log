using CLog.Model;
using CLog.Output;

namespace CLog.Tests.Fakes;

public sealed class RecordingTriageSink : ITriageSink
{
    private readonly List<TriageRecord> _records = [];

    public IReadOnlyList<TriageRecord> Records => _records;

    /// <summary>How many of the coming writes fail, standing in for a triage log that cannot be written.</summary>
    public int FailNextWrites { get; set; }

    public Task<bool> WriteAsync(TriageRecord record, CancellationToken cancellationToken = default)
    {
        if (FailNextWrites > 0)
        {
            FailNextWrites--;
            return Task.FromResult(false);
        }

        _records.Add(record);
        return Task.FromResult(true);
    }
}
