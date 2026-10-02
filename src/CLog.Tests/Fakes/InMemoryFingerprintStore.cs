using CLog.Storage;

namespace CLog.Tests.Fakes;

/// <summary>An in-memory stand-in for the SQLite store.</summary>
public sealed class InMemoryFingerprintStore : IFingerprintStore
{
    private readonly Dictionary<string, int> _seen = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, int> Seen => _seen;

    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    private readonly Dictionary<string, IssueLink> _issues = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, IssueLink> Issues => _issues;

    public Task<IssueLink?> GetIssueAsync(string fingerprint, CancellationToken cancellationToken = default) =>
        Task.FromResult(_issues.GetValueOrDefault(fingerprint));

    public Task SaveIssueAsync(
        string fingerprint,
        int issueNumber,
        DateTimeOffset reportedAt,
        CancellationToken cancellationToken = default)
    {
        _issues[fingerprint] = new IssueLink(issueNumber, reportedAt);
        return Task.CompletedTask;
    }

    private readonly Dictionary<string, NoticeState> _notices = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, NoticeState> Notices => _notices;

    public Task<NoticeState?> GetNoticeAsync(string fingerprint, CancellationToken cancellationToken = default) =>
        Task.FromResult(_notices.GetValueOrDefault(fingerprint));

    public Task SaveNoticeAsync(string fingerprint, NoticeState state, CancellationToken cancellationToken = default)
    {
        _notices[fingerprint] = state;
        return Task.CompletedTask;
    }

    public Task<bool> IsSeenAsync(string fingerprint, CancellationToken cancellationToken = default) =>
        Task.FromResult(_seen.ContainsKey(fingerprint));

    public Task<bool> TryMarkSeenAsync(
        string fingerprint,
        DateTimeOffset seenAt,
        CancellationToken cancellationToken = default)
    {
        if (_seen.TryGetValue(fingerprint, out var count))
        {
            _seen[fingerprint] = count + 1;
            return Task.FromResult(false);
        }

        _seen[fingerprint] = 1;
        return Task.FromResult(true);
    }
}
