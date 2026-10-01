namespace CLog.Storage;

/// <summary>Remembers which error fingerprints have already been triaged.</summary>
public interface IFingerprintStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>Whether this fingerprint has been marked as seen. Changes nothing.</summary>
    Task<bool> IsSeenAsync(string fingerprint, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a sighting and reports whether this fingerprint had never been seen before.
    /// A fingerprint is only ever new once, so the same error is judged once and then counted.
    /// Call it for a new fingerprint only after its outcome has been written: a fingerprint
    /// marked first and judged afterwards is lost for good if the judgement is interrupted.
    /// </summary>
    Task<bool> TryMarkSeenAsync(string fingerprint, DateTimeOffset seenAt, CancellationToken cancellationToken = default);
}
