using CLog.Model;

namespace CLog.Output;

/// <summary>Where a triage result goes once it is decided.</summary>
public interface ITriageSink
{
    Task WriteAsync(TriageRecord record, CancellationToken cancellationToken = default);
}
