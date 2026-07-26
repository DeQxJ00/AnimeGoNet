using System.Globalization;
using AnimeGoNet.Data.Sqlite;

namespace AnimeGoNet.Data.Downloads;

public sealed record DownloaderUsageRecord(
    long SourceProfileCount,
    long IngestTaskCount,
    long DownloadJobCount,
    bool? Connected,
    string? FailureCode,
    DateTimeOffset? LastSuccessAtUtc,
    DateTimeOffset? UpdatedAtUtc);

public sealed class DownloaderAdminStore(AnimeGoSqliteDatabase database)
{
    public async Task RecordConnectionTestAsync(
        string downloaderId,
        bool connected,
        string? failureCode,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO downloader_runtime_state (
              downloader_id, connected, failure_code, last_success_at_utc, updated_at_utc)
            VALUES ($id, $connected, $failure,
                    CASE WHEN $connected = 1 THEN $now ELSE NULL END, $now)
            ON CONFLICT(downloader_id) DO UPDATE SET
              connected = excluded.connected,
              failure_code = excluded.failure_code,
              last_success_at_utc = CASE WHEN excluded.connected = 1
                THEN excluded.updated_at_utc
                ELSE downloader_runtime_state.last_success_at_utc END,
              updated_at_utc = excluded.updated_at_utc;
            """;
        command.Parameters.AddWithValue("$id", downloaderId);
        command.Parameters.AddWithValue("$connected", connected ? 1 : 0);
        command.Parameters.AddWithValue("$failure", (object?)failureCode ?? DBNull.Value);
        command.Parameters.AddWithValue("$now", utcNow.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<DownloaderUsageRecord> GetUsageAsync(
        string downloaderId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(downloaderId);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
              (SELECT COUNT(*) FROM source_profiles WHERE downloader_id = $id),
              (SELECT COUNT(*) FROM ingest_tasks WHERE downloader_id = $id),
              (SELECT COUNT(*) FROM download_jobs WHERE downloader_id = $id),
              r.connected, r.failure_code, r.last_success_at_utc, r.updated_at_utc
            FROM (SELECT 1) seed
            LEFT JOIN downloader_runtime_state r ON r.downloader_id = $id;
            """;
        command.Parameters.AddWithValue("$id", downloaderId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        _ = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        return new DownloaderUsageRecord(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.IsDBNull(3) ? null : reader.GetBoolean(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            ReadDate(reader, 5),
            ReadDate(reader, 6));
    }

    private static DateTimeOffset? ReadDate(Microsoft.Data.Sqlite.SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : DateTimeOffset.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture);
}
