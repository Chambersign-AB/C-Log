using CLog.Model;

namespace CLog.Output;

/// <summary>Where a triage result goes once it is decided.</summary>
public interface ITriageSink
{
    /// <summary>
    /// Returns false when the result could not be written. The caller then leaves the error
    /// unmarked, so it is reported again rather than remembered as handled.
    /// </summary>
    Task<bool> WriteAsync(TriageRecord record, CancellationToken cancellationToken = default);
}
