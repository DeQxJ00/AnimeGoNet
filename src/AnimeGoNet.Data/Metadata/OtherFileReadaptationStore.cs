using System.Globalization;
using AnimeGoNet.Data.Sqlite;
using Microsoft.Data.Sqlite;

namespace AnimeGoNet.Data.Metadata;

public sealed record OtherFileReadaptationFile(
    string TaskFileId,
    string SourceName,
    long SizeBytes,
    string OtherReason,
    int TmdbSeriesId,
    int TmdbSeasonNumber,
    string SourceMediaPath,
    int SharedPathReferenceCount);

public sealed record OtherFileReadaptationPreview(
    string TaskId,
    string Title,
    string TaskStatus,
    string FileStrategy,
    IReadOnlyList<OtherFileReadaptationFile> Files,
    bool HasActiveResolutionLease);

public enum OtherFileReadaptationStartResult
{
    Started,
    NotFound,
    NotEligible,
    ActiveLease,
}

public sealed class OtherFileReadaptationStore(AnimeGoSqliteDatabase database)
{
    public async Task<OtherFileReadaptationPreview?> PreviewAsync(
        string taskId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        string? title = null;
        string? status = null;
        string? strategy = null;
        var activeLease = false;
        await using (var task = connection.CreateCommand())
        {
            task.CommandText = """
                SELECT task.title, task.status,
                       json_extract(task.route_snapshot_json, '$.file_strategy'),
                       EXISTS (
                           SELECT 1 FROM metadata_resolution_runs AS run
                           WHERE run.task_id = task.id AND run.status = 'running')
                FROM ingest_tasks AS task
                WHERE task.id = $task_id;
                """;
            task.Parameters.AddWithValue("$task_id", taskId);
            await using var reader = await task.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            title = reader.GetString(0);
            status = reader.GetString(1);
            strategy = reader.GetString(2);
            activeLease = reader.GetInt64(3) == 1;
        }

        var files = new List<OtherFileReadaptationFile>();
        await using (var query = connection.CreateCommand())
        {
            query.CommandText = """
                SELECT file.id, file.relative_path, file.size_bytes, file.other_reason,
                       file.tmdb_series_id, file.tmdb_season_number, operation.target_path,
                       (
                           SELECT COUNT(*)
                           FROM file_operations AS shared
                           WHERE shared.target_path = operation.target_path
                             AND shared.state = 'completed')
                FROM task_files AS file
                JOIN file_operations AS operation
                  ON operation.task_file_id = file.id AND operation.state = 'completed'
                WHERE file.task_id = $task_id
                  AND file.disposition = 'other'
                  AND file.other_reason IS NOT NULL
                  AND file.tmdb_series_id IS NOT NULL
                  AND file.tmdb_series_id > 0
                  AND file.tmdb_season_number IS NOT NULL
                  AND file.tmdb_season_number > 0
                  AND NOT EXISTS (
                      SELECT 1 FROM other_file_readaptation_jobs AS active
                      WHERE active.task_file_id = file.id AND active.state = 'pending')
                ORDER BY file.relative_path, file.id;
                """;
            query.Parameters.AddWithValue("$task_id", taskId);
            await using var reader = await query.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                files.Add(new OtherFileReadaptationFile(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetInt64(2),
                    reader.GetString(3),
                    reader.GetInt32(4),
                    reader.GetInt32(5),
                    reader.GetString(6),
                    reader.GetInt32(7)));
            }
        }

        return new OtherFileReadaptationPreview(
            taskId,
            title!,
            status!,
            strategy!,
            files,
            activeLease);
    }

    public async Task<OtherFileReadaptationStartResult> StartAsync(
        string taskId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        var preview = await PreviewAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (preview is null)
        {
            return OtherFileReadaptationStartResult.NotFound;
        }

        if (preview.HasActiveResolutionLease)
        {
            return OtherFileReadaptationStartResult.ActiveLease;
        }

        if (preview.TaskStatus != "organized"
            || preview.FileStrategy is not ("move" or "wait_move")
            || preview.Files.Count == 0
            || preview.Files.Any(file => file.SharedPathReferenceCount != 1))
        {
            return OtherFileReadaptationStartResult.NotEligible;
        }

        var now = utcNow.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using (var guard = connection.CreateCommand())
        {
            guard.Transaction = transaction;
            guard.CommandText = """
                SELECT COUNT(*)
                FROM ingest_tasks AS task
                JOIN download_jobs AS job ON job.task_id = task.id
                WHERE task.id = $task_id
                  AND task.status = 'organized'
                  AND job.organization_state = 'completed'
                  AND json_extract(task.route_snapshot_json, '$.file_strategy') IN ('move', 'wait_move')
                  AND NOT EXISTS (
                      SELECT 1 FROM metadata_resolution_runs AS run
                      WHERE run.task_id = task.id AND run.status = 'running');
                """;
            guard.Parameters.AddWithValue("$task_id", taskId);
            if (Convert.ToInt32(
                    await guard.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
                    CultureInfo.InvariantCulture) != 1)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return OtherFileReadaptationStartResult.NotEligible;
            }
        }

        await using (var fileGuard = connection.CreateCommand())
        {
            fileGuard.Transaction = transaction;
            fileGuard.CommandText = """
                SELECT COUNT(*)
                FROM task_files AS file
                JOIN file_operations AS operation
                  ON operation.task_file_id = file.id AND operation.state = 'completed'
                WHERE file.task_id = $task_id
                  AND file.disposition = 'other'
                  AND file.other_reason IS NOT NULL
                  AND file.tmdb_series_id > 0
                  AND file.tmdb_season_number > 0
                  AND NOT EXISTS (
                      SELECT 1 FROM other_file_readaptation_jobs AS active
                      WHERE active.task_file_id = file.id AND active.state = 'pending')
                  AND 1 = (
                      SELECT COUNT(*) FROM file_operations AS shared
                      WHERE shared.target_path = operation.target_path
                        AND shared.state = 'completed');
                """;
            fileGuard.Parameters.AddWithValue("$task_id", taskId);
            if (Convert.ToInt32(
                    await fileGuard.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
                    CultureInfo.InvariantCulture) != preview.Files.Count)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return OtherFileReadaptationStartResult.NotEligible;
            }
        }

        foreach (var file in preview.Files)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO other_file_readaptation_jobs (
                    id, task_id, task_file_id, source_media_path,
                    original_other_reason, state, requested_at_utc, completed_at_utc)
                VALUES (
                    $id, $task_id, $file_id, $source_media_path,
                    $other_reason, 'pending', $now, NULL);
                """;
            insert.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            insert.Parameters.AddWithValue("$task_id", taskId);
            insert.Parameters.AddWithValue("$file_id", file.TaskFileId);
            insert.Parameters.AddWithValue("$source_media_path", file.SourceMediaPath);
            insert.Parameters.AddWithValue("$other_reason", file.OtherReason);
            insert.Parameters.AddWithValue("$now", now);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var reset = connection.CreateCommand())
        {
            reset.Transaction = transaction;
            reset.CommandText = """
                DELETE FROM file_operations
                WHERE task_file_id IN (
                    SELECT task_file_id FROM other_file_readaptation_jobs
                    WHERE task_id = $task_id AND state = 'pending');

                UPDATE task_files
                SET disposition = 'pending', other_reason = NULL,
                    tmdb_episode_number = NULL, tmdb_episode_id = NULL,
                    associated_task_file_id = NULL, rename_suffix = NULL,
                    episode_resolution_source = NULL,
                    episode_resolution_run_id = NULL,
                    episode_resolution_attempt_id = NULL
                WHERE task_id = $task_id
                  AND id IN (
                      SELECT task_file_id FROM other_file_readaptation_jobs
                      WHERE task_id = $task_id AND state = 'pending');

                UPDATE download_jobs
                SET organization_state = 'pending',
                    organization_lease_token = NULL,
                    organization_lease_expires_at_utc = NULL,
                    organization_next_attempt_at_utc = NULL,
                    organization_failure_code = NULL,
                    organization_phase = 'not_started',
                    organization_total_units = 0,
                    organization_completed_units = 0,
                    updated_at_utc = $now,
                    revision = revision + 1
                WHERE task_id = $task_id AND organization_state = 'completed';

                UPDATE ingest_tasks
                SET status = 'metadata_season_resolved',
                    failure_kind = NULL, failure_reason = NULL,
                    updated_at_utc = $now
                WHERE id = $task_id AND status = 'organized';
                """;
            reset.Parameters.AddWithValue("$task_id", taskId);
            reset.Parameters.AddWithValue("$now", now);
            if (await reset.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false)
                != (preview.Files.Count * 2) + 2)
            {
                throw new InvalidOperationException("Other readaptation state changed concurrently.");
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return OtherFileReadaptationStartResult.Started;
    }
}
