using System.Globalization;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Diagnostics;
using AnimeGoNet.Data.Sqlite;
using Microsoft.Data.Sqlite;

namespace AnimeGoNet.Data.Library;

public sealed class MediaOrganizationStore(AnimeGoSqliteDatabase database)
{
    public async Task<MediaOrganizationClaim?> TryClaimNextAsync(
        DateTimeOffset utcNow,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(leaseDuration, TimeSpan.Zero);
        var now = Format(utcNow);
        var token = Guid.NewGuid().ToString("N");
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using (var recover = connection.CreateCommand())
        {
            recover.Transaction = transaction;
            recover.CommandText = """
                UPDATE download_jobs
                SET organization_state = CASE
                        WHEN organization_phase = 'cleanup_downloader'
                        THEN 'cleanup' ELSE 'pending' END,
                    organization_lease_token = NULL,
                    organization_lease_expires_at_utc = NULL,
                    organization_failure_code = 'media_organization_lease_expired',
                    organization_next_attempt_at_utc = $now,
                    updated_at_utc = $now
                WHERE organization_state = 'organizing'
                  AND organization_lease_expires_at_utc <= $now;
                """;
            recover.Parameters.AddWithValue("$now", now);
            await recover.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var promoteLinked = connection.CreateCommand())
        {
            promoteLinked.Transaction = transaction;
            promoteLinked.CommandText = """
                UPDATE ingest_tasks
                SET status = 'organized', failure_kind = NULL,
                    failure_reason = NULL, updated_at_utc = $now
                WHERE status = 'downloaded'
                  AND json_extract(route_snapshot_json, '$.file_strategy')
                      IN ('link', 'link_delete')
                  AND EXISTS (
                    SELECT 1
                    FROM download_jobs AS job
                    WHERE job.task_id = ingest_tasks.id
                      AND job.organization_state = 'cleanup'
                      AND job.organization_phase = 'cleanup_downloader');
                """;
            promoteLinked.Parameters.AddWithValue("$now", now);
            await promoteLinked.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        string? jobId = null;
        string? taskId = null;
        string? priorState = null;
        await using (var candidate = connection.CreateCommand())
        {
            candidate.Transaction = transaction;
            candidate.CommandText = """
                SELECT job.id, job.task_id, job.organization_state
                FROM download_jobs AS job
                JOIN ingest_tasks AS task ON task.id = job.task_id
                WHERE (
                    (job.organization_state = 'pending'
                     AND task.status = 'downloaded'
                     AND (
                         json_extract(task.route_snapshot_json, '$.file_strategy')
                             IN ('move', 'link', 'link_delete')
                         OR (
                             json_extract(task.route_snapshot_json, '$.file_strategy') = 'wait_move'
                             AND job.seeding_state IN ('not_required', 'completed')
                         )
                     ))
                    OR
                    (job.organization_state = 'cleanup'
                     AND (
                         (json_extract(task.route_snapshot_json, '$.file_strategy')
                              IN ('move', 'wait_move')
                          AND task.status = 'organizing_cleanup')
                         OR
                         (json_extract(task.route_snapshot_json, '$.file_strategy')
                              IN ('link', 'link_delete')
                          AND task.status IN ('downloaded', 'organized')
                          AND job.seeding_state IN ('not_required', 'completed'))
                     ))
                )
                  AND (job.organization_next_attempt_at_utc IS NULL
                       OR job.organization_next_attempt_at_utc <= $now)
                ORDER BY job.updated_at_utc, job.id
                LIMIT 1;
                """;
            candidate.Parameters.AddWithValue("$now", now);
            await using var reader = await candidate.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                jobId = reader.GetString(0);
                taskId = reader.GetString(1);
                priorState = reader.GetString(2);
            }
        }

        if (jobId is null || taskId is null || priorState is null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }

        var attempt = 0;
        await using (var claim = connection.CreateCommand())
        {
            claim.Transaction = transaction;
            claim.CommandText = """
                UPDATE download_jobs
                SET organization_state = 'organizing',
                    organization_lease_token = $token,
                    organization_lease_expires_at_utc = $expires,
                    organization_attempt_count = organization_attempt_count + 1,
                    organization_next_attempt_at_utc = NULL,
                    organization_failure_code = NULL,
                    updated_at_utc = $now
                WHERE id = $job_id AND task_id = $task_id
                  AND organization_state = $prior_state
                RETURNING organization_attempt_count;
                """;
            claim.Parameters.AddWithValue("$token", token);
            claim.Parameters.AddWithValue("$expires", Format(utcNow.Add(leaseDuration)));
            claim.Parameters.AddWithValue("$now", now);
            claim.Parameters.AddWithValue("$job_id", jobId);
            claim.Parameters.AddWithValue("$task_id", taskId);
            claim.Parameters.AddWithValue("$prior_state", priorState);
            var result = await claim.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (result is null)
            {
                throw new InvalidOperationException("Media organization candidate changed concurrently.");
            }

            attempt = Convert.ToInt32(result, CultureInfo.InvariantCulture);
        }

        var stage = priorState == "cleanup" ? MediaOrganizationStage.CleanupDownloader : MediaOrganizationStage.MoveFiles;
        string downloaderId;
        string infoHash;
        string fileStrategy;
        string downloadRoot;
        string saveRoot;
        string sourceId;
        string? sourceItemId;
        string? sourceWorkId;
        int? mikanId;
        int? bangumiId;
        var isOtherReadaptation = false;
        var mediaType = "tv";
        var linkType = "hard";
        await using (var details = connection.CreateCommand())
        {
            details.Transaction = transaction;
            details.CommandText = """
                SELECT job.downloader_id, job.info_hash, job.download_root_path, job.save_root_path,
                       task.source_id, task.source_item_id, task.bangumi_subject_id,
                       task.source_work_id, task.mikanid,
                       json_extract(task.route_snapshot_json, '$.file_strategy'),
                       EXISTS (
                           SELECT 1 FROM other_file_readaptation_jobs AS readaptation
                           WHERE readaptation.task_id = task.id
                             AND readaptation.state = 'pending'),
                       task.media_type,
                       COALESCE(json_extract(task.route_snapshot_json, '$.link_type'), 'hard')
                FROM download_jobs AS job
                JOIN ingest_tasks AS task ON task.id = job.task_id
                WHERE job.id = $job_id AND job.task_id = $task_id
                  AND job.organization_state = 'organizing'
                  AND job.organization_lease_token = $token;
                """;
            details.Parameters.AddWithValue("$job_id", jobId);
            details.Parameters.AddWithValue("$task_id", taskId);
            details.Parameters.AddWithValue("$token", token);
            await using var reader = await details.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("Claimed media organization job disappeared.");
            }

            downloaderId = reader.GetString(0);
            infoHash = reader.GetString(1);
            downloadRoot = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
            saveRoot = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
            sourceId = reader.GetString(4);
            sourceItemId = reader.IsDBNull(5) ? null : reader.GetString(5);
            bangumiId = reader.IsDBNull(6) ? null : reader.GetInt32(6);
            sourceWorkId = reader.IsDBNull(7) ? null : reader.GetString(7);
            mikanId = reader.IsDBNull(8) ? null : reader.GetInt32(8);
            fileStrategy = reader.GetString(9);
            isOtherReadaptation = reader.GetInt64(10) == 1;
            mediaType = reader.GetString(11);
            linkType = reader.GetString(12);
            if (fileStrategy is not ("link" or "link_delete" or "move" or "wait_move"))
            {
                throw new InvalidOperationException("Captured file strategy is unsupported.");
            }
            _ = SourceDownloadPolicy.NormalizeLinkType(fileStrategy, linkType);
        }

        if (string.IsNullOrWhiteSpace(downloadRoot) || string.IsNullOrWhiteSpace(saveRoot))
        {
            throw new InvalidOperationException("Media organization paths were not captured.");
        }

        var files = new List<MediaOrganizationFile>();
        if (stage == MediaOrganizationStage.MoveFiles
            || (stage == MediaOrganizationStage.CleanupDownloader && fileStrategy == "link_delete"))
        {
            await using var query = connection.CreateCommand();
            query.Transaction = transaction;
            query.CommandText = """
                SELECT file.id, file.relative_path, file.size_bytes, file.disposition,
                       COALESCE(series.tmdb_series_id, 0),
                       COALESCE(file.tmdb_season_number, 0), file.tmdb_episode_number,
                       COALESCE(series.canonical_name, movie.canonical_title),
                       file.rename_suffix, file.associated_task_file_id
                       , file.source_episode, readaptation.source_media_path,
                       COALESCE(readaptation.preserve_source, 0),
                       CASE WHEN file.disposition = 'movie' THEN 'movie'
                            ELSE task.media_type END,
                       file.tmdb_movie_id, movie.release_date,
                       movie.original_title
                FROM task_files AS file
                JOIN ingest_tasks AS task ON task.id = file.task_id
                LEFT JOIN other_file_readaptation_jobs AS readaptation
                  ON readaptation.task_file_id = file.id
                 AND readaptation.state = 'pending'
                LEFT JOIN anime_series AS series ON
                    (file.tmdb_series_id IS NOT NULL
                     AND series.tmdb_series_id = file.tmdb_series_id)
                    OR
                    (file.tmdb_series_id IS NULL
                     AND file.other_reason = 'tmdb_fallback_pending_completion'
                     AND series.tmdb_series_id = 0
                     AND series.bangumi_subject_id = task.bangumi_subject_id)
                LEFT JOIN anime_movies AS movie
                  ON file.tmdb_movie_id IS NOT NULL
                 AND movie.tmdb_movie_id = file.tmdb_movie_id
                WHERE file.task_id = $task_id
                  AND file.disposition IN ('episode', 'movie', 'other', 'extras')
                  AND ((file.disposition = 'movie' AND movie.id IS NOT NULL)
                       OR (file.disposition <> 'movie'
                           AND task.media_type = 'tv' AND series.id IS NOT NULL)
                       OR (file.disposition <> 'movie'
                           AND task.media_type = 'movie' AND movie.id IS NOT NULL))
                  AND COALESCE(file.download_wanted, 1) = 1
                  AND (
                      NOT EXISTS (
                          SELECT 1 FROM other_file_readaptation_jobs AS active
                          WHERE active.task_id = file.task_id AND active.state = 'pending')
                      OR readaptation.task_file_id IS NOT NULL)
                ORDER BY file.relative_path, file.id;
                """;
            query.Parameters.AddWithValue("$task_id", taskId);
            await using var reader = await query.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                files.Add(new MediaOrganizationFile(
                    reader.GetString(0), reader.GetString(1), reader.GetInt64(2), reader.GetString(3),
                    reader.GetInt32(4), reader.GetInt32(5), reader.IsDBNull(6) ? null : reader.GetInt32(6),
                    reader.GetString(7), reader.IsDBNull(8) ? null : reader.GetString(8),
                    reader.IsDBNull(9) ? null : reader.GetString(9),
                    reader.IsDBNull(10) ? null : reader.GetString(10),
                    reader.IsDBNull(11) ? null : reader.GetString(11),
                    reader.GetInt64(12) == 1,
                    reader.GetString(13),
                    reader.IsDBNull(14) ? null : reader.GetInt32(14),
                    reader.IsDBNull(15)
                        ? null
                        : DateOnly.ParseExact(
                            reader.GetString(15),
                            "yyyy-MM-dd",
                            CultureInfo.InvariantCulture),
                    reader.IsDBNull(16) ? null : reader.GetString(16)));
            }

            if (files.Count == 0)
            {
                throw new InvalidOperationException("Media organization task has no wanted files.");
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new MediaOrganizationClaim(
            jobId, taskId, downloaderId, infoHash, fileStrategy, downloadRoot, saveRoot,
            sourceId, sourceItemId, bangumiId, token, attempt, stage, files,
            sourceWorkId, mikanId, isOtherReadaptation, mediaType, linkType);
    }

    public async Task<IReadOnlyList<MediaOperationRecord>> EnsureOperationsAsync(
        MediaOrganizationClaim claim,
        IReadOnlyList<MediaOperationPlan> plans,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        if (claim.Stage != MediaOrganizationStage.MoveFiles
            || plans.Count != claim.Files.Count
            || !plans.Select(plan => plan.TaskFileId).ToHashSet(StringComparer.Ordinal)
                .SetEquals(claim.Files.Select(file => file.TaskFileId)))
        {
            throw new ArgumentException("Every claimed media file requires exactly one plan.", nameof(plans));
        }

        var now = Format(utcNow);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await GuardLeaseAsync(connection, transaction, claim, cancellationToken).ConfigureAwait(false);
        foreach (var plan in plans)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO file_operations (
                    id, task_file_id, strategy, source_path, target_path, state,
                    bytes_verified, failure_reason, created_at_utc, updated_at_utc)
                VALUES ($id, $file_id, $strategy, $source, $target, 'pending', 0, NULL, $now, $now)
                ON CONFLICT(task_file_id) DO NOTHING;
                """;
            insert.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            insert.Parameters.AddWithValue("$file_id", plan.TaskFileId);
            insert.Parameters.AddWithValue("$strategy", claim.FileStrategy);
            insert.Parameters.AddWithValue("$source", plan.SourcePath);
            insert.Parameters.AddWithValue("$target", plan.TargetPath);
            insert.Parameters.AddWithValue("$now", now);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var operations = await ReadOperationsAsync(connection, transaction, claim.TaskId, cancellationToken).ConfigureAwait(false);
        foreach (var plan in plans)
        {
            var operation = operations.Single(item => item.TaskFileId == plan.TaskFileId);
            if (!string.Equals(operation.SourcePath, plan.SourcePath, StringComparison.Ordinal)
                || !string.Equals(operation.TargetPath, plan.TargetPath, StringComparison.Ordinal)
                || !string.Equals(operation.Strategy, claim.FileStrategy, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Persisted media operation path differs from the immutable plan.");
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return operations;
    }

    public async Task<IReadOnlyList<MediaOperationRecord>> GetOperationsAsync(
        MediaOrganizationClaim claim,
        CancellationToken cancellationToken = default)
    {
        if (claim.Stage != MediaOrganizationStage.CleanupDownloader)
        {
            throw new ArgumentException("Completed operations are only read during downloader cleanup.", nameof(claim));
        }

        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await GuardLeaseAsync(connection, transaction, claim, cancellationToken).ConfigureAwait(false);
        var operations = await ReadOperationsAsync(connection, transaction, claim.TaskId, cancellationToken).ConfigureAwait(false);
        if (operations.Count == 0 || operations.Any(operation =>
                operation.State != "completed"
                || !string.Equals(operation.Strategy, claim.FileStrategy, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Downloader cleanup requires completed immutable file operations.");
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return operations;
    }

    public async Task CompleteFileAsync(
        MediaOrganizationClaim claim,
        string operationId,
        long bytesVerified,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytesVerified);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await GuardLeaseAsync(connection, transaction, claim, cancellationToken).ConfigureAwait(false);
        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE file_operations
            SET state = 'completed', bytes_verified = $bytes, failure_reason = NULL, updated_at_utc = $now
            WHERE id = $id AND task_file_id IN (SELECT id FROM task_files WHERE task_id = $task_id);
            """;
        update.Parameters.AddWithValue("$bytes", bytesVerified);
        update.Parameters.AddWithValue("$now", Format(utcNow));
        update.Parameters.AddWithValue("$id", operationId);
        update.Parameters.AddWithValue("$task_id", claim.TaskId);
        if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            throw new InvalidOperationException("Media operation changed concurrently.");
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateProgressAsync(
        MediaOrganizationClaim claim,
        string phase,
        int completedUnits,
        int totalUnits,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phase);
        ArgumentOutOfRangeException.ThrowIfLessThan(totalUnits, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(completedUnits);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(completedUnits, totalUnits);
        if (claim.Stage == MediaOrganizationStage.MoveFiles
            ? !MediaOrganizationPhases.IsMovePhase(phase)
            : phase != MediaOrganizationPhases.CleanupDownloader)
        {
            throw new ArgumentException("Progress phase does not match the claimed organization stage.", nameof(phase));
        }

        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await GuardLeaseAsync(connection, transaction, claim, cancellationToken).ConfigureAwait(false);
        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE download_jobs
            SET organization_phase = $phase,
                organization_completed_units = $completed,
                organization_total_units = $total,
                updated_at_utc = $now
            WHERE id = $job_id AND task_id = $task_id
              AND organization_state = 'organizing'
              AND organization_lease_token = $token;
            """;
        AddIdentity(update, claim);
        update.Parameters.AddWithValue("$phase", phase);
        update.Parameters.AddWithValue("$completed", completedUnits);
        update.Parameters.AddWithValue("$total", totalUnits);
        update.Parameters.AddWithValue("$now", Format(utcNow));
        if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            throw new InvalidOperationException("Media organization progress changed concurrently.");
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task CompleteMovesAsync(
        MediaOrganizationClaim claim,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        var now = Format(utcNow);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await GuardLeaseAsync(connection, transaction, claim, cancellationToken).ConfigureAwait(false);
        var operations = await ReadOperationsAsync(connection, transaction, claim.TaskId, cancellationToken).ConfigureAwait(false);
        if (operations.Count != claim.Files.Count || operations.Any(operation => operation.State != "completed"))
        {
            throw new InvalidOperationException("All media operations must complete before business completion.");
        }

        foreach (var file in claim.Files.Where(file =>
                     file.Disposition == "episode" && file.AssociatedFileId is null))
        {
            var operation = operations.Single(item => item.TaskFileId == file.TaskFileId);
            var completionId = Guid.NewGuid().ToString("N");
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO completion_records (
                    id, tmdb_series_id, tmdb_season_number, tmdb_episode_number,
                    source_id, source_item_id, media_path, completed_at_utc)
                VALUES ($id, $series, $season, $episode, $source, $source_item, $path, $now);
                UPDATE episode_claims SET state = 'completed', expires_at_utc = NULL
                WHERE task_file_id = $file_id AND state = 'active';

                INSERT INTO completion_aliases (
                    id, completion_id, source_id, source_work_id, source_episode,
                    info_hash, created_at_utc)
                VALUES (
                    $alias_id, $id, $source, $source_work_id, $source_episode,
                    $info_hash, $now);
                """;
            insert.Parameters.AddWithValue("$id", completionId);
            insert.Parameters.AddWithValue("$alias_id", Guid.NewGuid().ToString("N"));
            insert.Parameters.AddWithValue("$series", file.TmdbSeriesId);
            insert.Parameters.AddWithValue("$season", file.SeasonNumber);
            insert.Parameters.AddWithValue("$episode", file.EpisodeNumber!.Value);
            insert.Parameters.AddWithValue("$source", claim.SourceId.ToLowerInvariant());
            insert.Parameters.AddWithValue("$source_item", (object?)claim.SourceItemId ?? DBNull.Value);
            insert.Parameters.AddWithValue("$source_work_id", (object?)claim.SourceWorkId ?? DBNull.Value);
            insert.Parameters.AddWithValue("$source_episode", (object?)file.SourceEpisode ?? DBNull.Value);
            insert.Parameters.AddWithValue("$info_hash", claim.InfoHash.ToLowerInvariant());
            insert.Parameters.AddWithValue("$path", operation.TargetPath);
            insert.Parameters.AddWithValue("$now", now);
            insert.Parameters.AddWithValue("$file_id", file.TaskFileId);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var file in claim.Files.Where(file =>
                     file.MediaType == "movie" && file.AssociatedFileId is null))
        {
            if (file.TmdbMovieId is null or <= 0)
            {
                throw new InvalidOperationException("Movie completion requires a positive TMDB Movie ID.");
            }

            var operation = operations.Single(item => item.TaskFileId == file.TaskFileId);
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO movie_completion_records (
                    id, tmdb_movie_id, source_id, source_item_id,
                    media_path, completed_at_utc)
                VALUES ($id, $tmdb_id, $source, $source_item, $path, $now);

                UPDATE movie_claims
                SET state = 'completed', expires_at_utc = NULL
                WHERE tmdb_movie_id = $tmdb_id
                  AND task_file_id = $file_id AND state = 'active';
                """;
            insert.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            insert.Parameters.AddWithValue("$tmdb_id", file.TmdbMovieId.Value);
            insert.Parameters.AddWithValue("$source", claim.SourceId.ToLowerInvariant());
            insert.Parameters.AddWithValue("$source_item", (object?)claim.SourceItemId ?? DBNull.Value);
            insert.Parameters.AddWithValue("$path", operation.TargetPath);
            insert.Parameters.AddWithValue("$now", now);
            insert.Parameters.AddWithValue("$file_id", file.TaskFileId);
            if (await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 2)
            {
                throw new InvalidOperationException("Movie completion claim changed concurrently.");
            }
        }

        foreach (var file in claim.Files.Where(file =>
                     file.MediaType == "tv" && file.TmdbSeriesId == 0))
        {
            var operation = operations.Single(item => item.TaskFileId == file.TaskFileId);
            var scope = FallbackDedupScopeResolver.Resolve(
                claim.SourceId,
                claim.MikanId,
                claim.SourceWorkId,
                claim.SourceItemId,
                claim.InfoHash,
                file.RelativePath,
                file.SizeBytes,
                file.SourceEpisode);
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO fallback_completion_records (
                    id, anime_series_id, bangumi_subject_id, scope_kind, scope_key,
                    source_id, source_episode, media_path, completed_at_utc)
                SELECT $id, series.id, $bgmid, $scope_kind, $scope_key,
                       $source_id, $source_episode, $media_path, $now
                FROM anime_series AS series
                WHERE series.tmdb_series_id = 0
                  AND series.bangumi_subject_id = $bgmid
                ON CONFLICT(scope_kind, scope_key) DO NOTHING;

                UPDATE fallback_claims
                SET state = 'completed', expires_at_utc = NULL
                WHERE scope_kind = $scope_kind
                  AND scope_key = $scope_key
                  AND state = 'active'
                  AND task_file_id IN (
                      SELECT id FROM task_files WHERE task_id = $task_id);
                """;
            insert.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            insert.Parameters.AddWithValue("$bgmid", claim.BangumiSubjectId!.Value);
            insert.Parameters.AddWithValue("$scope_kind", scope.Kind);
            insert.Parameters.AddWithValue("$scope_key", scope.Key);
            insert.Parameters.AddWithValue("$source_id", claim.SourceId);
            insert.Parameters.AddWithValue("$source_episode", (object?)file.SourceEpisode ?? DBNull.Value);
            insert.Parameters.AddWithValue("$media_path", operation.TargetPath);
            insert.Parameters.AddWithValue("$now", now);
            insert.Parameters.AddWithValue("$task_id", claim.TaskId);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var isReadaptation = false;
        await using (var readaptation = connection.CreateCommand())
        {
            readaptation.Transaction = transaction;
            readaptation.CommandText = """
                SELECT COUNT(*)
                FROM other_file_readaptation_jobs
                WHERE task_id = $task_id AND state = 'pending';
                """;
            readaptation.Parameters.AddWithValue("$task_id", claim.TaskId);
            var pendingReadaptationFiles = Convert.ToInt32(
                await readaptation.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
                CultureInfo.InvariantCulture);
            if (claim.IsOtherReadaptation && pendingReadaptationFiles != claim.Files.Count)
            {
                throw new InvalidOperationException("Other readaptation files changed concurrently.");
            }

            isReadaptation = claim.IsOtherReadaptation;
        }

        await using var finish = connection.CreateCommand();
        finish.Transaction = transaction;
        finish.CommandText = isReadaptation
            ? """
            UPDATE other_file_readaptation_jobs
            SET state = 'completed', completed_at_utc = $now
            WHERE task_id = $task_id AND state = 'pending';
            UPDATE download_jobs
            SET organization_state = 'completed', organization_lease_token = NULL,
                organization_lease_expires_at_utc = NULL, organization_next_attempt_at_utc = NULL,
                organization_failure_code = NULL,
                organization_phase = 'completed',
                organization_completed_units = 1, organization_total_units = 1,
                updated_at_utc = $now, revision = revision + 1
            WHERE id = $job_id AND task_id = $task_id AND organization_state = 'organizing'
              AND organization_lease_token = $token;
            UPDATE ingest_tasks
            SET status = 'organized', failure_kind = NULL, failure_reason = NULL,
                updated_at_utc = $now
            WHERE id = $task_id AND status = 'downloaded';
            """
            : """
            UPDATE download_jobs
            SET organization_state = 'cleanup', organization_lease_token = NULL,
                organization_lease_expires_at_utc = NULL, organization_next_attempt_at_utc = NULL,
                organization_failure_code = NULL,
                organization_phase = 'cleanup_downloader',
                organization_completed_units = 0, organization_total_units = 1,
                updated_at_utc = $now, revision = revision + 1
            WHERE id = $job_id AND task_id = $task_id AND organization_state = 'organizing'
              AND organization_lease_token = $token;
            UPDATE ingest_tasks
            SET status = CASE
                    WHEN $strategy IN ('move', 'wait_move') THEN 'organizing_cleanup'
                    ELSE 'organized'
                END,
                updated_at_utc = $now
            WHERE id = $task_id AND status = 'downloaded';
            """;
        AddIdentity(finish, claim);
        finish.Parameters.AddWithValue("$strategy", claim.FileStrategy);
        finish.Parameters.AddWithValue("$now", now);
        var expectedUpdates = isReadaptation ? claim.Files.Count + 2 : 2;
        if (await finish.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != expectedUpdates)
        {
            throw new InvalidOperationException("Media organization completion changed concurrently.");
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task CompleteCleanupAsync(
        MediaOrganizationClaim claim,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default) =>
        FinishClaimAsync(claim, "completed", "organized", utcNow, cancellationToken);

    public async Task ReleaseAsync(
        MediaOrganizationClaim claim,
        string failureCode,
        DateTimeOffset retryAtUtc,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        StableErrorCode.Require(failureCode, nameof(failureCode));
        var restoredState = claim.Stage == MediaOrganizationStage.CleanupDownloader ? "cleanup" : "pending";
        var taskStatus = claim.Stage == MediaOrganizationStage.CleanupDownloader
            && claim.FileStrategy is "move" or "wait_move"
                ? "organizing_cleanup"
                : claim.Stage == MediaOrganizationStage.CleanupDownloader
                    && claim.FileStrategy is "link" or "link_delete"
                        ? "organized"
                        : "downloaded";
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE download_jobs
            SET organization_state = $state, organization_lease_token = NULL,
                organization_lease_expires_at_utc = NULL,
                organization_next_attempt_at_utc = $retry,
                organization_failure_code = $failure,
                updated_at_utc = $now, revision = revision + 1
            WHERE id = $job_id AND task_id = $task_id AND organization_state = 'organizing'
              AND organization_lease_token = $token;
            UPDATE ingest_tasks SET status = $task_status, failure_kind = 'organization',
                failure_reason = $failure, updated_at_utc = $now
            WHERE id = $task_id;
            """;
        AddIdentity(command, claim);
        command.Parameters.AddWithValue("$state", restoredState);
        command.Parameters.AddWithValue("$task_status", taskStatus);
        command.Parameters.AddWithValue("$retry", Format(retryAtUtc));
        command.Parameters.AddWithValue("$failure", failureCode);
        command.Parameters.AddWithValue("$now", Format(utcNow));
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 2)
        {
            throw new InvalidOperationException("Media organization release changed concurrently.");
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task FinishClaimAsync(
        MediaOrganizationClaim claim,
        string organizationState,
        string taskStatus,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        var now = Format(utcNow);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE download_jobs SET organization_state = $state, organization_lease_token = NULL,
                organization_lease_expires_at_utc = NULL, organization_next_attempt_at_utc = NULL,
                organization_failure_code = NULL,
                organization_phase = 'completed',
                organization_completed_units = 1, organization_total_units = 1,
                updated_at_utc = $now, revision = revision + 1
            WHERE id = $job_id AND task_id = $task_id AND organization_state = 'organizing'
              AND organization_lease_token = $token;
            UPDATE ingest_tasks SET status = $task_status, failure_kind = NULL,
                failure_reason = NULL, updated_at_utc = $now
            WHERE id = $task_id
              AND (
                  ($strategy IN ('move', 'wait_move') AND status = 'organizing_cleanup')
                  OR
                  ($strategy IN ('link', 'link_delete') AND status IN ('downloaded', 'organized'))
              );
            """;
        AddIdentity(command, claim);
        command.Parameters.AddWithValue("$strategy", claim.FileStrategy);
        command.Parameters.AddWithValue("$state", organizationState);
        command.Parameters.AddWithValue("$task_status", taskStatus);
        command.Parameters.AddWithValue("$now", now);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 2)
        {
            throw new InvalidOperationException("Media cleanup completion changed concurrently.");
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task GuardLeaseAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        MediaOrganizationClaim claim,
        CancellationToken cancellationToken)
    {
        await using var guard = connection.CreateCommand();
        guard.Transaction = transaction;
        guard.CommandText = """
            SELECT COUNT(*) FROM download_jobs
            WHERE id = $job_id AND task_id = $task_id AND organization_state = 'organizing'
              AND organization_lease_token = $token;
            """;
        AddIdentity(guard, claim);
        if (Convert.ToInt32(await guard.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture) != 1)
        {
            throw new InvalidOperationException("Media organization lease is no longer owned.");
        }
    }

    private static async Task<List<MediaOperationRecord>> ReadOperationsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string taskId,
        CancellationToken cancellationToken)
    {
        await using var query = connection.CreateCommand();
        query.Transaction = transaction;
        query.CommandText = """
            SELECT operation.id, operation.task_file_id, operation.strategy,
                   operation.source_path, operation.target_path,
                   operation.state, operation.bytes_verified
            FROM file_operations AS operation
            JOIN task_files AS file ON file.id = operation.task_file_id
            WHERE file.task_id = $task_id
              AND (
                  NOT EXISTS (
                      SELECT 1 FROM other_file_readaptation_jobs AS active
                      WHERE active.task_id = file.task_id AND active.state = 'pending')
                  OR EXISTS (
                      SELECT 1 FROM other_file_readaptation_jobs AS selected
                      WHERE selected.task_file_id = file.id AND selected.state = 'pending'))
            ORDER BY file.relative_path, file.id;
            """;
        query.Parameters.AddWithValue("$task_id", taskId);
        await using var reader = await query.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var results = new List<MediaOperationRecord>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new MediaOperationRecord(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), reader.GetInt64(6)));
        }

        return results;
    }

    private static void AddIdentity(SqliteCommand command, MediaOrganizationClaim claim)
    {
        command.Parameters.AddWithValue("$job_id", claim.JobId);
        command.Parameters.AddWithValue("$task_id", claim.TaskId);
        command.Parameters.AddWithValue("$token", claim.LeaseToken);
    }

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
