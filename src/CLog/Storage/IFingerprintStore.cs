namespace CLog.Storage;

/// <summary>Remembers which error fingerprints have already been triaged.</summary>
public interface IFingerprintStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a sighting and reports whether this fingerprint had never been seen before.
    /// A fingerprint is only ever new once, so the same error is judged once and then counted.
    /// </summary>
    Task<bool> TryMarkSeenAsync(string fingerprint, DateTimeOffset seenAt, CancellationToken cancellationToken = default);
}
