using CLog.Storage;
using CLog.Tests.Fakes;

namespace CLog.Tests;

/// <summary>The issue number kept on a fingerprint in the SQLite store.</summary>
public sealed class SqliteFingerprintStoreIssueTests : IDisposable
{
    private static readonly DateTimeOffset Filed = new(2026, 9, 30, 8, 0, 0, TimeSpan.Zero);

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "c-log-tests", Guid.NewGuid().ToString("N"));

    private SqliteFingerprintStore NewStore() =>
        new(Path.Combine(_directory, "clog.db"), new TestLogger<SqliteFingerprintStore>());

    [Fact]
    public async Task A_fingerprint_has_no_issue_until_one_is_saved()
    {
        var store = NewStore();
        await store.InitializeAsync();

        Assert.Null(await store.GetIssueAsync("abc123"));
    }

    [Fact]
    public async Task A_saved_issue_comes_back_with_its_number_and_time()
    {
        var store = NewStore();
        await store.InitializeAsync();

        await store.SaveIssueAsync("abc123", 42, Filed);

        Assert.Equal(new IssueLink(42, Filed), await store.GetIssueAsync("abc123"));
        Assert.Null(await store.GetIssueAsync("def456"));
    }

    [Fact]
    public async Task Saving_again_moves_the_last_reported_time_forward()
    {
        var store = NewStore();
        await store.InitializeAsync();
        await store.SaveIssueAsync("abc123", 42, Filed);

        await store.SaveIssueAsync("abc123", 42, Filed.AddDays(1));

        Assert.Equal(new IssueLink(42, Filed.AddDays(1)), await store.GetIssueAsync("abc123"));
    }

    [Fact]
    public async Task The_issue_survives_a_restart()
    {
        var first = NewStore();
        await first.InitializeAsync();
        await first.SaveIssueAsync("abc123", 42, Filed);

        var second = NewStore();
        await second.InitializeAsync();

        Assert.Equal(42, (await second.GetIssueAsync("abc123"))?.Number);
    }

    [Fact]
    public async Task Saving_an_issue_does_not_mark_the_fingerprint_seen()
    {
        // The issue is saved the moment it exists; "seen" waits until the result is written.
        var store = NewStore();
        await store.InitializeAsync();

        await store.SaveIssueAsync("abc123", 42, Filed);

        Assert.False(await store.IsSeenAsync("abc123"));
    }

    [Fact]
    public async Task A_database_from_before_issues_were_kept_gains_the_table_on_start()
    {
        var old = NewStore();
        await old.InitializeAsync();
        await old.TryMarkSeenAsync("abc123", Filed);
        await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(
            $"Data Source={Path.Combine(_directory, "clog.db")}"))
        {
            await connection.OpenAsync();
            await using var drop = connection.CreateCommand();
            drop.CommandText = "DROP TABLE fingerprint_issues;";
            await drop.ExecuteNonQueryAsync();
        }

        var upgraded = NewStore();
        await upgraded.InitializeAsync();

        Assert.True(await upgraded.IsSeenAsync("abc123"));
        Assert.Null(await upgraded.GetIssueAsync("abc123"));
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
