using CLog.Storage;
using CLog.Tests.Fakes;

namespace CLog.Tests;

/// <summary>Asking the SQLite store whether a fingerprint is seen, without marking it.</summary>
public sealed class SqliteFingerprintStoreIsSeenTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "c-log-tests", Guid.NewGuid().ToString("N"));

    private SqliteFingerprintStore NewStore() =>
        new(Path.Combine(_directory, "clog.db"), new TestLogger<SqliteFingerprintStore>());

    [Fact]
    public async Task Asking_about_a_fingerprint_does_not_mark_it()
    {
        var store = NewStore();
        await store.InitializeAsync();

        Assert.False(await store.IsSeenAsync("abc123"));
        Assert.False(await store.IsSeenAsync("abc123"));
        Assert.True(await store.TryMarkSeenAsync("abc123", DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task A_marked_fingerprint_is_seen_and_others_are_not()
    {
        var store = NewStore();
        await store.InitializeAsync();
        await store.TryMarkSeenAsync("abc123", DateTimeOffset.UtcNow);

        Assert.True(await store.IsSeenAsync("abc123"));
        Assert.False(await store.IsSeenAsync("def456"));
    }

    [Fact]
    public async Task A_marked_fingerprint_is_still_seen_after_a_restart()
    {
        var first = NewStore();
        await first.InitializeAsync();
        await first.TryMarkSeenAsync("abc123", DateTimeOffset.UtcNow);

        var second = NewStore();
        await second.InitializeAsync();

        Assert.True(await second.IsSeenAsync("abc123"));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // A locked file in the temp directory is not worth failing a test over.
        }
    }
}
