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

    /// <summary>The issue filed for this fingerprint, if any.</summary>
    Task<IssueLink?> GetIssueAsync(string fingerprint, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remembers the issue filed for a fingerprint, or moves its last-reported time forward.
    /// Kept apart from "seen" on purpose: it is saved the moment the issue exists, so an
    /// interruption before the fingerprint is marked seen cannot lead to a second issue.
    /// </summary>
    Task SaveIssueAsync(string fingerprint, int issueNumber, DateTimeOffset reportedAt, CancellationToken cancellationToken = default);

    /// <summary>What is known about notices for this fingerprint, if it has ever been due one.</summary>
    Task<NoticeState?> GetNoticeAsync(string fingerprint, CancellationToken cancellationToken = default);

    /// <summary>Remembers the verdict a notice reports, or that a notice has now been sent.</summary>
    Task SaveNoticeAsync(string fingerprint, NoticeState state, CancellationToken cancellationToken = default);
}
