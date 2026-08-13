using System.Globalization;
using AnimeGoNet.Core.Metadata;
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

public sealed record OtherFileReadaptationSourceIdentity(
    int? MikanId,
    int? GroupId,
    int? BangumiSubjectId);

public sealed record OtherFileReadaptationPreview(
    string TaskId,
    string Title,
    string TaskStatus,
    string FileStrategy,
    IReadOnlyList<OtherFileReadaptationFile> Files,
    bool HasActiveResolutionLease,
    string SourceProfileId,
    string SourceAdapter,
    string? SourcePageUrl,
    string ReviewState);

public sealed record OtherFileReadaptationReviewFileComparison(
    string TaskFileId,
    string SourceName,
    string BeforeDisposition,
    string BeforeOtherReason,
    int? BeforeTmdbSeriesId,
    string? BeforeSeriesName,
    int? BeforeTmdbSeasonNumber,
    string? BeforeSeasonName,
    int? BeforeTmdbEpisodeNumber,
    string? BeforeEpisodeName,
    string AfterDisposition,
    string? AfterOtherReason,
    int? AfterTmdbSeriesId,
    string? AfterSeriesName,
    int? AfterTmdbSeasonNumber,
    string? AfterSeasonName,
    int? AfterTmdbEpisodeNumber,
    string? AfterEpisodeName,
    string? AfterEpisodeStrategy,
    bool PreservedSharedSource,
    string BeforeMediaPath,
    string? AfterMediaPath);

public sealed record OtherFileReadaptationReviewPreview(
    string TaskId,
    string Title,
    string TaskStatus,
    string ReviewState,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    DateTimeOffset? ReviewedAtUtc,
    IReadOnlyList<OtherFileReadaptationReviewFileComparison> Files);

public enum OtherFileReadaptationStartResult
{
    Started,
    NotFound,
    NotEligible,
    ActiveLease,
}

public enum OtherFileReadaptationReviewResult
{
    Approved,
    NotFound,
    NotPending,
    NotCompleted,
}

public sealed class OtherFileReadaptationStore(AnimeGoSqliteDatabase database)
{
    public async Task<OtherFileReadaptationReviewPreview?> GetReviewPreviewAsync(
        string taskId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        string title;
        string taskStatus;
        string reviewState;
        string? requestedAt;
        string? reviewedAt;
        await using (var task = connection.CreateCommand())
        {
            task.CommandText = """
                SELECT title, status, readaptation_review_state,
                       readaptation_review_requested_at_utc,
                       readaptation_reviewed_at_utc
                FROM ingest_tasks WHERE id = $task_id;
                """;
            task.Parameters.AddWithValue("$task_id", taskId);
            await using var reader = await task.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            title = reader.GetString(0);
            taskStatus = reader.GetString(1);
            reviewState = reader.GetString(2);
            requestedAt = reader.IsDBNull(3) ? null : reader.GetString(3);
            reviewedAt = reader.IsDBNull(4) ? null : reader.GetString(4);
        }

        if (requestedAt is null)
        {
            return new OtherFileReadaptationReviewPreview(
                taskId, title, taskStatus, reviewState, DateTimeOffset.MinValue, null, null, []);
        }

        var files = new List<OtherFileReadaptationReviewFileComparison>();
        DateTimeOffset? completedAt = null;
        await using (var query = connection.CreateCommand())
        {
            query.CommandText = """
                SELECT job.task_file_id, file.relative_path,
                       COALESCE(job.original_disposition, 'other'),
                       job.original_other_reason,
                       job.original_tmdb_series_id, before_series.canonical_name,
                       job.original_tmdb_season_number, before_season.canonical_name,
                       job.original_tmdb_episode_number, before_episode.name,
                       file.disposition, file.other_reason,
                       file.tmdb_series_id, after_series.canonical_name,
                       file.tmdb_season_number, after_season.canonical_name,
                       file.tmdb_episode_number, after_episode.name,
                       file.episode_resolution_source, job.preserve_source,
                       job.completed_at_utc, job.source_media_path,
                       (
                           SELECT operation.target_path
                           FROM file_operations AS operation
                           WHERE operation.task_file_id = file.id
                             AND operation.state = 'completed'
                             AND operation.updated_at_utc >= job.requested_at_utc
                           ORDER BY operation.updated_at_utc DESC, operation.id DESC
                           LIMIT 1
                       )
                FROM other_file_readaptation_jobs AS job
                JOIN task_files AS file ON file.id = job.task_file_id
                LEFT JOIN anime_series AS before_series
                  ON before_series.tmdb_series_id = job.original_tmdb_series_id
                LEFT JOIN anime_seasons AS before_season
                  ON before_season.series_id = before_series.id
                 AND before_season.season_number = job.original_tmdb_season_number
                LEFT JOIN tmdb_episodes AS before_episode
                  ON before_episode.series_id = before_series.id
                 AND before_episode.season_number = job.original_tmdb_season_number
                 AND before_episode.episode_number = job.original_tmdb_episode_number
                LEFT JOIN anime_series AS after_series
                  ON after_series.tmdb_series_id = file.tmdb_series_id
                LEFT JOIN anime_seasons AS after_season
                  ON after_season.series_id = after_series.id
                 AND after_season.season_number = file.tmdb_season_number
                LEFT JOIN tmdb_episodes AS after_episode
                  ON after_episode.series_id = after_series.id
                 AND after_episode.season_number = file.tmdb_season_number
                 AND after_episode.episode_number = file.tmdb_episode_number
                WHERE job.task_id = $task_id AND job.requested_at_utc = $requested_at
                ORDER BY file.relative_path, file.id;
                """;
            query.Parameters.AddWithValue("$task_id", taskId);
            query.Parameters.AddWithValue("$requested_at", requestedAt);
            await using var reader = await query.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                DateTimeOffset? rowCompletedAt = reader.IsDBNull(20)
                    ? null
                    : DateTimeOffset.Parse(
                        reader.GetString(20),
                        CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.RoundtripKind);
                if (rowCompletedAt is not null
                    && (completedAt is null || rowCompletedAt.Value > completedAt.Value))
                {
                    completedAt = rowCompletedAt;
                }

                files.Add(new OtherFileReadaptationReviewFileComparison(
                    reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetInt32(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    reader.IsDBNull(8) ? null : reader.GetInt32(8),
                    reader.IsDBNull(9) ? null : reader.GetString(9),
                    reader.GetString(10), reader.IsDBNull(11) ? null : reader.GetString(11),
                    reader.IsDBNull(12) ? null : reader.GetInt32(12),
                    reader.IsDBNull(13) ? null : reader.GetString(13),
                    reader.IsDBNull(14) ? null : reader.GetInt32(14),
                    reader.IsDBNull(15) ? null : reader.GetString(15),
                    reader.IsDBNull(16) ? null : reader.GetInt32(16),
                    reader.IsDBNull(17) ? null : reader.GetString(17),
                    reader.IsDBNull(18) ? null : reader.GetString(18),
                    reader.GetInt64(19) == 1,
                    reader.GetString(21),
                    reader.IsDBNull(22) ? null : reader.GetString(22)));
            }
        }

        return new OtherFileReadaptationReviewPreview(
            taskId,
            title,
            taskStatus,
            reviewState,
            DateTimeOffset.Parse(
                requestedAt,
                CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind),
            completedAt,
            reviewedAt is null
                ? null
                : DateTimeOffset.Parse(
                    reviewedAt,
                    CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind),
            files);
    }

    public async Task<OtherFileReadaptationPreview?> PreviewAsync(
        string taskId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        string? title = null;
        string? status = null;
        string? strategy = null;
        string? sourceProfileId = null;
        string? sourceAdapter = null;
        string? sourcePageUrl = null;
        string? reviewState = null;
        var activeLease = false;
        await using (var task = connection.CreateCommand())
        {
            task.CommandText = """
                SELECT task.title, task.status,
                       json_extract(task.route_snapshot_json, '$.file_strategy'),
                       EXISTS (
                           SELECT 1 FROM metadata_resolution_runs AS run
                           WHERE run.task_id = task.id AND run.status = 'running'),
                       task.source_profile_id, profile.adapter, task.source_page_url,
                       task.readaptation_review_state
                FROM ingest_tasks AS task
                JOIN source_profiles AS profile ON profile.id = task.source_profile_id
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
            sourceProfileId = reader.GetString(4);
            sourceAdapter = reader.GetString(5);
            sourcePageUrl = reader.IsDBNull(6) ? null : reader.GetString(6);
            reviewState = reader.GetString(7);
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
            activeLease,
            sourceProfileId!,
            sourceAdapter!,
            sourcePageUrl,
            reviewState!);
    }

    public async Task<OtherFileReadaptationStartResult> StartAsync(
        string taskId,
        DateTimeOffset utcNow,
        OtherFileReadaptationSourceIdentity? freshIdentity = null,
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
            || preview.Files.Count == 0)
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
                      WHERE active.task_file_id = file.id AND active.state = 'pending');
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
                    original_other_reason, state, requested_at_utc, completed_at_utc,
                    preserve_source, original_disposition,
                    original_tmdb_series_id, original_tmdb_season_number,
                    original_tmdb_episode_number)
                SELECT
                    $id, $task_id, $file_id, $source_media_path,
                    $other_reason, 'pending', $now, NULL, $preserve_source,
                    file.disposition, file.tmdb_series_id, file.tmdb_season_number,
                    file.tmdb_episode_number
                FROM task_files AS file
                WHERE file.id = $file_id AND file.task_id = $task_id;
                """;
            insert.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            insert.Parameters.AddWithValue("$task_id", taskId);
            insert.Parameters.AddWithValue("$file_id", file.TaskFileId);
            insert.Parameters.AddWithValue("$source_media_path", file.SourceMediaPath);
            insert.Parameters.AddWithValue("$other_reason", file.OtherReason);
            insert.Parameters.AddWithValue("$now", now);
            insert.Parameters.AddWithValue(
                "$preserve_source",
                file.SharedPathReferenceCount > 1 ? 1 : 0);
            if (await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            {
                throw new InvalidOperationException("Other readaptation source snapshot changed concurrently.");
            }
        }

        foreach (var file in preview.Files)
        {
            var sourceEpisode = TorrentEpisodeCandidateParser.Parse(file.SourceName);
            var fileCandidate = FileEpisodeCandidateResolver.Resolve(
                preview.SourceAdapter,
                file.SourceName);
            await using var resetFile = connection.CreateCommand();
            resetFile.Transaction = transaction;
            resetFile.CommandText = """
                UPDATE task_files
                SET disposition = 'pending', other_reason = NULL,
                    tmdb_series_id = NULL, tmdb_season_number = NULL,
                    tmdb_episode_number = NULL, tmdb_episode_id = NULL,
                    associated_task_file_id = NULL, rename_suffix = NULL,
                    episode_resolution_source = NULL,
                    episode_resolution_run_id = NULL,
                    episode_resolution_attempt_id = NULL,
                    source_episode = $source_episode,
                    file_episode_candidate = $file_episode_candidate
                WHERE task_id = $task_id AND id = $file_id;
                """;
            resetFile.Parameters.AddWithValue("$task_id", taskId);
            resetFile.Parameters.AddWithValue("$file_id", file.TaskFileId);
            resetFile.Parameters.AddWithValue(
                "$source_episode",
                (object?)sourceEpisode.SourceEpisode
                ?? (object?)fileCandidate?.Episode?.ToString(CultureInfo.InvariantCulture)
                ?? DBNull.Value);
            resetFile.Parameters.AddWithValue(
                "$file_episode_candidate",
                fileCandidate?.Episode is int candidate
                    ? candidate.ToString(CultureInfo.InvariantCulture)
                    : DBNull.Value);
            if (await resetFile.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            {
                throw new InvalidOperationException("Other readaptation file changed concurrently.");
            }
        }

        await using (var reset = connection.CreateCommand())
        {
            reset.Transaction = transaction;
            reset.CommandText = """
                DELETE FROM file_operations
                WHERE task_file_id IN (
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
                SET status = 'download_preparing',
                    mikanid = CASE WHEN $apply_fresh = 1 THEN $mikanid ELSE mikanid END,
                    groupid = CASE WHEN $apply_fresh = 1 THEN $groupid ELSE groupid END,
                    bangumi_subject_id = CASE WHEN $apply_fresh = 1 THEN $bgmid ELSE bangumi_subject_id END,
                    failure_kind = NULL, failure_reason = NULL,
                    readaptation_review_state = 'pending',
                    readaptation_review_requested_at_utc = $now,
                    readaptation_reviewed_at_utc = NULL,
                    updated_at_utc = $now
                WHERE id = $task_id AND status = 'organized';
                """;
            reset.Parameters.AddWithValue("$task_id", taskId);
            reset.Parameters.AddWithValue("$now", now);
            reset.Parameters.AddWithValue("$mikanid", (object?)freshIdentity?.MikanId ?? DBNull.Value);
            reset.Parameters.AddWithValue("$groupid", (object?)freshIdentity?.GroupId ?? DBNull.Value);
            reset.Parameters.AddWithValue("$bgmid", (object?)freshIdentity?.BangumiSubjectId ?? DBNull.Value);
            reset.Parameters.AddWithValue("$apply_fresh", freshIdentity is null ? 0 : 1);
            if (await reset.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false)
                != preview.Files.Count + 2)
            {
                throw new InvalidOperationException("Other readaptation state changed concurrently.");
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return OtherFileReadaptationStartResult.Started;
    }

    public async Task<OtherFileReadaptationReviewResult> ApproveReviewAsync(
        string taskId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE ingest_tasks
            SET readaptation_review_state = 'approved',
                readaptation_reviewed_at_utc = $now,
                updated_at_utc = $now
            WHERE id = $task_id
              AND status = 'organized'
              AND readaptation_review_state = 'pending'
              AND NOT EXISTS (
                SELECT 1 FROM other_file_readaptation_jobs AS job
                WHERE job.task_id = ingest_tasks.id AND job.state = 'pending')
            RETURNING 1;
            """;
        command.Parameters.AddWithValue("$task_id", taskId);
        command.Parameters.AddWithValue("$now", utcNow.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        if (await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null)
        {
            return OtherFileReadaptationReviewResult.Approved;
        }

        await using var state = connection.CreateCommand();
        state.CommandText = """
            SELECT status, readaptation_review_state,
                   EXISTS (SELECT 1 FROM other_file_readaptation_jobs AS job
                           WHERE job.task_id = ingest_tasks.id AND job.state = 'pending')
            FROM ingest_tasks WHERE id = $task_id;
            """;
        state.Parameters.AddWithValue("$task_id", taskId);
        await using var reader = await state.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return OtherFileReadaptationReviewResult.NotFound;
        }
        if (reader.GetString(1) != "pending")
        {
            return OtherFileReadaptationReviewResult.NotPending;
        }
        return OtherFileReadaptationReviewResult.NotCompleted;
    }
}
