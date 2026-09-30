using Watcher.Storage;
using Watcher.Tests.Fakes;

namespace Watcher.Tests;

public sealed class SqliteFingerprintStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "c-log-tests", Guid.NewGuid().ToString("N"));

    private string DatabasePath => Path.Combine(_directory, "watcher.db");

    private SqliteFingerprintStore NewStore() =>
        new(DatabasePath, new TestLogger<SqliteFingerprintStore>());

    [Fact]
    public async Task A_fingerprint_is_new_the_first_time_and_not_the_second()
    {
        var store = NewStore();
        await store.InitializeAsync();

        Assert.True(await store.TryMarkSeenAsync("abc123", DateTimeOffset.UtcNow));
        Assert.False(await store.TryMarkSeenAsync("abc123", DateTimeOffset.UtcNow));
        Assert.False(await store.TryMarkSeenAsync("abc123", DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task Different_fingerprints_are_tracked_separately()
    {
        var store = NewStore();
        await store.InitializeAsync();

        Assert.True(await store.TryMarkSeenAsync("first", DateTimeOffset.UtcNow));
        Assert.True(await store.TryMarkSeenAsync("second", DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task What_was_seen_survives_a_restart()
    {
        // The whole point of using a file: a redeploy must not re-report old errors.
        var first = NewStore();
        await first.InitializeAsync();
        Assert.True(await first.TryMarkSeenAsync("abc123", DateTimeOffset.UtcNow));

        var second = NewStore();
        await second.InitializeAsync();

        Assert.False(await second.TryMarkSeenAsync("abc123", DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task Initialising_twice_is_harmless()
    {
        var store = NewStore();

        await store.InitializeAsync();
        await store.InitializeAsync();

        Assert.True(await store.TryMarkSeenAsync("abc123", DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task The_database_directory_is_created_if_it_is_missing()
    {
        var store = NewStore();
        await store.InitializeAsync();

        Assert.True(File.Exists(DatabasePath));
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
