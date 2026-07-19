using System.Globalization;
using AnimeGoNet.Core.Downloads;
using AnimeGoNet.Data.Sqlite;

namespace AnimeGoNet.Data.Downloads;

public sealed class DownloadJobStore(AnimeGoSqliteDatabase database)
{
    public async Task<int> CountActiveAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM download_jobs
            JOIN ingest_tasks ON ingest_tasks.id = download_jobs.task_id
            WHERE ingest_tasks.status IN ('download_queued', 'downloading', 'downloaded', 'download_error');
            """;
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    public async Task<DownloadSyncResult> ApplyInstanceSnapshotAsync(
        string downloaderId,
        IReadOnlyList<DownloadTaskSnapshot> snapshots,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(downloaderId);
        ArgumentNullException.ThrowIfNull(snapshots);
        var now = utcNow.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        var byHash = snapshots
            .GroupBy(snapshot => snapshot.Hash, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (Microsoft.Data.Sqlite.SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        var jobs = new List<(string JobId, string TaskId, string InfoHash)>();
        await using (var query = connection.CreateCommand())
        {
            query.Transaction = transaction;
            query.CommandText = """
                SELECT download_jobs.id, download_jobs.task_id, download_jobs.info_hash
                FROM download_jobs
                JOIN ingest_tasks ON ingest_tasks.id = download_jobs.task_id
                WHERE download_jobs.downloader_id = $downloader_id
                  AND ingest_tasks.status IN ('download_queued', 'downloading', 'downloaded', 'download_error');
                """;
            query.Parameters.AddWithValue("$downloader_id", downloaderId);
            await using var reader = await query.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                jobs.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
            }
        }

        var matched = 0;
        foreach (var job in jobs)
        {
            if (!byHash.TryGetValue(job.InfoHash, out var snapshot))
            {
                await using var stale = connection.CreateCommand();
                stale.Transaction = transaction;
                stale.CommandText = """
                    UPDATE download_jobs
                    SET is_stale = 1, revision = revision + 1, updated_at_utc = $now
                    WHERE id = $id;
                    """;
                stale.Parameters.AddWithValue("$now", now);
                stale.Parameters.AddWithValue("$id", job.JobId);
                await stale.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            matched++;
            await using (var update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText = """
                    UPDATE download_jobs
                    SET state = $state, progress = $progress,
                        downloaded_bytes = $downloaded_bytes, total_bytes = $total_bytes,
                        speed_bytes_per_second = $speed, eta_seconds = $eta_seconds,
                        seeds = $seeds, peers = $peers, snapshot_at_utc = $now,
                        is_stale = 0, revision = revision + 1, updated_at_utc = $now
                    WHERE id = $id;
                    """;
                update.Parameters.AddWithValue("$state", ToDatabaseValue(snapshot.State));
                update.Parameters.AddWithValue("$progress", Math.Clamp(snapshot.Progress, 0, 1));
                update.Parameters.AddWithValue("$downloaded_bytes", Math.Max(0, snapshot.DownloadedBytes));
                update.Parameters.AddWithValue("$total_bytes", Math.Max(0, snapshot.TotalBytes));
                update.Parameters.AddWithValue("$speed", Math.Max(0, snapshot.DownloadSpeedBytesPerSecond));
                update.Parameters.AddWithValue("$eta_seconds", (object?)snapshot.EtaSeconds ?? DBNull.Value);
                update.Parameters.AddWithValue("$seeds", Math.Max(0, snapshot.Seeds));
                update.Parameters.AddWithValue("$peers", Math.Max(0, snapshot.Peers));
                update.Parameters.AddWithValue("$now", now);
                update.Parameters.AddWithValue("$id", job.JobId);
                await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using var updateTask = connection.CreateCommand();
            updateTask.Transaction = transaction;
            updateTask.CommandText = """
                UPDATE ingest_tasks
                SET status = $status, updated_at_utc = $now
                WHERE id = $task_id;
                """;
            updateTask.Parameters.AddWithValue("$status", ToBusinessStatus(snapshot.State));
            updateTask.Parameters.AddWithValue("$now", now);
            updateTask.Parameters.AddWithValue("$task_id", job.TaskId);
            await updateTask.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await UpsertRuntimeStateAsync(connection, transaction, downloaderId, connected: true, null, now, cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new DownloadSyncResult(jobs.Count, matched);
    }

    public async Task MarkInstanceUnavailableAsync(
        string downloaderId,
        string safeFailureCode,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        ValidateFailureCode(safeFailureCode);
        var now = utcNow.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (Microsoft.Data.Sqlite.SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        await UpsertRuntimeStateAsync(
            connection,
            transaction,
            downloaderId,
            connected: false,
            safeFailureCode,
            now,
            cancellationToken).ConfigureAwait(false);
        await using var stale = connection.CreateCommand();
        stale.Transaction = transaction;
        stale.CommandText = """
            UPDATE download_jobs
            SET is_stale = 1, revision = revision + 1, updated_at_utc = $now
            WHERE downloader_id = $downloader_id
              AND task_id IN (
                  SELECT id FROM ingest_tasks WHERE status IN ('download_queued', 'downloading', 'downloaded', 'download_error'));
            """;
        stale.Parameters.AddWithValue("$now", now);
        stale.Parameters.AddWithValue("$downloader_id", downloaderId);
        await stale.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DownloadJobListItemRecord>> ListAsync(
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 500);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT download_jobs.id, download_jobs.task_id, ingest_tasks.title,
                   ingest_tasks.source_id, download_jobs.downloader_id, download_jobs.info_hash,
                   download_jobs.state, ingest_tasks.status, download_jobs.progress,
                   download_jobs.downloaded_bytes, download_jobs.total_bytes,
                   download_jobs.speed_bytes_per_second, download_jobs.eta_seconds,
                   download_jobs.seeds, download_jobs.peers, download_jobs.is_stale,
                   download_jobs.revision, download_jobs.snapshot_at_utc,
                   download_jobs.updated_at_utc,
                   COALESCE(downloader_runtime_state.connected, 0),
                   downloader_runtime_state.failure_code,
                   downloader_runtime_state.last_success_at_utc
            FROM download_jobs
            JOIN ingest_tasks ON ingest_tasks.id = download_jobs.task_id
            LEFT JOIN downloader_runtime_state
              ON downloader_runtime_state.downloader_id = download_jobs.downloader_id
            ORDER BY download_jobs.updated_at_utc DESC, download_jobs.id
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var results = new List<DownloadJobListItemRecord>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new DownloadJobListItemRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetDouble(8),
                reader.GetInt64(9),
                reader.GetInt64(10),
                reader.GetInt64(11),
                reader.IsDBNull(12) ? null : reader.GetInt64(12),
                reader.GetInt32(13),
                reader.GetInt32(14),
                reader.GetInt64(15) != 0,
                reader.GetInt64(16),
                ReadDateTimeOffset(reader, 17),
                ReadDateTimeOffset(reader, 18)!.Value,
                reader.GetInt64(19) != 0,
                reader.IsDBNull(20) ? null : reader.GetString(20),
                ReadDateTimeOffset(reader, 21)));
        }

        return results;
    }

    private static async Task UpsertRuntimeStateAsync(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        Microsoft.Data.Sqlite.SqliteTransaction transaction,
        string downloaderId,
        bool connected,
        string? failureCode,
        string now,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO downloader_runtime_state (
                downloader_id, connected, failure_code, last_success_at_utc, updated_at_utc)
            VALUES (
                $downloader_id, $connected, $failure_code, $last_success_at_utc, $updated_at_utc)
            ON CONFLICT(downloader_id) DO UPDATE SET
                connected = excluded.connected,
                failure_code = excluded.failure_code,
                last_success_at_utc = COALESCE(excluded.last_success_at_utc, downloader_runtime_state.last_success_at_utc),
                updated_at_utc = excluded.updated_at_utc;
            """;
        command.Parameters.AddWithValue("$downloader_id", downloaderId);
        command.Parameters.AddWithValue("$connected", connected ? 1 : 0);
        command.Parameters.AddWithValue("$failure_code", (object?)failureCode ?? DBNull.Value);
        command.Parameters.AddWithValue("$last_success_at_utc", connected ? now : DBNull.Value);
        command.Parameters.AddWithValue("$updated_at_utc", now);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string ToDatabaseValue(DownloadTaskState state) => state switch
    {
        DownloadTaskState.Waiting => "waiting",
        DownloadTaskState.Downloading => "downloading",
        DownloadTaskState.Moving => "moving",
        DownloadTaskState.Seeding => "seeding",
        DownloadTaskState.Paused => "paused",
        DownloadTaskState.Complete => "complete",
        DownloadTaskState.Error => "error",
        _ => "unknown",
    };

    private static string ToBusinessStatus(DownloadTaskState state) => state switch
    {
        DownloadTaskState.Downloading or DownloadTaskState.Moving => "downloading",
        DownloadTaskState.Seeding or DownloadTaskState.Complete => "downloaded",
        DownloadTaskState.Error => "download_error",
        _ => "download_queued",
    };

    private static void ValidateFailureCode(string safeFailureCode)
    {
        if (string.IsNullOrWhiteSpace(safeFailureCode)
            || safeFailureCode.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '_' or '-')))
        {
            throw new ArgumentException("Failure code must be a stable ASCII identifier.", nameof(safeFailureCode));
        }
    }

    private static DateTimeOffset? ReadDateTimeOffset(Microsoft.Data.Sqlite.SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : DateTimeOffset.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
