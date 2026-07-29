using Microsoft.Data.Sqlite;
using AnimeGoNet.Data.Sqlite;

namespace AnimeGoNet.Data.Library;

public sealed class DirectoryDatabaseIndexStore(
    AnimeGoSqliteDatabase database,
    DirectoryDatabaseScanner scanner) : IDisposable
{
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public async Task<DirectoryDatabaseRefreshResult> RefreshAsync(
        string saveRoot,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var runId = Guid.NewGuid().ToString("N");
            await StartRunAsync(runId, utcNow, cancellationToken).ConfigureAwait(false);
            try
            {
                var result = await scanner.ScanAsync(saveRoot, cancellationToken).ConfigureAwait(false);
                await ReplaceIndexAsync(runId, result, utcNow, cancellationToken).ConfigureAwait(false);
                return new DirectoryDatabaseRefreshResult(
                    runId,
                    result.ScannedCount,
                    result.Entries.Count,
                    result.Issues.Count);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await BestEffortFailAsync(runId, "directory_database_cancelled", utcNow).ConfigureAwait(false);
                throw;
            }
            catch
            {
                await BestEffortFailAsync(runId, "directory_database_scan_failed", utcNow).ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task UpsertAsync(
        IReadOnlyList<DirectoryDatabaseEntry> entries,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0)
        {
            return;
        }
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection
                .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            foreach (var entry in entries)
            {
                await UpsertEntryAsync(connection, transaction, entry, utcNow, cancellationToken)
                    .ConfigureAwait(false);
            }
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<DirectoryDatabaseStatus> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM directory_database_entries),
                run.id, run.status, run.scanned_count, run.indexed_count,
                run.rejected_count, run.failure_code,
                run.started_at_utc, run.completed_at_utc
            FROM (SELECT 1) AS singleton
            LEFT JOIN directory_database_scan_runs AS run
              ON run.id = (
                  SELECT id
                  FROM directory_database_scan_runs
                  ORDER BY started_at_utc DESC, id DESC
                  LIMIT 1);
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Directory database status query returned no row.");
        }
        return new DirectoryDatabaseStatus(
            reader.GetInt32(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
            reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
            reader.IsDBNull(5) ? 0 : reader.GetInt32(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : DateTimeOffset.Parse(
                reader.GetString(7),
                System.Globalization.CultureInfo.InvariantCulture),
            reader.IsDBNull(8) ? null : DateTimeOffset.Parse(
                reader.GetString(8),
                System.Globalization.CultureInfo.InvariantCulture));
    }

    private async Task StartRunAsync(
        string runId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO directory_database_scan_runs (
                id, status, scanned_count, indexed_count, rejected_count,
                failure_code, started_at_utc, completed_at_utc)
            VALUES ($id, 'running', 0, 0, 0, NULL, $now, NULL);
            """;
        command.Parameters.AddWithValue("$id", runId);
        command.Parameters.AddWithValue("$now", Format(utcNow));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task ReplaceIndexAsync(
        string runId,
        DirectoryDatabaseScanResult result,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (var clear = connection.CreateCommand())
        {
            clear.Transaction = transaction;
            clear.CommandText = "DELETE FROM directory_database_entries;";
            await clear.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        foreach (var entry in result.Entries)
        {
            await UpsertEntryAsync(connection, transaction, entry, utcNow, cancellationToken)
                .ConfigureAwait(false);
        }
        foreach (var issue in result.Issues)
        {
            await using var insertIssue = connection.CreateCommand();
            insertIssue.Transaction = transaction;
            insertIssue.CommandText = """
                INSERT INTO directory_database_scan_issues (run_id, relative_path, error_code)
                VALUES ($run_id, $path, $code);
                """;
            insertIssue.Parameters.AddWithValue("$run_id", runId);
            insertIssue.Parameters.AddWithValue("$path", issue.RelativePath);
            insertIssue.Parameters.AddWithValue("$code", issue.ErrorCode);
            await insertIssue.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await using (var finish = connection.CreateCommand())
        {
            finish.Transaction = transaction;
            finish.CommandText = """
                UPDATE directory_database_scan_runs
                SET status = 'completed',
                    scanned_count = $scanned,
                    indexed_count = $indexed,
                    rejected_count = $rejected,
                    completed_at_utc = $now
                WHERE id = $id AND status = 'running';
                """;
            finish.Parameters.AddWithValue("$scanned", result.ScannedCount);
            finish.Parameters.AddWithValue("$indexed", result.Entries.Count);
            finish.Parameters.AddWithValue("$rejected", result.Issues.Count);
            finish.Parameters.AddWithValue("$now", Format(utcNow));
            finish.Parameters.AddWithValue("$id", runId);
            if (await finish.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            {
                throw new InvalidOperationException("Directory database scan run changed concurrently.");
            }
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task UpsertEntryAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DirectoryDatabaseEntry entry,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        Validate(entry);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO directory_database_entries (
                relative_path, entry_kind, info_hash, anime_name,
                season_number, episode_type, episode_number,
                seeded, downloaded, renamed, scraped,
                create_at_unix, update_at_unix, indexed_at_utc)
            VALUES (
                $path, $kind, $hash, $name,
                $season, $type, $episode,
                $seeded, $downloaded, $renamed, $scraped,
                $created, $updated, $indexed)
            ON CONFLICT(relative_path) DO UPDATE SET
                entry_kind = excluded.entry_kind,
                info_hash = excluded.info_hash,
                anime_name = excluded.anime_name,
                season_number = excluded.season_number,
                episode_type = excluded.episode_type,
                episode_number = excluded.episode_number,
                seeded = excluded.seeded,
                downloaded = excluded.downloaded,
                renamed = excluded.renamed,
                scraped = excluded.scraped,
                create_at_unix = excluded.create_at_unix,
                update_at_unix = excluded.update_at_unix,
                indexed_at_utc = excluded.indexed_at_utc;
            """;
        command.Parameters.AddWithValue("$path", entry.RelativePath);
        command.Parameters.AddWithValue("$kind", Kind(entry.Kind));
        command.Parameters.AddWithValue("$hash", entry.InfoHash);
        command.Parameters.AddWithValue("$name", entry.AnimeName);
        command.Parameters.AddWithValue("$season", (object?)entry.SeasonNumber ?? DBNull.Value);
        command.Parameters.AddWithValue("$type", (object?)entry.EpisodeType ?? DBNull.Value);
        command.Parameters.AddWithValue("$episode", (object?)entry.EpisodeNumber ?? DBNull.Value);
        command.Parameters.AddWithValue("$seeded", Boolean(entry.Seeded));
        command.Parameters.AddWithValue("$downloaded", Boolean(entry.Downloaded));
        command.Parameters.AddWithValue("$renamed", Boolean(entry.Renamed));
        command.Parameters.AddWithValue("$scraped", Boolean(entry.Scraped));
        command.Parameters.AddWithValue("$created", entry.CreateAtUnix);
        command.Parameters.AddWithValue("$updated", entry.UpdateAtUnix);
        command.Parameters.AddWithValue("$indexed", Format(utcNow));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task BestEffortFailAsync(
        string runId,
        string failureCode,
        DateTimeOffset utcNow)
    {
        try
        {
            await using var connection = await database.OpenConnectionAsync().ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE directory_database_scan_runs
                SET status = 'failed', failure_code = $failure, completed_at_utc = $now
                WHERE id = $id AND status = 'running';
                """;
            command.Parameters.AddWithValue("$failure", failureCode);
            command.Parameters.AddWithValue("$now", Format(utcNow));
            command.Parameters.AddWithValue("$id", runId);
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        catch
        {
            // The original scan exception remains authoritative.
        }
    }

    private static void Validate(DirectoryDatabaseEntry entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.RelativePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.AnimeName);
        if (Path.IsPathRooted(entry.RelativePath)
            || entry.RelativePath.Split('/').Any(segment => segment is "" or "." or "..")
            || entry.CreateAtUnix < 0
            || entry.UpdateAtUnix < 0)
        {
            throw new ArgumentException("Directory database entry is invalid.", nameof(entry));
        }
    }

    private static string Kind(DirectoryDatabaseEntryKind kind) => kind switch
    {
        DirectoryDatabaseEntryKind.Anime => "anime",
        DirectoryDatabaseEntryKind.Season => "season",
        DirectoryDatabaseEntryKind.Episode => "episode",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static object Boolean(bool? value) =>
        value.HasValue ? value.Value ? 1 : 0 : DBNull.Value;

    private static string Format(DateTimeOffset value) =>
        value.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture);

    public void Dispose() => _writeGate.Dispose();
}
