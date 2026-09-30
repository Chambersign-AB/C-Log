using Watcher.Storage;

namespace Watcher.Tests.Fakes;

/// <summary>An in-memory stand-in for the SQLite store.</summary>
public sealed class InMemoryFingerprintStore : IFingerprintStore
{
    private readonly Dictionary<string, int> _seen = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, int> Seen => _seen;

    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

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
