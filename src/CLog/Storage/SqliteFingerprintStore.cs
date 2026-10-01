using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace CLog.Storage;

/// <summary>
/// SQLite-backed store. The file survives restarts, which is the point: an error the
/// watcher judged last week must not be judged again just because the service was redeployed.
/// </summary>
public sealed class SqliteFingerprintStore : IFingerprintStore
{
    private readonly string _connectionString;
    private readonly ILogger<SqliteFingerprintStore> _logger;

    public SqliteFingerprintStore(string databasePath, ILogger<SqliteFingerprintStore> logger)
    {
        _logger = logger;

        var fullPath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS seen_fingerprints (
                fingerprint TEXT PRIMARY KEY,
                first_seen  TEXT NOT NULL,
                last_seen   TEXT NOT NULL,
                hit_count   INTEGER NOT NULL DEFAULT 1
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);

        _logger.LogDebug("Fingerprint store ready at {ConnectionString}", _connectionString);
    }

    public async Task<bool> TryMarkSeenAsync(
        string fingerprint,
        DateTimeOffset seenAt,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();

        // One statement, so two overlapping cycles cannot both decide the same error is new.
        // The returned hit_count is 1 only for a row this statement actually inserted;
        // an upsert that hit an existing row returns its incremented count instead.
        command.CommandText =
            """
            INSERT INTO seen_fingerprints (fingerprint, first_seen, last_seen, hit_count)
            VALUES ($fingerprint, $seenAt, $seenAt, 1)
            ON CONFLICT(fingerprint) DO UPDATE SET
                last_seen = $seenAt,
                hit_count = hit_count + 1
            RETURNING hit_count;
            """;
        command.Parameters.AddWithValue("$fingerprint", fingerprint);
        command.Parameters.AddWithValue("$seenAt", seenAt.UtcDateTime.ToString("O"));

        var hitCount = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
        return hitCount == 1;
    }
}
