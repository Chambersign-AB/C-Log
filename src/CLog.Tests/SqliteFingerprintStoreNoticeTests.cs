using CLog.Model;
using CLog.Storage;
using CLog.Tests.Fakes;

namespace CLog.Tests;

/// <summary>What the SQLite store remembers about notices for a fingerprint.</summary>
public sealed class SqliteFingerprintStoreNoticeTests : IDisposable
{
    private static readonly DateTimeOffset Sent = new(2026, 9, 30, 8, 0, 0, TimeSpan.Zero);

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "c-log-tests", Guid.NewGuid().ToString("N"));

    private SqliteFingerprintStore NewStore() =>
        new(Path.Combine(_directory, "clog.db"), new TestLogger<SqliteFingerprintStore>());

    [Fact]
    public async Task A_fingerprint_has_no_notice_until_one_is_saved()
    {
        var store = NewStore();
        await store.InitializeAsync();

        Assert.Null(await store.GetNoticeAsync("abc123"));
    }

    [Fact]
    public async Task A_notice_not_yet_sent_comes_back_with_its_verdict_and_no_time()
    {
        var store = NewStore();
        await store.InitializeAsync();

        await store.SaveNoticeAsync("abc123", new NoticeState(Verdict.Analyze, null, null));

        Assert.Equal(new NoticeState(Verdict.Analyze, null, null), await store.GetNoticeAsync("abc123"));
        Assert.Null(await store.GetNoticeAsync("def456"));
    }

    [Fact]
    public async Task Saving_again_records_when_the_notice_went_out_and_keeps_the_solution()
    {
        var store = NewStore();
        await store.InitializeAsync();
        var pending = new NoticeState(Verdict.Known, "Restart the sync job.", null);
        await store.SaveNoticeAsync("abc123", pending);

        await store.SaveNoticeAsync("abc123", pending with { LastNotifiedAt = Sent });

        Assert.Equal(pending with { LastNotifiedAt = Sent }, await store.GetNoticeAsync("abc123"));
    }

    [Fact]
    public async Task The_notice_survives_a_restart()
    {
        var first = NewStore();
        await first.InitializeAsync();
        await first.SaveNoticeAsync("abc123", new NoticeState(Verdict.Known, "Restart the sync job.", Sent));

        var second = NewStore();
        await second.InitializeAsync();

        Assert.Equal(
            new NoticeState(Verdict.Known, "Restart the sync job.", Sent),
            await second.GetNoticeAsync("abc123"));
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
