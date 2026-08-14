using System.Globalization;
using AnimeGoNet.Core.Compatibility;
using AnimeGoNet.Core.Diagnostics;
using AnimeGoNet.Core.Library;
using AnimeGoNet.Core.Metadata;
using AnimeGoNet.Data.Library;
using AnimeGoNet.Data.Sqlite;
using Microsoft.Data.Sqlite;

namespace AnimeGoNet.Data.Metadata;

public sealed class MetadataResolutionStore(AnimeGoSqliteDatabase database)
{
    public Task<MetadataTaskClaim?> TryClaimNextDownloadedAsync(
        DateTimeOffset utcNow,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default) =>
        TryClaimAsync(utcNow, leaseDuration, requireManualOverride: false, cancellationToken);

    public Task<MetadataTaskClaim?> TryClaimNextManualOverrideAsync(
        DateTimeOffset utcNow,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default) =>
        TryClaimAsync(utcNow, leaseDuration, requireManualOverride: true, cancellationToken);

    public async Task<MetadataEpisodeTaskClaim?> TryClaimNextSeasonResolvedAsync(
        DateTimeOffset utcNow,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(leaseDuration, TimeSpan.Zero);
        var now = Format(utcNow);
        var leaseToken = Guid.NewGuid().ToString("N");
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using (var recover = connection.CreateCommand())
        {
            recover.Transaction = transaction;
            recover.CommandText = """
                UPDATE metadata_resolution_runs
                SET status = 'interrupted', failure_kind = 'Cancelled',
                    fallback_eligible = 0, fallback_denial_reason = 'metadata_lease_expired',
                    completed_at_utc = $now, lease_token = NULL, lease_expires_at_utc = NULL
                WHERE status = 'running' AND lease_expires_at_utc <= $now;

                UPDATE ingest_tasks
                SET status = 'metadata_season_resolved', failure_kind = 'metadata_retry',
                    failure_reason = 'metadata_lease_expired', updated_at_utc = $now
                WHERE status = 'metadata_episode_resolving'
                  AND NOT EXISTS (
                    SELECT 1 FROM metadata_resolution_runs
                    WHERE metadata_resolution_runs.task_id = ingest_tasks.id
                      AND metadata_resolution_runs.status = 'running');
                """;
            recover.Parameters.AddWithValue("$now", now);
            await recover.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        string? taskId = null;
        string? title = null;
        int? mikanId = null;
        int? groupId = null;
        int? bangumiSubjectId = null;
        int? aniDbAnimeId = null;
        string? imdbTitleId = null;
        string? sourceAdapter = null;
        string? sourcePublishedAtRaw = null;
        DateTimeOffset? sourcePublishedAt = null;
        var torrentFileCount = 0;
        var tmdbSeriesId = 0;
        var tmdbSeasonNumber = 0;
        var seasonResolvedByAi = false;
        var hasMultipleSeasons = false;
        var episodeResolvedByTrustedOffset = false;
        var aiMetadataAttempted = false;
        var isOtherReadaptation = false;
        string? sourceProfileId = null;
        string? sourceId = null;
        var duplicateNotificationEnabled = true;
        await using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = """
                SELECT task.id, task.title, task.mikanid, task.groupid, task.bangumi_subject_id,
                       task.anidb_id, task.imdb_id,
                       profile.adapter, task.source_published_at_raw, task.source_published_at,
                       (SELECT COUNT(*) FROM task_files AS all_file WHERE all_file.task_id = task.id),
                       MIN(file.tmdb_series_id), MIN(file.tmdb_season_number),
                       EXISTS (
                         SELECT 1
                         FROM metadata_resolution_attempts AS attempt
                         JOIN metadata_resolution_runs AS prior_run ON prior_run.id = attempt.run_id
                         WHERE prior_run.task_id = task.id
                           AND (
                             attempt.strategy = 'ai_season'
                             OR (attempt.strategy = 'ai_metadata' AND attempt.stage = 'season'))
                           AND attempt.result = 'matched'),
                       COUNT(DISTINCT file.tmdb_season_number) > 1,
                       EXISTS (
                         SELECT 1
                         FROM metadata_resolution_attempts AS attempt
                         JOIN metadata_resolution_runs AS prior_run ON prior_run.id = attempt.run_id
                         WHERE prior_run.task_id = task.id
                           AND attempt.strategy = 'trusted_mikan_offset'
                           AND attempt.result = 'matched'),
                       EXISTS (
                         SELECT 1
                         FROM metadata_resolution_attempts AS attempt
                         JOIN metadata_resolution_runs AS prior_run ON prior_run.id = attempt.run_id
                         WHERE prior_run.task_id = task.id
                           AND attempt.strategy IN ('ai_metadata', 'ai_season', 'ai_episode'))
                       , EXISTS (
                           SELECT 1 FROM other_file_readaptation_jobs AS readaptation
                           WHERE readaptation.task_id = task.id
                             AND readaptation.state = 'pending')
                       , task.source_profile_id, task.source_id,
                       COALESCE(json_extract(
                           task.route_snapshot_json,
                           '$.duplicate_notification_enabled'), 1)
                FROM ingest_tasks AS task
                JOIN source_profiles AS profile ON profile.id = task.source_profile_id
                JOIN task_files AS file ON file.task_id = task.id AND file.disposition = 'pending'
                WHERE task.status = 'metadata_season_resolved'
                  AND file.tmdb_series_id IS NOT NULL
                  AND file.tmdb_season_number IS NOT NULL
                  AND NOT EXISTS (
                    SELECT 1 FROM metadata_resolution_runs
                    WHERE metadata_resolution_runs.task_id = task.id
                      AND metadata_resolution_runs.status = 'running')
                GROUP BY task.id
                HAVING COUNT(DISTINCT file.tmdb_series_id) = 1
                ORDER BY task.updated_at_utc, task.id
                LIMIT 1;
                """;
            await using var reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                taskId = reader.GetString(0);
                title = reader.GetString(1);
                mikanId = reader.IsDBNull(2) ? null : reader.GetInt32(2);
                groupId = reader.IsDBNull(3) ? null : reader.GetInt32(3);
                bangumiSubjectId = reader.IsDBNull(4) ? null : reader.GetInt32(4);
                aniDbAnimeId = reader.IsDBNull(5) ? null : reader.GetInt32(5);
                imdbTitleId = reader.IsDBNull(6) ? null : reader.GetString(6);
                sourceAdapter = reader.GetString(7);
                sourcePublishedAtRaw = reader.IsDBNull(8) ? null : reader.GetString(8);
                sourcePublishedAt = reader.IsDBNull(9)
                    ? null
                    : ParseDateTimeOffset(reader.GetString(9));
                torrentFileCount = reader.GetInt32(10);
                tmdbSeriesId = reader.GetInt32(11);
                tmdbSeasonNumber = reader.GetInt32(12);
                seasonResolvedByAi = reader.GetInt64(13) == 1;
                hasMultipleSeasons = reader.GetInt64(14) == 1;
                episodeResolvedByTrustedOffset = reader.GetInt64(15) == 1;
                aiMetadataAttempted = reader.GetInt64(16) == 1;
                isOtherReadaptation = reader.GetInt64(17) == 1;
                if (isOtherReadaptation)
                {
                    aiMetadataAttempted = false;
                }
                sourceProfileId = reader.GetString(18);
                sourceId = reader.GetString(19);
                duplicateNotificationEnabled = reader.GetInt64(20) == 1;
            }
        }

        if (taskId is null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }

        var runId = Guid.NewGuid().ToString("N");
        var attemptNumber = 1;
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO metadata_resolution_runs (
                    id, task_id, status, tmdb_access_confirmed, failure_kind,
                    fallback_eligible, fallback_denial_reason, started_at_utc,
                    completed_at_utc, lease_token, lease_expires_at_utc,
                    attempt_number, tmdb_series_id, tmdb_season_number)
                VALUES (
                    $id, $task_id, 'running', 1, NULL, 0, NULL, $now,
                    NULL, $lease_token, $lease_expires_at_utc,
                    (SELECT COALESCE(MAX(attempt_number), 0) + 1
                     FROM metadata_resolution_runs WHERE task_id = $task_id),
                    $tmdb_series_id, $tmdb_season_number)
                RETURNING attempt_number;
                """;
            insert.Parameters.AddWithValue("$id", runId);
            insert.Parameters.AddWithValue("$task_id", taskId);
            insert.Parameters.AddWithValue("$now", now);
            insert.Parameters.AddWithValue("$lease_token", leaseToken);
            insert.Parameters.AddWithValue("$lease_expires_at_utc", Format(utcNow.Add(leaseDuration)));
            insert.Parameters.AddWithValue("$tmdb_series_id", tmdbSeriesId);
            insert.Parameters.AddWithValue(
                "$tmdb_season_number",
                hasMultipleSeasons ? DBNull.Value : tmdbSeasonNumber);
            attemptNumber = Convert.ToInt32(
                await insert.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
                CultureInfo.InvariantCulture);
        }

        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE ingest_tasks
                SET status = 'metadata_episode_resolving', failure_kind = NULL,
                    failure_reason = NULL, updated_at_utc = $now
                WHERE id = $task_id AND status = 'metadata_season_resolved';
                """;
            update.Parameters.AddWithValue("$task_id", taskId);
            update.Parameters.AddWithValue("$now", now);
            if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            {
                throw new InvalidOperationException("Metadata Episode task was not claimable.");
            }
        }

        var files = new List<MetadataTaskFileProjection>();
        await using (var selectFiles = connection.CreateCommand())
        {
            selectFiles.Transaction = transaction;
            selectFiles.CommandText = """
                SELECT id, relative_path, size_bytes, source_episode, file_episode_candidate,
                       tmdb_episode_number, other_reason, tmdb_season_number
                FROM task_files
                WHERE task_id = $task_id AND disposition = 'pending'
                ORDER BY relative_path, id;
                """;
            selectFiles.Parameters.AddWithValue("$task_id", taskId);
            await using var reader = await selectFiles.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                files.Add(new MetadataTaskFileProjection(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetInt64(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetInt32(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetInt32(7)));
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new MetadataEpisodeTaskClaim(
            new MetadataTaskClaim(
                runId, taskId, title!, mikanId, groupId, bangumiSubjectId, attemptNumber, leaseToken,
                aniDbAnimeId, imdbTitleId, SourceAdapter: sourceAdapter,
                SourcePublishedAtRaw: sourcePublishedAtRaw,
                SourcePublishedAt: sourcePublishedAt,
                TorrentFileCount: torrentFileCount,
                SourceProfileId: sourceProfileId,
                SourceId: sourceId,
                DuplicateNotificationEnabled: duplicateNotificationEnabled,
                IsForcedReadaptation: isOtherReadaptation),
            tmdbSeriesId,
            tmdbSeasonNumber,
            files,
            seasonResolvedByAi,
            hasMultipleSeasons,
            episodeResolvedByTrustedOffset,
            aiMetadataAttempted,
            isOtherReadaptation);
    }

    public async Task<MetadataCanonicalSeason?> GetCanonicalSeasonAsync(
        int tmdbSeriesId,
        int tmdbSeasonNumber,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tmdbSeriesId, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(tmdbSeasonNumber, 1);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT series.canonical_name, series.original_name, season.canonical_name
            FROM anime_series AS series
            JOIN anime_seasons AS season ON season.series_id = series.id
            WHERE series.tmdb_series_id = $tmdb_series_id
              AND season.season_number = $tmdb_season_number;
            """;
        command.Parameters.AddWithValue("$tmdb_series_id", tmdbSeriesId);
        command.Parameters.AddWithValue("$tmdb_season_number", tmdbSeasonNumber);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var canonicalName = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
        var originalName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
        if (string.IsNullOrWhiteSpace(canonicalName) && string.IsNullOrWhiteSpace(originalName))
        {
            return null;
        }

        canonicalName = string.IsNullOrWhiteSpace(canonicalName) ? originalName : canonicalName;
        originalName = string.IsNullOrWhiteSpace(originalName) ? canonicalName : originalName;
        var seasonName = reader.IsDBNull(2)
            ? $"Season {tmdbSeasonNumber.ToString(CultureInfo.InvariantCulture)}"
            : reader.GetString(2);
        return new MetadataCanonicalSeason(
            new TmdbSeries(tmdbSeriesId, canonicalName, originalName, null),
            new TmdbSeason(1, tmdbSeriesId, tmdbSeasonNumber, seasonName, null, 0));
    }

    private async Task<MetadataTaskClaim?> TryClaimAsync(
        DateTimeOffset utcNow,
        TimeSpan leaseDuration,
        bool requireManualOverride,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(leaseDuration, TimeSpan.Zero);
        var now = Format(utcNow);
        var leaseExpires = Format(utcNow.Add(leaseDuration));
        var leaseToken = Guid.NewGuid().ToString("N");
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using (var recover = connection.CreateCommand())
        {
            recover.Transaction = transaction;
            recover.CommandText = """
                UPDATE metadata_resolution_runs
                SET status = 'interrupted', failure_kind = 'Cancelled',
                    fallback_eligible = 0, fallback_denial_reason = 'metadata_lease_expired',
                    completed_at_utc = $now, lease_token = NULL, lease_expires_at_utc = NULL
                WHERE status = 'running' AND lease_expires_at_utc <= $now;

                UPDATE ingest_tasks
                SET status = CASE WHEN EXISTS (
                        SELECT 1 FROM download_jobs
                        WHERE download_jobs.task_id = ingest_tasks.id
                          AND download_jobs.preparation_state IN ('pending', 'preparing'))
                    THEN 'download_preparing' ELSE 'downloaded' END,
                    failure_kind = 'metadata_retry',
                    failure_reason = 'metadata_lease_expired', updated_at_utc = $now
                WHERE status = 'metadata_resolving'
                  AND NOT EXISTS (
                    SELECT 1 FROM metadata_resolution_runs
                    WHERE metadata_resolution_runs.task_id = ingest_tasks.id
                      AND metadata_resolution_runs.status = 'running');
                """;
            recover.Parameters.AddWithValue("$now", now);
            await recover.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        string? taskId = null;
        string? title = null;
        int? mikanId = null;
        int? groupId = null;
        int? bangumiSubjectId = null;
        int? aniDbAnimeId = null;
        string? imdbTitleId = null;
        string? sourceAdapter = null;
        string? sourcePublishedAtRaw = null;
        DateTimeOffset? sourcePublishedAt = null;
        var torrentFileCount = 0;
        var isForcedReadaptation = false;
        await using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = """
                SELECT task.id, task.title, task.mikanid, task.groupid, task.bangumi_subject_id,
                       task.anidb_id, task.imdb_id,
                       profile.adapter, task.source_published_at_raw, task.source_published_at,
                       (SELECT COUNT(*) FROM task_files AS all_file WHERE all_file.task_id = task.id),
                       EXISTS (
                         SELECT 1 FROM other_file_readaptation_jobs AS readaptation
                         WHERE readaptation.task_id = task.id
                           AND readaptation.state = 'pending')
                FROM ingest_tasks AS task
                JOIN source_profiles AS profile ON profile.id = task.source_profile_id
                WHERE (
                        task.status = 'download_preparing'
                        OR (
                            task.status = 'downloaded'
                            AND EXISTS (
                                SELECT 1
                                FROM download_jobs AS claim_job
                                WHERE claim_job.task_id = task.id
                                  AND claim_job.preparation_state IN ('pending', 'preparing')
                            )
                        )
                    )
                  AND NOT EXISTS (
                    SELECT 1 FROM metadata_resolution_runs
                    WHERE metadata_resolution_runs.task_id = task.id
                      AND metadata_resolution_runs.status = 'running')
                  AND (($manual_override = 1 AND EXISTS (
                    SELECT 1 FROM mikan_work_rules AS rule
                    WHERE rule.mikanid = task.mikanid
                      AND rule.enabled = 1
                      AND rule.tmdb_series_id IS NOT NULL
                      AND rule.tmdb_season_number IS NOT NULL))
                    OR ($manual_override = 0 AND NOT EXISTS (
                    SELECT 1 FROM mikan_work_rules AS rule
                    WHERE rule.mikanid = task.mikanid
                      AND rule.enabled = 1
                      AND rule.tmdb_series_id IS NOT NULL
                      AND rule.tmdb_season_number IS NOT NULL)))
                ORDER BY task.updated_at_utc, task.id
                LIMIT 1;
                """;
            select.Parameters.AddWithValue("$manual_override", requireManualOverride ? 1 : 0);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                taskId = reader.GetString(0);
                title = reader.GetString(1);
                mikanId = reader.IsDBNull(2) ? null : reader.GetInt32(2);
                groupId = reader.IsDBNull(3) ? null : reader.GetInt32(3);
                bangumiSubjectId = reader.IsDBNull(4) ? null : reader.GetInt32(4);
                aniDbAnimeId = reader.IsDBNull(5) ? null : reader.GetInt32(5);
                imdbTitleId = reader.IsDBNull(6) ? null : reader.GetString(6);
                sourceAdapter = reader.GetString(7);
                sourcePublishedAtRaw = reader.IsDBNull(8) ? null : reader.GetString(8);
                sourcePublishedAt = reader.IsDBNull(9)
                    ? null
                    : ParseDateTimeOffset(reader.GetString(9));
                torrentFileCount = reader.GetInt32(10);
                isForcedReadaptation = reader.GetInt64(11) == 1;
            }
        }

        if (taskId is null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }

        var runId = Guid.NewGuid().ToString("N");
        var attemptNumber = 1;
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO metadata_resolution_runs (
                    id, task_id, status, tmdb_access_confirmed, failure_kind,
                    fallback_eligible, fallback_denial_reason, started_at_utc,
                    completed_at_utc, lease_token, lease_expires_at_utc,
                    attempt_number, tmdb_series_id, tmdb_season_number)
                VALUES (
                    $id, $task_id, 'running', 0, NULL, 0, NULL, $now,
                    NULL, $lease_token, $lease_expires_at_utc,
                    (SELECT COALESCE(MAX(attempt_number), 0) + 1
                     FROM metadata_resolution_runs WHERE task_id = $task_id),
                    NULL, NULL)
                RETURNING attempt_number;
                """;
            insert.Parameters.AddWithValue("$id", runId);
            insert.Parameters.AddWithValue("$task_id", taskId);
            insert.Parameters.AddWithValue("$now", now);
            insert.Parameters.AddWithValue("$lease_token", leaseToken);
            insert.Parameters.AddWithValue("$lease_expires_at_utc", leaseExpires);
            attemptNumber = Convert.ToInt32(
                await insert.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
                CultureInfo.InvariantCulture);
        }

        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE ingest_tasks
                SET status = 'metadata_resolving', failure_kind = NULL,
                    failure_reason = NULL, updated_at_utc = $now
                WHERE id = $task_id
                  AND (
                        status = 'download_preparing'
                        OR (
                            status = 'downloaded'
                            AND EXISTS (
                                SELECT 1
                                FROM download_jobs AS claim_job
                                WHERE claim_job.task_id = ingest_tasks.id
                                  AND claim_job.preparation_state IN ('pending', 'preparing')
                            )
                        )
                    );
                """;
            update.Parameters.AddWithValue("$task_id", taskId);
            update.Parameters.AddWithValue("$now", now);
            if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            {
                throw new InvalidOperationException("Metadata task was not claimable.");
            }
        }

        var files = new List<MetadataTaskFileProjection>();
        await using (var selectFiles = connection.CreateCommand())
        {
            selectFiles.Transaction = transaction;
            selectFiles.CommandText = """
                SELECT id, relative_path, size_bytes, source_episode, file_episode_candidate
                FROM task_files
                WHERE task_id = $task_id AND disposition = 'pending'
                ORDER BY relative_path, id;
                """;
            selectFiles.Parameters.AddWithValue("$task_id", taskId);
            await using var reader = await selectFiles.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                files.Add(new MetadataTaskFileProjection(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetInt64(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4)));
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new MetadataTaskClaim(
            runId,
            taskId,
            title!,
            mikanId,
            groupId,
            bangumiSubjectId,
            attemptNumber,
            leaseToken,
            aniDbAnimeId,
            imdbTitleId,
            files,
            sourceAdapter,
            sourcePublishedAtRaw,
            sourcePublishedAt,
            torrentFileCount,
            IsForcedReadaptation: isForcedReadaptation);
    }

    public async Task<string> RecordAttemptAsync(
        MetadataTaskClaim claim,
        MetadataAttempt attempt,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(attempt);
        ValidateIdentifier(attempt.Stage, nameof(attempt.Stage));
        ValidateIdentifier(attempt.Strategy, nameof(attempt.Strategy));
        ValidateIdentifier(attempt.Result, nameof(attempt.Result));
        if (attempt.ErrorCode is not null)
        {
            StableErrorCode.Require(attempt.ErrorCode, nameof(attempt.ErrorCode));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(attempt.DurationMilliseconds);
        ArgumentOutOfRangeException.ThrowIfLessThan(attempt.AttemptNumber, 1);
        var aiUsage = NormalizeAiUsage(attempt.AiUsage);
        var reason = NormalizeAttemptReason(attempt.Reason ?? attempt.ErrorCode);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO metadata_resolution_attempts (
                id, run_id, stage, strategy, priority, result, error_code,
                reason, retryable, attempt_number, duration_ms, created_at_utc,
                ai_model, ai_prompt_tokens, ai_completion_tokens, ai_total_tokens,
                ai_request_count, ai_tool_call_count)
            SELECT $id, id, $stage, $strategy, $priority, $result, $error_code,
                   $reason, $retryable, $attempt_number, $duration_ms, $created_at_utc,
                   $ai_model, $ai_prompt_tokens, $ai_completion_tokens, $ai_total_tokens,
                   $ai_request_count, $ai_tool_call_count
            FROM metadata_resolution_runs
            WHERE id = $run_id AND status = 'running' AND lease_token = $lease_token;
            """;
        var attemptId = Guid.NewGuid().ToString("N");
        command.Parameters.AddWithValue("$id", attemptId);
        command.Parameters.AddWithValue("$run_id", claim.RunId);
        command.Parameters.AddWithValue("$lease_token", claim.LeaseToken);
        command.Parameters.AddWithValue("$stage", attempt.Stage);
        command.Parameters.AddWithValue("$strategy", attempt.Strategy);
        command.Parameters.AddWithValue("$priority", (object?)attempt.Priority ?? DBNull.Value);
        command.Parameters.AddWithValue("$result", attempt.Result);
        command.Parameters.AddWithValue("$error_code", (object?)attempt.ErrorCode ?? DBNull.Value);
        command.Parameters.AddWithValue("$reason", (object?)reason ?? DBNull.Value);
        command.Parameters.AddWithValue("$retryable", attempt.Retryable ? 1 : 0);
        command.Parameters.AddWithValue("$attempt_number", attempt.AttemptNumber);
        command.Parameters.AddWithValue("$duration_ms", attempt.DurationMilliseconds);
        command.Parameters.AddWithValue("$created_at_utc", Format(utcNow));
        command.Parameters.AddWithValue("$ai_model", (object?)aiUsage?.Model ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$ai_prompt_tokens", (object?)aiUsage?.PromptTokens ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$ai_completion_tokens", (object?)aiUsage?.CompletionTokens ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$ai_total_tokens", (object?)aiUsage?.TotalTokens ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$ai_request_count", (object?)aiUsage?.RequestCount ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$ai_tool_call_count", (object?)aiUsage?.ToolCallCount ?? DBNull.Value);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            throw new InvalidOperationException("Metadata resolution lease is no longer active.");
        }

        return attemptId;
    }

    public async Task CompleteSeasonAsync(
        MetadataTaskClaim claim,
        TmdbSeries series,
        TmdbSeason season,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default) =>
        await CompleteSeasonCoreAsync(
            claim,
            series,
            [VerifiedSeason(series, season)],
            null,
            utcNow,
            cancellationToken).ConfigureAwait(false);

    public async Task CompleteLocalSeasonAsync(
        MetadataTaskClaim claim,
        TmdbSeries series,
        int seasonNumber,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentOutOfRangeException.ThrowIfLessThan(series.Id, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(seasonNumber, 1);
        await CompleteSeasonCoreAsync(
            claim,
            series,
            [new SeasonCompletion(
                seasonNumber,
                $"Season {seasonNumber.ToString(CultureInfo.InvariantCulture)}")],
            null,
            utcNow,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task CompleteAiSeasonAsync(
        MetadataTaskClaim claim,
        TmdbSeries series,
        TmdbSeason season,
        IReadOnlyList<MetadataSeasonFileSeed> fileSeeds,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default) =>
        await CompleteSeasonCoreAsync(
            claim,
            series,
            [VerifiedSeason(series, season)],
            fileSeeds,
            utcNow,
            cancellationToken).ConfigureAwait(false);

    public async Task CompleteAiSeasonsAsync(
        MetadataTaskClaim claim,
        TmdbSeries series,
        IReadOnlyList<TmdbSeason> seasons,
        IReadOnlyList<MetadataSeasonFileSeed> fileSeeds,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default) =>
        await CompleteSeasonCoreAsync(
            claim,
            series,
            seasons.Select(season => VerifiedSeason(series, season)).ToArray(),
            fileSeeds,
            utcNow,
            cancellationToken).ConfigureAwait(false);

    public async Task CompleteBangumiFallbackAsync(
        MetadataTaskClaim claim,
        BangumiSubject subject,
        int seasonNumber,
        MetadataFailure failure,
        IReadOnlyDictionary<string, int>? bangumiEpisodeIds,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(failure);
        if (claim.BangumiSubjectId != subject.Id
            || subject.Id <= 0
            || seasonNumber <= 0
            || failure.Kind != MetadataFailureKind.SemanticNoMatch
            || !failure.TmdbAccessConfirmed)
        {
            throw new ArgumentException("Bangumi fallback requires authoritative no-match, bgmid and a positive Season.");
        }

        var now = Format(utcNow);
        var canonicalName = string.IsNullOrWhiteSpace(subject.ChineseName)
            ? subject.Name
            : subject.ChineseName;
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        var seriesRowId = Guid.NewGuid().ToString("N");
        await using (var upsertSeries = connection.CreateCommand())
        {
            upsertSeries.Transaction = transaction;
            upsertSeries.CommandText = """
                INSERT INTO anime_series (
                    id, tmdb_series_id, bangumi_subject_id, canonical_name,
                    original_name, poster_path, needs_tmdb_completion,
                    created_at_utc, updated_at_utc)
                VALUES ($id, 0, $bgmid, $name, $original_name, NULL, 1, $now, $now)
                ON CONFLICT(bangumi_subject_id) WHERE tmdb_series_id = 0 DO UPDATE SET
                    canonical_name = excluded.canonical_name,
                    original_name = excluded.original_name,
                    needs_tmdb_completion = 1,
                    updated_at_utc = excluded.updated_at_utc;
                """;
            upsertSeries.Parameters.AddWithValue("$id", seriesRowId);
            upsertSeries.Parameters.AddWithValue("$bgmid", subject.Id);
            upsertSeries.Parameters.AddWithValue("$name", canonicalName);
            upsertSeries.Parameters.AddWithValue("$original_name", subject.Name);
            upsertSeries.Parameters.AddWithValue("$now", now);
            await upsertSeries.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var findSeries = connection.CreateCommand())
        {
            findSeries.Transaction = transaction;
            findSeries.CommandText = """
                SELECT id FROM anime_series
                WHERE tmdb_series_id = 0 AND bangumi_subject_id = $bgmid;
                """;
            findSeries.Parameters.AddWithValue("$bgmid", subject.Id);
            seriesRowId = (string)(await findSeries.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Bangumi fallback Series projection was not found."));
        }

        string sourceId;
        string? sourceItemId;
        string? sourceWorkId;
        int? mikanId;
        string infoHash;
        await using (var taskContext = connection.CreateCommand())
        {
            taskContext.Transaction = transaction;
            taskContext.CommandText = """
                SELECT task.source_id, task.source_item_id, task.source_work_id,
                       task.mikanid, job.info_hash
                FROM ingest_tasks AS task
                JOIN download_jobs AS job ON job.task_id = task.id
                WHERE task.id = $task_id;
                """;
            taskContext.Parameters.AddWithValue("$task_id", claim.TaskId);
            await using var reader = await taskContext.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("Bangumi fallback download context was not found.");
            }

            sourceId = reader.GetString(0);
            sourceItemId = reader.IsDBNull(1) ? null : reader.GetString(1);
            sourceWorkId = reader.IsDBNull(2) ? null : reader.GetString(2);
            mikanId = reader.IsDBNull(3) ? null : reader.GetInt32(3);
            infoHash = reader.GetString(4);
        }

        var decisions = new List<(string FileId, EpisodeClaimDecision Decision)>();
        foreach (var file in claim.Files ?? [])
        {
            var scope = FallbackDedupScopeResolver.Resolve(
                sourceId,
                mikanId,
                sourceWorkId,
                sourceItemId,
                infoHash,
                file.RelativePath,
                file.SizeBytes,
                file.SourceEpisode,
                bangumiEpisodeIds is not null
                    && bangumiEpisodeIds.TryGetValue(file.FileId, out var bangumiEpisodeId)
                    ? bangumiEpisodeId
                    : null);
            decisions.Add((
                file.FileId,
                await ClaimFallbackAsync(
                    connection,
                    transaction,
                    claim.TaskId,
                    file.FileId,
                    scope,
                    now,
                    cancellationToken).ConfigureAwait(false)));
        }

        await using (var complete = connection.CreateCommand())
        {
            complete.Transaction = transaction;
            complete.CommandText = """
                INSERT INTO anime_seasons (
                    id, series_id, season_number, canonical_name, poster_path,
                    created_at_utc, updated_at_utc)
                VALUES ($season_id, $series_id, $season, $season_name, NULL, $now, $now)
                ON CONFLICT(series_id, season_number) DO UPDATE SET
                    canonical_name = excluded.canonical_name,
                    updated_at_utc = excluded.updated_at_utc;

                UPDATE task_files
                SET tmdb_series_id = NULL,
                    tmdb_season_number = $season,
                    tmdb_episode_number = NULL,
                    tmdb_episode_id = NULL,
                    disposition = 'other',
                    other_reason = 'tmdb_fallback_pending_completion'
                WHERE task_id = $task_id AND disposition = 'pending';

                UPDATE metadata_resolution_runs
                SET status = 'fallback_resolved', tmdb_access_confirmed = 1,
                    failure_kind = 'SemanticNoMatch', fallback_eligible = 1,
                    fallback_denial_reason = NULL, completed_at_utc = $now,
                    lease_token = NULL, lease_expires_at_utc = NULL,
                    tmdb_series_id = NULL, tmdb_season_number = $season
                WHERE id = $run_id AND task_id = $task_id
                  AND status = 'running' AND lease_token = $lease_token;

                UPDATE ingest_tasks
                SET status = 'metadata_resolved',
                    failure_kind = 'tmdb_completion_pending',
                    failure_reason = $failure_code,
                    updated_at_utc = $now
                WHERE id = $task_id AND status = 'metadata_resolving';
                """;
            complete.Parameters.AddWithValue("$season_id", Guid.NewGuid().ToString("N"));
            complete.Parameters.AddWithValue("$series_id", seriesRowId);
            complete.Parameters.AddWithValue("$season", seasonNumber);
            complete.Parameters.AddWithValue("$season_name", $"Season {seasonNumber.ToString(CultureInfo.InvariantCulture)}");
            complete.Parameters.AddWithValue("$task_id", claim.TaskId);
            complete.Parameters.AddWithValue("$run_id", claim.RunId);
            complete.Parameters.AddWithValue("$lease_token", claim.LeaseToken);
            complete.Parameters.AddWithValue("$failure_code", failure.Code);
            complete.Parameters.AddWithValue("$now", now);
            if (await complete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) < 4)
            {
                throw new InvalidOperationException("Bangumi fallback metadata lease is no longer active.");
            }
        }

        foreach (var decision in decisions.Where(value => value.Decision != EpisodeClaimDecision.Owned))
        {
            await using var duplicate = connection.CreateCommand();
            duplicate.Transaction = transaction;
            duplicate.CommandText = """
                UPDATE task_files
                SET disposition = 'duplicate', other_reason = $reason
                WHERE id = $file_id AND task_id = $task_id
                  AND disposition = 'other'
                  AND other_reason = 'tmdb_fallback_pending_completion';
                """;
            duplicate.Parameters.AddWithValue(
                "$reason",
                decision.Decision == EpisodeClaimDecision.AlreadyCompleted
                    ? "fallback_already_completed"
                    : "fallback_claimed_by_another_task");
            duplicate.Parameters.AddWithValue("$file_id", decision.FileId);
            duplicate.Parameters.AddWithValue("$task_id", claim.TaskId);
            if (await duplicate.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            {
                throw new InvalidOperationException("Bangumi fallback duplicate projection changed concurrently.");
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task CompleteSeasonCoreAsync(
        MetadataTaskClaim claim,
        TmdbSeries series,
        IReadOnlyList<SeasonCompletion> seasons,
        IReadOnlyList<MetadataSeasonFileSeed>? fileSeeds,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(seasons);
        if (series.Id <= 0
            || seasons.Count == 0
            || seasons.Select(value => value.SeasonNumber).Distinct().Count() != seasons.Count
            || seasons.Any(season =>
                season.SeasonNumber <= 0
                || string.IsNullOrWhiteSpace(season.CanonicalName)))
        {
            throw new ArgumentException("Series/Season completion identity is invalid.", nameof(seasons));
        }

        var defaultSeasonNumber = seasons.Count == 1
            ? seasons[0].SeasonNumber
            : (int?)null;
        MetadataSeasonFileSeed[]? normalizedSeeds = null;
        if (fileSeeds is not null)
        {
            if (fileSeeds.Count == 0
                || fileSeeds.Select(seed => seed.RelativePath)
                    .Distinct(StringComparer.Ordinal).Count() != fileSeeds.Count)
            {
                throw new ArgumentException(
                    "AI Season file seeds must be non-empty and unique.",
                    nameof(fileSeeds));
            }

            normalizedSeeds = fileSeeds.Select(seed => seed with
            {
                SeasonNumber = seed.SeasonNumber ?? defaultSeasonNumber,
            }).ToArray();
            foreach (var seed in normalizedSeeds)
            {
                if (string.IsNullOrWhiteSpace(seed.RelativePath)
                    || seed.SeasonNumber is null
                    || !seasons.Any(season => season.SeasonNumber == seed.SeasonNumber.Value)
                    || (seed.EpisodeNumber is null) == (seed.OtherReason is null)
                    || seed.EpisodeNumber is <= 0)
                {
                    throw new ArgumentException(
                        "Every AI Season file seed requires either a positive Episode or an Other reason.",
                        nameof(fileSeeds));
                }

                if (seed.OtherReason is not null)
                {
                    ValidateIdentifier(seed.OtherReason, nameof(fileSeeds));
                }
            }
        }

        var now = Format(utcNow);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var seriesRowId = Guid.NewGuid().ToString("N");
        await using (var upsertSeries = connection.CreateCommand())
        {
            upsertSeries.Transaction = transaction;
            upsertSeries.CommandText = """
                INSERT INTO anime_series (
                    id, tmdb_series_id, bangumi_subject_id, canonical_name,
                    original_name, poster_path, needs_tmdb_completion, first_air_date,
                    created_at_utc, updated_at_utc)
                VALUES (
                    $id, $tmdb_id, NULL, $canonical_name, $original_name,
                    $poster_path, 0, $first_air_date, $now, $now)
                ON CONFLICT(tmdb_series_id) WHERE tmdb_series_id > 0 DO UPDATE SET
                    canonical_name = excluded.canonical_name,
                    original_name = excluded.original_name,
                    poster_path = COALESCE(excluded.poster_path, anime_series.poster_path),
                    first_air_date = COALESCE(excluded.first_air_date, anime_series.first_air_date),
                    updated_at_utc = excluded.updated_at_utc;
                """;
            upsertSeries.Parameters.AddWithValue("$id", seriesRowId);
            upsertSeries.Parameters.AddWithValue("$tmdb_id", series.Id);
            upsertSeries.Parameters.AddWithValue("$canonical_name", CanonicalName(series));
            upsertSeries.Parameters.AddWithValue("$original_name", series.OriginalName);
            upsertSeries.Parameters.AddWithValue(
                "$poster_path",
                (object?)series.PosterPath ?? DBNull.Value);
            upsertSeries.Parameters.AddWithValue(
                "$first_air_date",
                series.FirstAirDate is null
                    ? DBNull.Value
                    : series.FirstAirDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            upsertSeries.Parameters.AddWithValue("$now", now);
            await upsertSeries.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var findSeries = connection.CreateCommand())
        {
            findSeries.Transaction = transaction;
            findSeries.CommandText = "SELECT id FROM anime_series WHERE tmdb_series_id = $tmdb_id;";
            findSeries.Parameters.AddWithValue("$tmdb_id", series.Id);
            seriesRowId = (string)(await findSeries.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("TMDB Series upsert did not return a row."));
        }

        foreach (var season in seasons)
        {
            await using var upsertSeason = connection.CreateCommand();
            upsertSeason.Transaction = transaction;
            upsertSeason.CommandText = """
                INSERT INTO anime_seasons (
                    id, series_id, season_number, canonical_name, poster_path,
                    created_at_utc, updated_at_utc, air_date, episode_count)
                VALUES (
                    $id, $series_id, $season_number, $canonical_name, $poster_path,
                    $now, $now, $air_date, $episode_count)
                ON CONFLICT(series_id, season_number) DO UPDATE SET
                    canonical_name = excluded.canonical_name,
                    poster_path = COALESCE(excluded.poster_path, anime_seasons.poster_path),
                    air_date = COALESCE(excluded.air_date, anime_seasons.air_date),
                    episode_count = CASE
                        WHEN excluded.episode_count > 0 OR anime_seasons.episode_count = 0
                            THEN excluded.episode_count
                        ELSE anime_seasons.episode_count
                    END,
                    updated_at_utc = excluded.updated_at_utc;
                """;
            upsertSeason.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            upsertSeason.Parameters.AddWithValue("$series_id", seriesRowId);
            upsertSeason.Parameters.AddWithValue("$season_number", season.SeasonNumber);
            upsertSeason.Parameters.AddWithValue("$canonical_name", season.CanonicalName);
            upsertSeason.Parameters.AddWithValue(
                "$poster_path",
                (object?)season.PosterPath ?? DBNull.Value);
            upsertSeason.Parameters.AddWithValue(
                "$air_date",
                season.AirDate is null
                    ? DBNull.Value
                    : season.AirDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            upsertSeason.Parameters.AddWithValue("$episode_count", season.EpisodeCount);
            upsertSeason.Parameters.AddWithValue("$now", now);
            await upsertSeason.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await TmdbEpisodeProjectionWriter.UpsertAsync(
                connection,
                transaction,
                seriesRowId,
                series.Id,
                season.SeasonNumber,
                season.EpisodeCount,
                season.Episodes,
                now,
                cancellationToken).ConfigureAwait(false);
        }

        if (defaultSeasonNumber is not null)
        {
            await using var assignSeason = connection.CreateCommand();
            assignSeason.Transaction = transaction;
            assignSeason.CommandText = """
                UPDATE task_files
                SET tmdb_series_id = $tmdb_id, tmdb_season_number = $season_number
                WHERE task_id = $task_id AND disposition = 'pending';
                """;
            assignSeason.Parameters.AddWithValue("$tmdb_id", series.Id);
            assignSeason.Parameters.AddWithValue("$season_number", defaultSeasonNumber.Value);
            assignSeason.Parameters.AddWithValue("$task_id", claim.TaskId);
            await assignSeason.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (normalizedSeeds is not null)
        {
            foreach (var seed in normalizedSeeds)
            {
                await using var seedFile = connection.CreateCommand();
                seedFile.Transaction = transaction;
                seedFile.CommandText = """
                    UPDATE task_files
                    SET tmdb_series_id = $tmdb_id,
                        tmdb_season_number = $season_number,
                        tmdb_episode_number = $episode_number,
                        other_reason = $other_reason
                    WHERE task_id = $task_id
                      AND relative_path = $relative_path
                      AND disposition = 'pending';
                    """;
                seedFile.Parameters.AddWithValue("$task_id", claim.TaskId);
                seedFile.Parameters.AddWithValue("$relative_path", seed.RelativePath);
                seedFile.Parameters.AddWithValue("$tmdb_id", series.Id);
                seedFile.Parameters.AddWithValue("$season_number", seed.SeasonNumber!.Value);
                seedFile.Parameters.AddWithValue(
                    "$episode_number",
                    (object?)seed.EpisodeNumber ?? DBNull.Value);
                seedFile.Parameters.AddWithValue(
                    "$other_reason",
                    (object?)seed.OtherReason ?? DBNull.Value);
                if (await seedFile.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                {
                    throw new InvalidOperationException(
                        "AI Season task file changed concurrently or was not found.");
                }
            }

            if (defaultSeasonNumber is null)
            {
                await using var countPendingFiles = connection.CreateCommand();
                countPendingFiles.Transaction = transaction;
                countPendingFiles.CommandText = """
                    SELECT COUNT(*)
                    FROM task_files
                    WHERE task_id = $task_id AND disposition = 'pending';
                    """;
                countPendingFiles.Parameters.AddWithValue("$task_id", claim.TaskId);
                var pendingFileCount = Convert.ToInt32(
                    await countPendingFiles.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
                    CultureInfo.InvariantCulture);
                if (pendingFileCount != normalizedSeeds.Length)
                {
                    throw new InvalidOperationException(
                        "Every pending task file must receive an AI Season assignment.");
                }
            }
        }

        await using (var finish = connection.CreateCommand())
        {
            finish.Transaction = transaction;
            finish.CommandText = """
                UPDATE metadata_resolution_runs
                SET status = 'season_resolved', tmdb_access_confirmed = 1,
                    failure_kind = NULL, fallback_eligible = 0,
                    fallback_denial_reason = NULL, completed_at_utc = $now,
                    lease_token = NULL, lease_expires_at_utc = NULL,
                    tmdb_series_id = $tmdb_id,
                    tmdb_season_number = $season_number,
                    series_resolution_source = (
                        SELECT attempt.strategy
                        FROM metadata_resolution_attempts AS attempt
                        WHERE attempt.run_id = $run_id
                          AND attempt.stage = 'series'
                          AND attempt.result = 'matched'
                          AND attempt.strategy IN (
                              'manual_mikan_override', 'tmdb_title',
                              'backtrace', 'ai_metadata',
                              'trusted_mikan_offset')
                        ORDER BY attempt.created_at_utc DESC,
                                 attempt.id DESC
                        LIMIT 1),
                    series_resolution_attempt_id = (
                        SELECT attempt.id
                        FROM metadata_resolution_attempts AS attempt
                        WHERE attempt.run_id = $run_id
                          AND attempt.stage = 'series'
                          AND attempt.result = 'matched'
                          AND attempt.strategy IN (
                              'manual_mikan_override', 'tmdb_title',
                              'backtrace', 'ai_metadata',
                              'trusted_mikan_offset')
                        ORDER BY attempt.created_at_utc DESC,
                                 attempt.id DESC
                        LIMIT 1),
                    season_resolution_source = (
                        SELECT attempt.strategy
                        FROM metadata_resolution_attempts AS attempt
                        WHERE attempt.run_id = $run_id
                          AND attempt.stage = 'season'
                          AND attempt.result = 'matched'
                          AND attempt.strategy IN (
                              'manual_mikan_override', 'tmdb_air_date',
                              'backtrace', 'ai_metadata', 'title_season',
                              'first_season', 'trusted_mikan_offset')
                        ORDER BY attempt.created_at_utc DESC,
                                 attempt.id DESC
                        LIMIT 1),
                    season_resolution_attempt_id = (
                        SELECT attempt.id
                        FROM metadata_resolution_attempts AS attempt
                        WHERE attempt.run_id = $run_id
                          AND attempt.stage = 'season'
                          AND attempt.result = 'matched'
                          AND attempt.strategy IN (
                              'manual_mikan_override', 'tmdb_air_date',
                              'backtrace', 'ai_metadata', 'title_season',
                              'first_season', 'trusted_mikan_offset')
                        ORDER BY attempt.created_at_utc DESC,
                                 attempt.id DESC
                        LIMIT 1)
                WHERE id = $run_id AND task_id = $task_id
                  AND status = 'running' AND lease_token = $lease_token;

                UPDATE ingest_tasks
                SET status = 'metadata_season_resolved', failure_kind = NULL,
                    failure_reason = NULL, updated_at_utc = $now
                WHERE id = $task_id AND status = 'metadata_resolving';
                """;
            finish.Parameters.AddWithValue("$now", now);
            finish.Parameters.AddWithValue("$run_id", claim.RunId);
            finish.Parameters.AddWithValue("$task_id", claim.TaskId);
            finish.Parameters.AddWithValue("$lease_token", claim.LeaseToken);
            finish.Parameters.AddWithValue("$tmdb_id", series.Id);
            finish.Parameters.AddWithValue(
                "$season_number",
                defaultSeasonNumber is null ? DBNull.Value : defaultSeasonNumber.Value);
            if (await finish.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 2)
            {
                throw new InvalidOperationException("Metadata resolution lease is no longer active.");
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static SeasonCompletion VerifiedSeason(TmdbSeries series, TmdbSeason season)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(season);
        if (series.Id <= 0
            || season.Id <= 0
            || season.SeriesId != series.Id
            || season.SeasonNumber <= 0
            || string.IsNullOrWhiteSpace(season.Name)
            || (season.Episodes is not null
                && (season.Episodes.Count != season.EpisodeCount
                    || season.Episodes.Select(value => value.Id).Distinct().Count() != season.Episodes.Count
                    || season.Episodes.Select(value => value.EpisodeNumber).Distinct().Count() != season.Episodes.Count
                    || season.Episodes.Any(value =>
                        value.Id <= 0
                        || value.SeriesId != series.Id
                        || value.SeasonNumber != season.SeasonNumber
                        || value.EpisodeNumber <= 0))))
        {
            throw new ArgumentException("TMDB Series/Season identity is invalid.", nameof(season));
        }

        return new SeasonCompletion(
            season.SeasonNumber,
            season.Name,
            season.AirDate,
            season.EpisodeCount,
            season.PosterPath,
            season.Episodes);
    }

    private sealed record SeasonCompletion(
        int SeasonNumber,
        string CanonicalName,
        DateOnly? AirDate = null,
        int EpisodeCount = 0,
        string? PosterPath = null,
        IReadOnlyList<TmdbEpisode>? Episodes = null);

    public async Task<MetadataEpisodeCompletionResult> CompleteEpisodesAsync(
        MetadataEpisodeTaskClaim claim,
        IReadOnlyList<MetadataEpisodeFileResolution> fileResolutions,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(fileResolutions);
        if (fileResolutions.Count != claim.Files.Count
            || !fileResolutions.Select(value => value.FileId).ToHashSet(StringComparer.Ordinal)
                .SetEquals(claim.Files.Select(value => value.FileId)))
        {
            throw new ArgumentException("Every claimed task file must have exactly one Episode resolution.", nameof(fileResolutions));
        }

        var claimedFiles = claim.Files.ToDictionary(file => file.FileId, StringComparer.Ordinal);
        foreach (var resolution in fileResolutions)
        {
            var claimedFile = claimedFiles[resolution.FileId];
            var expectedSeasonNumber = claimedFile.TmdbSeasonNumber ?? claim.TmdbSeasonNumber;
            var resolvedEpisodeNumber = resolution.ResolvedEpisodeNumber;
            if (expectedSeasonNumber <= 0)
            {
                throw new ArgumentException("Task file TMDB Season identity is invalid.", nameof(fileResolutions));
            }

            if (resolution.Disposition is not ("episode" or "other"))
            {
                throw new ArgumentException("Episode resolution disposition must be episode or other.", nameof(fileResolutions));
            }

            if (resolution.Disposition == "episode")
            {
                if (resolution.ResolutionSource is null
                    || string.IsNullOrWhiteSpace(resolution.ResolutionAttemptId)
                    || resolvedEpisodeNumber is null or <= 0
                    || (resolution.Episode is not null
                        && (resolution.TrustedEpisodeNumber is not null
                            || resolution.Episode.SeriesId != claim.TmdbSeriesId
                            || resolution.Episode.SeasonNumber != expectedSeasonNumber))
                    || (resolution.Episode is null
                        && (!claim.EpisodeResolvedByTrustedOffset
                            || resolution.TrustedEpisodeNumber is null)))
                {
                    throw new ArgumentException("TMDB Episode identity is invalid.", nameof(fileResolutions));
                }

                ValidateIdentifier(
                    resolution.ResolutionAttemptId!,
                    nameof(fileResolutions));
            }
            else
            {
                if (resolution.Episode is not null
                    || resolution.TrustedEpisodeNumber is not null
                    || resolution.OtherReason is null
                    || resolution.ResolutionSource is not null
                    || resolution.ResolutionAttemptId is not null)
                {
                    throw new ArgumentException("Other resolution requires a reason and no TMDB Episode.", nameof(fileResolutions));
                }

                ValidateIdentifier(resolution.OtherReason, nameof(fileResolutions));
            }

            if (resolution.AssociatedFileId is not null
                && !claim.Files.Any(file => file.FileId == resolution.AssociatedFileId))
            {
                throw new ArgumentException("Associated subtitle target must belong to the same task.", nameof(fileResolutions));
            }

            if (resolution.RenameSuffix is not null
                && (resolution.RenameSuffix.Length is < 2 or > 128
                    || resolution.RenameSuffix[0] != '.'
                    || resolution.RenameSuffix.IndexOfAny(['/', '\\']) >= 0))
            {
                throw new ArgumentException("Subtitle rename suffix is invalid.", nameof(fileResolutions));
            }
        }

        var now = Format(utcNow);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        string seriesRowId;
        await using (var findSeries = connection.CreateCommand())
        {
            findSeries.Transaction = transaction;
            findSeries.CommandText = "SELECT id FROM anime_series WHERE tmdb_series_id = $tmdb_series_id;";
            findSeries.Parameters.AddWithValue("$tmdb_series_id", claim.TmdbSeriesId);
            seriesRowId = (string)(await findSeries.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Resolved TMDB Series projection was not found."));
        }

        var episodeClaims = new Dictionary<TmdbEpisodeIdentity, EpisodeClaimDecision>();
        foreach (var resolution in fileResolutions)
        {
            if (resolution.ResolvedEpisodeNumber is null)
            {
                continue;
            }

            var identity = new TmdbEpisodeIdentity(
                claim.TmdbSeriesId,
                claimedFiles[resolution.FileId].TmdbSeasonNumber ?? claim.TmdbSeasonNumber,
                resolution.ResolvedEpisodeNumber.Value);
            if (!episodeClaims.ContainsKey(identity))
            {
                episodeClaims.Add(
                    identity,
                    await ClaimEpisodeAsync(
                        connection,
                        transaction,
                        claim.Resolution.TaskId,
                        resolution.FileId,
                        identity,
                        now,
                        cancellationToken).ConfigureAwait(false));
            }
        }

        var duplicateHits = episodeClaims
            .Where(item => item.Value != EpisodeClaimDecision.Owned)
            .Select(item => new MetadataDuplicateHit(
                item.Key.SeriesId,
                item.Key.SeasonNumber,
                item.Key.EpisodeNumber,
                item.Value == EpisodeClaimDecision.AlreadyCompleted
                    ? "episode_already_completed"
                    : "episode_claimed_by_another_task"))
            .OrderBy(item => item.TmdbSeriesId)
            .ThenBy(item => item.TmdbSeasonNumber)
            .ThenBy(item => item.TmdbEpisodeNumber)
            .ToArray();

        foreach (var resolution in fileResolutions)
        {
            var claimedFile = claimedFiles[resolution.FileId];
            var expectedSeasonNumber = claimedFile.TmdbSeasonNumber ?? claim.TmdbSeasonNumber;
            if (resolution.Episode is not null)
            {
                await using var upsertEpisode = connection.CreateCommand();
                upsertEpisode.Transaction = transaction;
                upsertEpisode.CommandText = """
                    INSERT INTO tmdb_episodes (
                        tmdb_episode_id, series_id, season_number, episode_number,
                        name, air_date, runtime_minutes, fetched_at_utc)
                    VALUES (
                        $tmdb_episode_id, $series_id, $season_number, $episode_number,
                        $name, $air_date, NULL, $now)
                    ON CONFLICT(tmdb_episode_id) DO UPDATE SET
                        name = excluded.name,
                        air_date = excluded.air_date,
                        fetched_at_utc = excluded.fetched_at_utc;
                    """;
                upsertEpisode.Parameters.AddWithValue("$tmdb_episode_id", resolution.Episode.Id);
                upsertEpisode.Parameters.AddWithValue("$series_id", seriesRowId);
                upsertEpisode.Parameters.AddWithValue("$season_number", resolution.Episode.SeasonNumber);
                upsertEpisode.Parameters.AddWithValue("$episode_number", resolution.Episode.EpisodeNumber);
                upsertEpisode.Parameters.AddWithValue("$name", resolution.Episode.Name);
                upsertEpisode.Parameters.AddWithValue(
                    "$air_date",
                    resolution.Episode.AirDate is null
                        ? DBNull.Value
                        : resolution.Episode.AirDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                upsertEpisode.Parameters.AddWithValue("$now", now);
                await upsertEpisode.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            var disposition = resolution.Disposition;
            var otherReason = resolution.OtherReason;
            if (resolution.ResolvedEpisodeNumber is not null)
            {
                var identity = new TmdbEpisodeIdentity(
                    claim.TmdbSeriesId,
                    expectedSeasonNumber,
                    resolution.ResolvedEpisodeNumber.Value);
                var decision = episodeClaims[identity];
                if (decision != EpisodeClaimDecision.Owned)
                {
                    disposition = claim.IsOtherReadaptation ? "other" : "duplicate";
                    otherReason = decision == EpisodeClaimDecision.AlreadyCompleted
                        ? "episode_already_completed"
                        : "episode_claimed_by_another_task";
                }
            }

            await using var updateFile = connection.CreateCommand();
            updateFile.Transaction = transaction;
            updateFile.CommandText = """
                UPDATE task_files
                SET tmdb_episode_number = $tmdb_episode_number,
                    tmdb_episode_id = $tmdb_episode_id,
                    disposition = $disposition,
                    other_reason = $other_reason,
                    associated_task_file_id = $associated_file_id,
                    rename_suffix = $rename_suffix,
                    episode_resolution_source = $episode_resolution_source,
                    episode_resolution_run_id = CASE
                        WHEN $episode_resolution_source IS NULL THEN NULL
                        ELSE $run_id
                    END,
                    episode_resolution_attempt_id = CASE
                        WHEN $episode_resolution_source IS NULL THEN NULL
                        ELSE $episode_resolution_attempt_id
                    END
                WHERE id = $file_id AND task_id = $task_id
                  AND disposition = 'pending'
                  AND tmdb_series_id = $tmdb_series_id
                  AND tmdb_season_number = $tmdb_season_number;
                """;
            updateFile.Parameters.AddWithValue("$file_id", resolution.FileId);
            updateFile.Parameters.AddWithValue("$task_id", claim.Resolution.TaskId);
            updateFile.Parameters.AddWithValue(
                "$run_id",
                claim.Resolution.RunId);
            updateFile.Parameters.AddWithValue("$tmdb_series_id", claim.TmdbSeriesId);
            updateFile.Parameters.AddWithValue("$tmdb_season_number", expectedSeasonNumber);
            updateFile.Parameters.AddWithValue(
                "$tmdb_episode_number",
                (object?)resolution.ResolvedEpisodeNumber ?? DBNull.Value);
            updateFile.Parameters.AddWithValue("$tmdb_episode_id", (object?)resolution.Episode?.Id ?? DBNull.Value);
            updateFile.Parameters.AddWithValue("$disposition", disposition);
            updateFile.Parameters.AddWithValue("$other_reason", (object?)otherReason ?? DBNull.Value);
            updateFile.Parameters.AddWithValue("$associated_file_id", (object?)resolution.AssociatedFileId ?? DBNull.Value);
            updateFile.Parameters.AddWithValue("$rename_suffix", (object?)resolution.RenameSuffix ?? DBNull.Value);
            updateFile.Parameters.AddWithValue(
                "$episode_resolution_source",
                resolution.ResolutionSource is null
                    ? DBNull.Value
                    : resolution.ResolutionSource.Value.ToStorageValue());
            updateFile.Parameters.AddWithValue(
                "$episode_resolution_attempt_id",
                (object?)resolution.ResolutionAttemptId ?? DBNull.Value);
            if (await updateFile.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            {
                throw new InvalidOperationException("Metadata Episode task file changed concurrently.");
            }
        }

        await using (var finish = connection.CreateCommand())
        {
            finish.Transaction = transaction;
            finish.CommandText = """
                UPDATE metadata_resolution_runs
                SET status = 'resolved', tmdb_access_confirmed = 1,
                    failure_kind = NULL, fallback_eligible = 0,
                    fallback_denial_reason = NULL, completed_at_utc = $now,
                    lease_token = NULL, lease_expires_at_utc = NULL
                WHERE id = $run_id AND task_id = $task_id
                  AND status = 'running' AND lease_token = $lease_token;

                UPDATE ingest_tasks
                SET status = CASE
                        WHEN $is_other_readaptation = 1 THEN 'downloaded'
                        ELSE 'metadata_resolved'
                    END,
                    failure_kind = NULL,
                    failure_reason = NULL, updated_at_utc = $now
                WHERE id = $task_id AND status = 'metadata_episode_resolving';
                """;
            finish.Parameters.AddWithValue("$now", now);
            finish.Parameters.AddWithValue("$run_id", claim.Resolution.RunId);
            finish.Parameters.AddWithValue("$task_id", claim.Resolution.TaskId);
            finish.Parameters.AddWithValue("$lease_token", claim.Resolution.LeaseToken);
            finish.Parameters.AddWithValue("$is_other_readaptation", claim.IsOtherReadaptation ? 1 : 0);
            if (await finish.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 2)
            {
                throw new InvalidOperationException("Metadata Episode resolution lease is no longer active.");
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new MetadataEpisodeCompletionResult(duplicateHits);
    }

    private static async Task<EpisodeClaimDecision> ClaimEpisodeAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string taskId,
        string taskFileId,
        TmdbEpisodeIdentity episode,
        string claimedAtUtc,
        CancellationToken cancellationToken)
    {
        await using (var completed = connection.CreateCommand())
        {
            completed.Transaction = transaction;
            completed.CommandText = """
                SELECT EXISTS(
                    SELECT 1 FROM completion_records
                    WHERE tmdb_series_id = $series_id
                      AND tmdb_season_number = $season_number
                      AND tmdb_episode_number = $episode_number);
                """;
            completed.Parameters.AddWithValue("$series_id", episode.SeriesId);
            completed.Parameters.AddWithValue("$season_number", episode.SeasonNumber);
            completed.Parameters.AddWithValue("$episode_number", episode.EpisodeNumber);
            if (Convert.ToInt64(
                    await completed.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
                    CultureInfo.InvariantCulture) == 1)
            {
                return EpisodeClaimDecision.AlreadyCompleted;
            }
        }

        await using (var acquire = connection.CreateCommand())
        {
            acquire.Transaction = transaction;
            acquire.CommandText = """
                INSERT INTO episode_claims (
                    id, tmdb_series_id, tmdb_season_number, tmdb_episode_number,
                    task_file_id, state, claimed_at_utc, expires_at_utc)
                VALUES (
                    $id, $series_id, $season_number, $episode_number,
                    $task_file_id, 'active', $claimed_at_utc, NULL)
                ON CONFLICT(tmdb_series_id, tmdb_season_number, tmdb_episode_number)
                DO UPDATE SET
                    id = excluded.id,
                    task_file_id = excluded.task_file_id,
                    state = 'active',
                    claimed_at_utc = excluded.claimed_at_utc,
                    expires_at_utc = NULL
                WHERE episode_claims.state = 'released';
                """;
            acquire.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            acquire.Parameters.AddWithValue("$series_id", episode.SeriesId);
            acquire.Parameters.AddWithValue("$season_number", episode.SeasonNumber);
            acquire.Parameters.AddWithValue("$episode_number", episode.EpisodeNumber);
            acquire.Parameters.AddWithValue("$task_file_id", taskFileId);
            acquire.Parameters.AddWithValue("$claimed_at_utc", claimedAtUtc);
            if (await acquire.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1)
            {
                return EpisodeClaimDecision.Owned;
            }
        }

        await using var existing = connection.CreateCommand();
        existing.Transaction = transaction;
        existing.CommandText = """
            SELECT file.task_id, claim.state
            FROM episode_claims AS claim
            JOIN task_files AS file ON file.id = claim.task_file_id
            WHERE claim.tmdb_series_id = $series_id
              AND claim.tmdb_season_number = $season_number
              AND claim.tmdb_episode_number = $episode_number;
            """;
        existing.Parameters.AddWithValue("$series_id", episode.SeriesId);
        existing.Parameters.AddWithValue("$season_number", episode.SeasonNumber);
        existing.Parameters.AddWithValue("$episode_number", episode.EpisodeNumber);
        await using var reader = await existing.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("TMDB Episode claim conflict disappeared during the transaction.");
        }

        var ownerTaskId = reader.GetString(0);
        var state = reader.GetString(1);
        if (string.Equals(ownerTaskId, taskId, StringComparison.Ordinal) && state == "active")
        {
            return EpisodeClaimDecision.Owned;
        }

        return state == "completed"
            ? EpisodeClaimDecision.AlreadyCompleted
            : EpisodeClaimDecision.ClaimedByAnotherTask;
    }

    private static async Task<EpisodeClaimDecision> ClaimFallbackAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string taskId,
        string taskFileId,
        FallbackDedupScope scope,
        string claimedAtUtc,
        CancellationToken cancellationToken)
    {
        await using (var completed = connection.CreateCommand())
        {
            completed.Transaction = transaction;
            completed.CommandText = """
                SELECT EXISTS(
                    SELECT 1 FROM fallback_completion_records
                    WHERE scope_kind = $scope_kind AND scope_key = $scope_key);
                """;
            completed.Parameters.AddWithValue("$scope_kind", scope.Kind);
            completed.Parameters.AddWithValue("$scope_key", scope.Key);
            if (Convert.ToInt64(
                    await completed.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
                    CultureInfo.InvariantCulture) == 1)
            {
                return EpisodeClaimDecision.AlreadyCompleted;
            }
        }

        await using (var acquire = connection.CreateCommand())
        {
            acquire.Transaction = transaction;
            acquire.CommandText = """
                INSERT INTO fallback_claims (
                    id, scope_kind, scope_key, task_file_id,
                    state, claimed_at_utc, expires_at_utc)
                VALUES (
                    $id, $scope_kind, $scope_key, $task_file_id,
                    'active', $claimed_at_utc, NULL)
                ON CONFLICT(scope_kind, scope_key)
                DO UPDATE SET
                    id = excluded.id,
                    task_file_id = excluded.task_file_id,
                    state = 'active',
                    claimed_at_utc = excluded.claimed_at_utc,
                    expires_at_utc = NULL
                WHERE fallback_claims.state = 'released';
                """;
            acquire.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            acquire.Parameters.AddWithValue("$scope_kind", scope.Kind);
            acquire.Parameters.AddWithValue("$scope_key", scope.Key);
            acquire.Parameters.AddWithValue("$task_file_id", taskFileId);
            acquire.Parameters.AddWithValue("$claimed_at_utc", claimedAtUtc);
            if (await acquire.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1)
            {
                return EpisodeClaimDecision.Owned;
            }
        }

        await using var existing = connection.CreateCommand();
        existing.Transaction = transaction;
        existing.CommandText = """
            SELECT file.task_id, claim.state
            FROM fallback_claims AS claim
            JOIN task_files AS file ON file.id = claim.task_file_id
            WHERE claim.scope_kind = $scope_kind AND claim.scope_key = $scope_key;
            """;
        existing.Parameters.AddWithValue("$scope_kind", scope.Kind);
        existing.Parameters.AddWithValue("$scope_key", scope.Key);
        await using var reader = await existing.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Bangumi fallback claim conflict disappeared during the transaction.");
        }

        var ownerTaskId = reader.GetString(0);
        var state = reader.GetString(1);
        if (string.Equals(ownerTaskId, taskId, StringComparison.Ordinal) && state == "active")
        {
            return EpisodeClaimDecision.Owned;
        }

        return state == "completed"
            ? EpisodeClaimDecision.AlreadyCompleted
            : EpisodeClaimDecision.ClaimedByAnotherTask;
    }

    public async Task FailEpisodesAsync(
        MetadataEpisodeTaskClaim claim,
        MetadataFailure failure,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(failure);
        StableErrorCode.Require(failure.Code, nameof(failure.Code));
        var now = Format(utcNow);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE metadata_resolution_runs
            SET status = 'failed', tmdb_access_confirmed = $access_confirmed,
                failure_kind = $failure_kind, fallback_eligible = 0,
                fallback_denial_reason = 'tmdb_episode_validation_failed',
                completed_at_utc = $now, lease_token = NULL, lease_expires_at_utc = NULL
            WHERE id = $run_id AND task_id = $task_id
              AND status = 'running' AND lease_token = $lease_token;

            UPDATE ingest_tasks
            SET status = 'metadata_failed', failure_kind = $failure_kind,
                failure_reason = $failure_code, updated_at_utc = $now
            WHERE id = $task_id AND status = 'metadata_episode_resolving';
            """;
        command.Parameters.AddWithValue("$access_confirmed", failure.TmdbAccessConfirmed ? 1 : 0);
        command.Parameters.AddWithValue("$failure_kind", failure.Kind.ToString());
        command.Parameters.AddWithValue("$failure_code", failure.Code);
        command.Parameters.AddWithValue("$now", now);
        command.Parameters.AddWithValue("$run_id", claim.Resolution.RunId);
        command.Parameters.AddWithValue("$task_id", claim.Resolution.TaskId);
        command.Parameters.AddWithValue("$lease_token", claim.Resolution.LeaseToken);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 2)
        {
            throw new InvalidOperationException("Metadata Episode resolution lease is no longer active.");
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task FailAsync(
        MetadataTaskClaim claim,
        MetadataFailure failure,
        bool fallbackEligible,
        string fallbackDenialReason,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(failure);
        StableErrorCode.Require(failure.Code, nameof(failure.Code));
        StableErrorCode.Require(fallbackDenialReason, nameof(fallbackDenialReason));
        if (fallbackEligible
            && (failure.Kind != MetadataFailureKind.SemanticNoMatch || !failure.TmdbAccessConfirmed))
        {
            throw new ArgumentException("TMDB fallback requires authoritative SemanticNoMatch.", nameof(fallbackEligible));
        }

        var now = Format(utcNow);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE metadata_resolution_runs
            SET status = 'failed', tmdb_access_confirmed = $access_confirmed,
                failure_kind = $failure_kind, fallback_eligible = $fallback_eligible,
                fallback_denial_reason = $fallback_denial_reason,
                completed_at_utc = $now, lease_token = NULL, lease_expires_at_utc = NULL
            WHERE id = $run_id AND task_id = $task_id
              AND status = 'running' AND lease_token = $lease_token;

            UPDATE ingest_tasks
            SET status = 'metadata_failed', failure_kind = $failure_kind,
                failure_reason = $failure_code, updated_at_utc = $now
            WHERE id = $task_id AND status = 'metadata_resolving';
            """;
        command.Parameters.AddWithValue("$access_confirmed", failure.TmdbAccessConfirmed ? 1 : 0);
        command.Parameters.AddWithValue("$failure_kind", failure.Kind.ToString());
        command.Parameters.AddWithValue("$fallback_eligible", fallbackEligible ? 1 : 0);
        command.Parameters.AddWithValue("$fallback_denial_reason", fallbackDenialReason);
        command.Parameters.AddWithValue("$failure_code", failure.Code);
        command.Parameters.AddWithValue("$now", now);
        command.Parameters.AddWithValue("$run_id", claim.RunId);
        command.Parameters.AddWithValue("$task_id", claim.TaskId);
        command.Parameters.AddWithValue("$lease_token", claim.LeaseToken);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 2)
        {
            throw new InvalidOperationException("Metadata resolution lease is no longer active.");
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<MetadataRetryResult> RetryFailedAsync(
        string taskId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        var now = Format(utcNow);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        string? status = null;
        var hasActiveLease = false;
        await using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = """
                SELECT task.status, EXISTS (
                    SELECT 1
                    FROM metadata_resolution_runs AS run
                    WHERE run.task_id = task.id AND run.status = 'running')
                FROM ingest_tasks AS task
                WHERE task.id = $task_id;
                """;
            select.Parameters.AddWithValue("$task_id", taskId);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                status = reader.GetString(0);
                hasActiveLease = reader.GetInt64(1) != 0;
            }
        }

        if (status is null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return MetadataRetryResult.NotFound;
        }

        if (hasActiveLease)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return MetadataRetryResult.ActiveLease;
        }

        if (!string.Equals(status, "metadata_failed", StringComparison.Ordinal))
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return MetadataRetryResult.InvalidState;
        }

        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE ingest_tasks
            SET status = CASE WHEN EXISTS (
                    SELECT 1 FROM download_jobs
                    WHERE download_jobs.task_id = ingest_tasks.id
                      AND download_jobs.preparation_state IN ('pending', 'preparing'))
                THEN 'download_preparing' ELSE 'downloaded' END,
                failure_kind = NULL,
                failure_reason = NULL, updated_at_utc = $now
            WHERE id = $task_id AND status = 'metadata_failed'
              AND NOT EXISTS (
                SELECT 1
                FROM metadata_resolution_runs
                WHERE task_id = $task_id AND status = 'running');
            """;
        update.Parameters.AddWithValue("$task_id", taskId);
        update.Parameters.AddWithValue("$now", now);
        if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            throw new InvalidOperationException("Metadata task retry state changed concurrently.");
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return MetadataRetryResult.Retried;
    }

    public async Task<string?> GetTaskStatusAsync(
        string taskId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT status FROM ingest_tasks WHERE id = $task_id;";
        command.Parameters.AddWithValue("$task_id", taskId);
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
    }

    public async Task<MetadataRunProjection?> GetLatestAsync(
        string taskId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, task_id, status, attempt_number, tmdb_series_id,
                   tmdb_season_number, tmdb_access_confirmed, failure_kind,
                   fallback_eligible, fallback_denial_reason
            FROM metadata_resolution_runs
            WHERE task_id = $task_id
            ORDER BY attempt_number DESC LIMIT 1;
            """;
        command.Parameters.AddWithValue("$task_id", taskId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new MetadataRunProjection(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt32(3),
            reader.IsDBNull(4) ? null : reader.GetInt32(4),
            reader.IsDBNull(5) ? null : reader.GetInt32(5),
            reader.GetInt64(6) != 0,
            reader.IsDBNull(7) ? null : Enum.Parse<MetadataFailureKind>(reader.GetString(7), ignoreCase: false),
            reader.GetInt64(8) != 0,
            reader.IsDBNull(9) ? null : reader.GetString(9));
    }

    public async Task<IReadOnlyList<MetadataAttemptProjection>> ListAttemptsAsync(
        string taskId,
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 500);

        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT attempt.id, run.id, run.attempt_number, run.status,
                   attempt.stage, attempt.strategy, attempt.priority, attempt.result,
                   attempt.error_code, attempt.reason, attempt.retryable,
                   attempt.attempt_number, attempt.duration_ms, attempt.created_at_utc,
                   run.started_at_utc, run.completed_at_utc,
                   attempt.ai_model, attempt.ai_prompt_tokens,
                   attempt.ai_completion_tokens, attempt.ai_total_tokens,
                   attempt.ai_request_count, attempt.ai_tool_call_count
            FROM metadata_resolution_attempts AS attempt
            JOIN metadata_resolution_runs AS run ON run.id = attempt.run_id
            WHERE run.task_id = $task_id
            ORDER BY attempt.created_at_utc DESC, attempt.id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$task_id", taskId.Trim());
        command.Parameters.AddWithValue("$limit", limit);
        var attempts = new List<MetadataAttemptProjection>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            attempts.Add(new MetadataAttemptProjection(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetInt32(6),
                reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.GetInt64(10) != 0,
                reader.GetInt32(11),
                reader.GetInt64(12),
                DateTimeOffset.Parse(
                    reader.GetString(13),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind),
                DateTimeOffset.Parse(
                    reader.GetString(14),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind),
                reader.IsDBNull(15)
                    ? null
                    : DateTimeOffset.Parse(
                        reader.GetString(15),
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind),
                ReadAiUsage(reader, 16)));
        }

        return attempts;
    }

    public async Task<MetadataAiInvocationLogPage> ListAiInvocationLogsAsync(
        MetadataAiInvocationLogFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentOutOfRangeException.ThrowIfLessThan(filter.Page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(filter.PageSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(filter.PageSize, 100);

        var search = NormalizeLogFilter(filter.Search);
        var stage = NormalizeLogFilter(filter.Stage);
        var result = NormalizeLogFilter(filter.Result);
        var model = NormalizeLogFilter(filter.Model);
        var errorCategory = NormalizeLogFilter(filter.ErrorCategory);
        var fromUtc = filter.FromUtc?.ToUniversalTime();
        var toUtc = filter.ToUtc?.ToUniversalTime();
        if (fromUtc is not null && toUtc is not null && fromUtc > toUtc)
        {
            throw new ArgumentException("AI invocation log time range is invalid.", nameof(filter));
        }

        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var summaryCommand = connection.CreateCommand();
        summaryCommand.CommandText = AiInvocationLogFilterSql + """
            SELECT COUNT(*),
                   COALESCE(SUM(CASE WHEN result = 'matched' THEN 1 ELSE 0 END), 0),
                   COALESCE(SUM(CASE WHEN result <> 'matched' THEN 1 ELSE 0 END), 0),
                   COALESCE(SUM(CASE WHEN error_category = 'output_format' THEN 1 ELSE 0 END), 0),
                   COALESCE(SUM(ai_prompt_tokens), 0),
                   COALESCE(SUM(ai_completion_tokens), 0),
                   COALESCE(SUM(ai_total_tokens), 0),
                   COALESCE(SUM(ai_request_count), 0),
                   COALESCE(SUM(ai_tool_call_count), 0)
            FROM filtered;
            """;
        AddAiInvocationLogParameters(
            summaryCommand, search, stage, result, model, errorCategory, fromUtc, toUtc);
        MetadataAiInvocationLogSummary summary;
        await using (var reader = await summaryCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            summary = new MetadataAiInvocationLogSummary(
                checked((int)reader.GetInt64(0)),
                checked((int)reader.GetInt64(1)),
                checked((int)reader.GetInt64(2)),
                checked((int)reader.GetInt64(3)),
                reader.GetInt64(4),
                reader.GetInt64(5),
                reader.GetInt64(6),
                reader.GetInt64(7),
                reader.GetInt64(8));
        }

        await using var itemCommand = connection.CreateCommand();
        itemCommand.CommandText = AiInvocationLogFilterSql + """
            SELECT attempt_id, run_id, task_id, title, source_id, mikanid,
                   bangumi_subject_id, tmdb_series_id, tmdb_season_number,
                   run_status, stage, strategy, result, error_code, error_category,
                   reason,
                   retryable, duration_ms, created_at_utc, ai_model,
                   ai_prompt_tokens, ai_completion_tokens, ai_total_tokens,
                   ai_request_count, ai_tool_call_count
            FROM filtered
            ORDER BY created_at_utc DESC, attempt_id DESC
            LIMIT $page_size OFFSET $offset;
            """;
        AddAiInvocationLogParameters(
            itemCommand, search, stage, result, model, errorCategory, fromUtc, toUtc);
        itemCommand.Parameters.AddWithValue("$page_size", filter.PageSize);
        itemCommand.Parameters.AddWithValue("$offset", checked((filter.Page - 1) * filter.PageSize));
        var items = new List<MetadataAiInvocationLogProjection>();
        await using (var reader = await itemCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                items.Add(new MetadataAiInvocationLogProjection(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetInt32(5),
                    reader.IsDBNull(6) ? null : reader.GetInt32(6),
                    reader.IsDBNull(7) ? null : reader.GetInt32(7),
                    reader.IsDBNull(8) ? null : reader.GetInt32(8),
                    reader.GetString(9),
                    reader.GetString(10),
                    reader.GetString(11),
                    reader.GetString(12),
                    reader.IsDBNull(13) ? null : reader.GetString(13),
                    reader.GetString(14),
                    reader.IsDBNull(15) ? null : reader.GetString(15),
                    reader.GetInt64(16) != 0,
                    reader.GetInt64(17),
                    DateTimeOffset.Parse(
                        reader.GetString(18),
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind),
                    new AiMetadataProviderUsage(
                        reader.GetString(19),
                        reader.IsDBNull(20) ? null : reader.GetInt64(20),
                        reader.IsDBNull(21) ? null : reader.GetInt64(21),
                        reader.IsDBNull(22) ? null : reader.GetInt64(22),
                        reader.IsDBNull(23) ? 0 : reader.GetInt32(23),
                        reader.IsDBNull(24) ? 0 : reader.GetInt32(24))));
            }
        }

        return new MetadataAiInvocationLogPage(
            filter with
            {
                Search = search,
                Stage = stage,
                Result = result,
                Model = model,
                ErrorCategory = errorCategory,
                FromUtc = fromUtc,
                ToUtc = toUtc,
            },
            summary,
            items);
    }

    private const string AiInvocationLogFilterSql = """
        WITH classified AS (
            SELECT attempt.id AS attempt_id, run.id AS run_id, task.id AS task_id,
                   task.title, task.source_id, task.mikanid, task.bangumi_subject_id,
                   run.tmdb_series_id, run.tmdb_season_number, run.status AS run_status,
                   attempt.stage, attempt.strategy, attempt.result,
                   attempt.error_code, attempt.reason, attempt.retryable,
                   attempt.duration_ms, attempt.created_at_utc, attempt.ai_model,
                   attempt.ai_prompt_tokens, attempt.ai_completion_tokens,
                   attempt.ai_total_tokens, attempt.ai_request_count,
                   attempt.ai_tool_call_count,
                   CASE
                       WHEN attempt.error_code IS NULL THEN 'none'
                       WHEN attempt.error_code IN (
                           'ai_response_json_invalid',
                           'ai_result_json_invalid',
                           'ai_result_not_object',
                           'ai_result_empty',
                           'ai_response_invalid',
                           'ai_chat_response_invalid',
                           'ai_chat_response_ambiguous',
                           'ai_responses_response_invalid',
                           'ai_response_content_missing',
                           'ai_legacy_result_field',
                           'ai_metadata_response_incomplete',
                           'ai_metadata_match_invalid',
                           'ai_metadata_no_match_reason_missing',
                           'ai_episode_match_invalid',
                           'ai_file_count_mismatch',
                           'ai_file_identity_mismatch',
                           'ai_file_resolution_incomplete',
                           'ai_other_resolution_invalid',
                           'ai_other_season_missing'
                       ) THEN 'output_format'
                       ELSE 'other'
                   END AS error_category
            FROM metadata_resolution_attempts AS attempt
            JOIN metadata_resolution_runs AS run ON run.id = attempt.run_id
            JOIN ingest_tasks AS task ON task.id = run.task_id
            WHERE attempt.ai_model IS NOT NULL
        ), filtered AS (
            SELECT *
            FROM classified
            WHERE ($stage IS NULL OR stage = $stage)
              AND ($result IS NULL OR result = $result)
              AND ($model IS NULL OR instr(lower(ai_model), $model) > 0)
              AND ($error_category IS NULL OR error_category = $error_category)
              AND ($from_utc IS NULL OR created_at_utc >= $from_utc)
              AND ($to_utc IS NULL OR created_at_utc <= $to_utc)
              AND ($search IS NULL
                   OR instr(lower(title), $search) > 0
                   OR instr(lower(task_id), $search) > 0
                   OR instr(lower(source_id), $search) > 0
                   OR instr(lower(strategy), $search) > 0
                   OR instr(lower(COALESCE(error_code, '')), $search) > 0
                   OR instr(lower(COALESCE(reason, '')), $search) > 0)
        )
        """;

    private static void AddAiInvocationLogParameters(
        SqliteCommand command,
        string? search,
        string? stage,
        string? result,
        string? model,
        string? errorCategory,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc)
    {
        command.Parameters.AddWithValue("$search", (object?)search ?? DBNull.Value);
        command.Parameters.AddWithValue("$stage", (object?)stage ?? DBNull.Value);
        command.Parameters.AddWithValue("$result", (object?)result ?? DBNull.Value);
        command.Parameters.AddWithValue("$model", (object?)model ?? DBNull.Value);
        command.Parameters.AddWithValue("$error_category", (object?)errorCategory ?? DBNull.Value);
        command.Parameters.AddWithValue("$from_utc", fromUtc is null ? DBNull.Value : Format(fromUtc.Value));
        command.Parameters.AddWithValue("$to_utc", toUtc is null ? DBNull.Value : Format(toUtc.Value));
    }

    private static string? NormalizeLogFilter(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    public async Task<MetadataTaskAttentionSummary> GetTaskAttentionSummaryAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                COALESCE(SUM(CASE WHEN EXISTS (
                    SELECT 1 FROM task_files AS file
                    WHERE file.task_id = task.id AND file.disposition = 'other'
                ) THEN 1 ELSE 0 END), 0),
                COALESCE(SUM(CASE WHEN task.status = 'metadata_failed'
                    THEN 1 ELSE 0 END), 0),
                COALESCE(SUM(CASE WHEN task.readaptation_review_state = 'pending'
                    THEN 1 ELSE 0 END), 0)
            FROM ingest_tasks AS task;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return new MetadataTaskAttentionSummary(0, 0, 0);
        }

        return new MetadataTaskAttentionSummary(
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.GetInt32(2));
    }

    public async Task<IReadOnlyList<MetadataTaskListProjection>> ListTasksAsync(
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 500);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT task.id, task.title, task.source_id, task.status,
                   task.mikanid, task.bangumi_subject_id,
                   (SELECT run.tmdb_series_id
                    FROM metadata_resolution_runs AS run
                    WHERE run.task_id = task.id
                      AND run.series_resolution_source IS NOT NULL
                    ORDER BY COALESCE(run.completed_at_utc, run.started_at_utc) DESC,
                             run.id DESC LIMIT 1),
                   (SELECT run.tmdb_season_number
                    FROM metadata_resolution_runs AS run
                    WHERE run.task_id = task.id
                      AND run.season_resolution_source IS NOT NULL
                    ORDER BY COALESCE(run.completed_at_utc, run.started_at_utc) DESC,
                             run.id DESC LIMIT 1),
                   (SELECT run.series_resolution_source
                    FROM metadata_resolution_runs AS run
                    WHERE run.task_id = task.id
                      AND run.series_resolution_source IS NOT NULL
                    ORDER BY COALESCE(run.completed_at_utc, run.started_at_utc) DESC,
                             run.id DESC LIMIT 1),
                   (SELECT run.id
                    FROM metadata_resolution_runs AS run
                    WHERE run.task_id = task.id
                      AND run.series_resolution_source IS NOT NULL
                    ORDER BY COALESCE(run.completed_at_utc, run.started_at_utc) DESC,
                             run.id DESC LIMIT 1),
                   (SELECT run.series_resolution_attempt_id
                    FROM metadata_resolution_runs AS run
                    WHERE run.task_id = task.id
                      AND run.series_resolution_source IS NOT NULL
                    ORDER BY COALESCE(run.completed_at_utc, run.started_at_utc) DESC,
                             run.id DESC LIMIT 1),
                   (SELECT run.season_resolution_source
                    FROM metadata_resolution_runs AS run
                    WHERE run.task_id = task.id
                      AND run.season_resolution_source IS NOT NULL
                    ORDER BY COALESCE(run.completed_at_utc, run.started_at_utc) DESC,
                             run.id DESC LIMIT 1),
                   (SELECT run.id
                    FROM metadata_resolution_runs AS run
                    WHERE run.task_id = task.id
                      AND run.season_resolution_source IS NOT NULL
                    ORDER BY COALESCE(run.completed_at_utc, run.started_at_utc) DESC,
                             run.id DESC LIMIT 1),
                   (SELECT run.season_resolution_attempt_id
                    FROM metadata_resolution_runs AS run
                    WHERE run.task_id = task.id
                      AND run.season_resolution_source IS NOT NULL
                    ORDER BY COALESCE(run.completed_at_utc, run.started_at_utc) DESC,
                             run.id DESC LIMIT 1),
                   (SELECT CASE
                        WHEN COUNT(DISTINCT (
                            file_evidence.episode_resolution_source || ':' ||
                            file_evidence.episode_resolution_run_id || ':' ||
                            file_evidence.episode_resolution_attempt_id)) = 1
                            THEN MIN(file_evidence.episode_resolution_source)
                        WHEN COUNT(file_evidence.episode_resolution_source) > 0
                            THEN 'mixed'
                        ELSE NULL
                    END
                    FROM task_files AS file_evidence
                    WHERE file_evidence.task_id = task.id),
                   (SELECT CASE
                        WHEN COUNT(DISTINCT (
                            file_evidence.episode_resolution_source || ':' ||
                            file_evidence.episode_resolution_run_id || ':' ||
                            file_evidence.episode_resolution_attempt_id)) = 1
                            THEN MIN(file_evidence.episode_resolution_run_id)
                        ELSE NULL
                    END
                    FROM task_files AS file_evidence
                    WHERE file_evidence.task_id = task.id),
                   (SELECT CASE
                        WHEN COUNT(DISTINCT (
                            file_evidence.episode_resolution_source || ':' ||
                            file_evidence.episode_resolution_run_id || ':' ||
                            file_evidence.episode_resolution_attempt_id)) = 1
                            THEN MIN(file_evidence.episode_resolution_attempt_id)
                        ELSE NULL
                    END
                    FROM task_files AS file_evidence
                    WHERE file_evidence.task_id = task.id),
                   (SELECT COUNT(DISTINCT (
                        file_evidence.episode_resolution_source || ':' ||
                        file_evidence.episode_resolution_run_id || ':' ||
                        file_evidence.episode_resolution_attempt_id)) > 1
                    FROM task_files AS file_evidence
                    WHERE file_evidence.task_id = task.id),
                   task.failure_kind, task.failure_reason,
                   (SELECT attempt.stage
                    FROM metadata_resolution_attempts AS attempt
                    JOIN metadata_resolution_runs AS run ON run.id = attempt.run_id
                    WHERE run.task_id = task.id AND attempt.result = 'failed'
                    ORDER BY attempt.created_at_utc DESC, attempt.id DESC LIMIT 1),
                   (SELECT attempt.error_code
                    FROM metadata_resolution_attempts AS attempt
                    JOIN metadata_resolution_runs AS run ON run.id = attempt.run_id
                    WHERE run.task_id = task.id AND attempt.result = 'failed'
                    ORDER BY attempt.created_at_utc DESC, attempt.id DESC LIMIT 1),
                   (SELECT attempt.retryable
                    FROM metadata_resolution_attempts AS attempt
                    JOIN metadata_resolution_runs AS run ON run.id = attempt.run_id
                    WHERE run.task_id = task.id AND attempt.result = 'failed'
                    ORDER BY attempt.created_at_utc DESC, attempt.id DESC LIMIT 1),
                   (SELECT run.status
                    FROM metadata_resolution_runs AS run
                    WHERE run.task_id = task.id
                    ORDER BY run.attempt_number DESC, run.id DESC LIMIT 1),
                   (SELECT run.tmdb_access_confirmed
                    FROM metadata_resolution_runs AS run
                    WHERE run.task_id = task.id
                    ORDER BY run.attempt_number DESC, run.id DESC LIMIT 1),
                   (SELECT run.fallback_eligible
                    FROM metadata_resolution_runs AS run
                    WHERE run.task_id = task.id
                    ORDER BY run.attempt_number DESC, run.id DESC LIMIT 1),
                   (SELECT run.fallback_denial_reason
                    FROM metadata_resolution_runs AS run
                    WHERE run.task_id = task.id
                    ORDER BY run.attempt_number DESC, run.id DESC LIMIT 1),
                   SUM(CASE WHEN file.disposition = 'episode' THEN 1 ELSE 0 END),
                   SUM(CASE WHEN file.disposition = 'other' THEN 1 ELSE 0 END),
                   SUM(CASE WHEN file.disposition = 'duplicate' THEN 1 ELSE 0 END),
                   SUM(CASE WHEN file.disposition = 'pending' THEN 1 ELSE 0 END),
                   task.updated_at_utc, task.readaptation_review_state
            FROM ingest_tasks AS task
            LEFT JOIN task_files AS file ON file.task_id = task.id
            GROUP BY task.id
            ORDER BY task.updated_at_utc DESC, task.id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);
        var items = new List<MetadataTaskListProjection>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            items.Add(ReadTaskListProjection(reader));
        }

        return items;
    }

    public async Task<MetadataTaskDetailProjection?> GetTaskDetailAsync(
        string taskId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        MetadataTaskListProjection? summary;
        MetadataTaskSourceProjection source;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT task.id, task.title, task.source_id, task.status,
                       task.mikanid, task.bangumi_subject_id,
                       (SELECT run.tmdb_series_id
                        FROM metadata_resolution_runs AS run
                        WHERE run.task_id = task.id
                          AND run.series_resolution_source IS NOT NULL
                        ORDER BY COALESCE(run.completed_at_utc, run.started_at_utc) DESC,
                                 run.id DESC LIMIT 1),
                       (SELECT run.tmdb_season_number
                        FROM metadata_resolution_runs AS run
                        WHERE run.task_id = task.id
                          AND run.season_resolution_source IS NOT NULL
                        ORDER BY COALESCE(run.completed_at_utc, run.started_at_utc) DESC,
                                 run.id DESC LIMIT 1),
                       (SELECT run.series_resolution_source
                        FROM metadata_resolution_runs AS run
                        WHERE run.task_id = task.id
                          AND run.series_resolution_source IS NOT NULL
                        ORDER BY COALESCE(run.completed_at_utc, run.started_at_utc) DESC,
                                 run.id DESC LIMIT 1),
                       (SELECT run.id
                        FROM metadata_resolution_runs AS run
                        WHERE run.task_id = task.id
                          AND run.series_resolution_source IS NOT NULL
                        ORDER BY COALESCE(run.completed_at_utc, run.started_at_utc) DESC,
                                 run.id DESC LIMIT 1),
                       (SELECT run.series_resolution_attempt_id
                        FROM metadata_resolution_runs AS run
                        WHERE run.task_id = task.id
                          AND run.series_resolution_source IS NOT NULL
                        ORDER BY COALESCE(run.completed_at_utc, run.started_at_utc) DESC,
                                 run.id DESC LIMIT 1),
                       (SELECT run.season_resolution_source
                        FROM metadata_resolution_runs AS run
                        WHERE run.task_id = task.id
                          AND run.season_resolution_source IS NOT NULL
                        ORDER BY COALESCE(run.completed_at_utc, run.started_at_utc) DESC,
                                 run.id DESC LIMIT 1),
                       (SELECT run.id
                        FROM metadata_resolution_runs AS run
                        WHERE run.task_id = task.id
                          AND run.season_resolution_source IS NOT NULL
                        ORDER BY COALESCE(run.completed_at_utc, run.started_at_utc) DESC,
                                 run.id DESC LIMIT 1),
                       (SELECT run.season_resolution_attempt_id
                        FROM metadata_resolution_runs AS run
                        WHERE run.task_id = task.id
                          AND run.season_resolution_source IS NOT NULL
                        ORDER BY COALESCE(run.completed_at_utc, run.started_at_utc) DESC,
                                 run.id DESC LIMIT 1),
                       (SELECT CASE
                            WHEN COUNT(DISTINCT (
                                file_evidence.episode_resolution_source || ':' ||
                                file_evidence.episode_resolution_run_id || ':' ||
                                file_evidence.episode_resolution_attempt_id)) = 1
                                THEN MIN(file_evidence.episode_resolution_source)
                            WHEN COUNT(file_evidence.episode_resolution_source) > 0
                                THEN 'mixed'
                            ELSE NULL
                        END
                        FROM task_files AS file_evidence
                        WHERE file_evidence.task_id = task.id),
                       (SELECT CASE
                            WHEN COUNT(DISTINCT (
                                file_evidence.episode_resolution_source || ':' ||
                                file_evidence.episode_resolution_run_id || ':' ||
                                file_evidence.episode_resolution_attempt_id)) = 1
                                THEN MIN(file_evidence.episode_resolution_run_id)
                            ELSE NULL
                        END
                        FROM task_files AS file_evidence
                        WHERE file_evidence.task_id = task.id),
                       (SELECT CASE
                            WHEN COUNT(DISTINCT (
                                file_evidence.episode_resolution_source || ':' ||
                                file_evidence.episode_resolution_run_id || ':' ||
                                file_evidence.episode_resolution_attempt_id)) = 1
                                THEN MIN(file_evidence.episode_resolution_attempt_id)
                            ELSE NULL
                        END
                        FROM task_files AS file_evidence
                        WHERE file_evidence.task_id = task.id),
                       (SELECT COUNT(DISTINCT (
                            file_evidence.episode_resolution_source || ':' ||
                            file_evidence.episode_resolution_run_id || ':' ||
                            file_evidence.episode_resolution_attempt_id)) > 1
                        FROM task_files AS file_evidence
                        WHERE file_evidence.task_id = task.id),
                       task.failure_kind, task.failure_reason,
                       (SELECT attempt.stage
                        FROM metadata_resolution_attempts AS attempt
                        JOIN metadata_resolution_runs AS run ON run.id = attempt.run_id
                        WHERE run.task_id = task.id AND attempt.result = 'failed'
                        ORDER BY attempt.created_at_utc DESC, attempt.id DESC LIMIT 1),
                       (SELECT attempt.error_code
                        FROM metadata_resolution_attempts AS attempt
                        JOIN metadata_resolution_runs AS run ON run.id = attempt.run_id
                        WHERE run.task_id = task.id AND attempt.result = 'failed'
                        ORDER BY attempt.created_at_utc DESC, attempt.id DESC LIMIT 1),
                       (SELECT attempt.retryable
                        FROM metadata_resolution_attempts AS attempt
                        JOIN metadata_resolution_runs AS run ON run.id = attempt.run_id
                        WHERE run.task_id = task.id AND attempt.result = 'failed'
                        ORDER BY attempt.created_at_utc DESC, attempt.id DESC LIMIT 1),
                       (SELECT run.status
                        FROM metadata_resolution_runs AS run
                        WHERE run.task_id = task.id
                        ORDER BY run.attempt_number DESC, run.id DESC LIMIT 1),
                       (SELECT run.tmdb_access_confirmed
                        FROM metadata_resolution_runs AS run
                        WHERE run.task_id = task.id
                        ORDER BY run.attempt_number DESC, run.id DESC LIMIT 1),
                       (SELECT run.fallback_eligible
                        FROM metadata_resolution_runs AS run
                        WHERE run.task_id = task.id
                        ORDER BY run.attempt_number DESC, run.id DESC LIMIT 1),
                       (SELECT run.fallback_denial_reason
                        FROM metadata_resolution_runs AS run
                        WHERE run.task_id = task.id
                        ORDER BY run.attempt_number DESC, run.id DESC LIMIT 1),
                       SUM(CASE WHEN file.disposition = 'episode' THEN 1 ELSE 0 END),
                       SUM(CASE WHEN file.disposition = 'other' THEN 1 ELSE 0 END),
                       SUM(CASE WHEN file.disposition = 'duplicate' THEN 1 ELSE 0 END),
                       SUM(CASE WHEN file.disposition = 'pending' THEN 1 ELSE 0 END),
                       task.updated_at_utc, task.readaptation_review_state,
                       task.source_profile_id, task.source_profile_revision,
                       task.source_item_id, task.source_work_id, task.groupid,
                       task.anidb_id, task.imdb_id,
                       task.source_published_at_raw IS NOT NULL,
                       task.source_published_at
                FROM ingest_tasks AS task
                LEFT JOIN task_files AS file ON file.task_id = task.id
                WHERE task.id = $task_id
                GROUP BY task.id;
                """;
            command.Parameters.AddWithValue("$task_id", taskId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            summary = ReadTaskListProjection(reader);
            var sourceId = summary.SourceId;
            source = new MetadataTaskSourceProjection(
                reader.GetString(33),
                reader.GetInt64(34),
                sourceId,
                summary.Title,
                reader.IsDBNull(35)
                    ? null
                    : FingerprintSourceIdentifier(sourceId, "item", reader.GetString(35)),
                reader.IsDBNull(36)
                    ? null
                    : FingerprintSourceIdentifier(sourceId, "work", reader.GetString(36)),
                summary.MikanId,
                reader.IsDBNull(37) ? null : reader.GetInt32(37),
                summary.BangumiSubjectId,
                reader.IsDBNull(38) ? null : reader.GetInt32(38),
                reader.IsDBNull(39) ? null : reader.GetString(39),
                reader.GetInt64(40) != 0,
                reader.IsDBNull(41)
                    ? null
                    : DateTimeOffset.Parse(
                        reader.GetString(41),
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind));
        }

        MetadataTaskAiProjection? ai = null;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT attempt.stage, attempt.result, attempt.error_code, attempt.reason,
                       attempt.duration_ms, attempt.created_at_utc,
                       attempt.ai_model, attempt.ai_prompt_tokens,
                       attempt.ai_completion_tokens, attempt.ai_total_tokens,
                       attempt.ai_request_count, attempt.ai_tool_call_count
                FROM metadata_resolution_attempts AS attempt
                JOIN metadata_resolution_runs AS run ON run.id = attempt.run_id
                WHERE run.task_id = $task_id
                  AND attempt.strategy = 'ai_metadata'
                ORDER BY attempt.created_at_utc DESC, attempt.id DESC
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$task_id", taskId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                ai = new MetadataTaskAiProjection(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.GetInt64(4),
                    DateTimeOffset.Parse(
                        reader.GetString(5),
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind),
                    ReadAiUsage(reader, 6));
            }
        }

        var files = new List<MetadataTaskFileDetailProjection>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT file.relative_path, file.size_bytes, file.source_episode,
                       file.file_episode_candidate, file.disposition, file.other_reason,
                       file.tmdb_series_id,
                       COALESCE(NULLIF(series.canonical_name, ''), NULLIF(series.original_name, '')),
                       file.tmdb_season_number, season.canonical_name,
                       file.tmdb_episode_number, episode.name,
                       file.episode_resolution_source,
                       file.episode_resolution_run_id,
                       file.episode_resolution_attempt_id
                FROM task_files AS file
                LEFT JOIN anime_series AS series
                  ON series.tmdb_series_id = file.tmdb_series_id
                LEFT JOIN anime_seasons AS season
                  ON season.series_id = series.id
                 AND season.season_number = file.tmdb_season_number
                LEFT JOIN tmdb_episodes AS episode
                  ON episode.tmdb_episode_id = file.tmdb_episode_id
                WHERE file.task_id = $task_id
                ORDER BY file.relative_path COLLATE NOCASE, file.id;
                """;
            command.Parameters.AddWithValue("$task_id", taskId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                files.Add(new MetadataTaskFileDetailProjection(
                    reader.GetString(0),
                    reader.GetInt64(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetInt32(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    reader.IsDBNull(8) ? null : reader.GetInt32(8),
                    reader.IsDBNull(9) ? null : reader.GetString(9),
                    reader.IsDBNull(10) ? null : reader.GetInt32(10),
                    reader.IsDBNull(11) ? null : reader.GetString(11),
                    ReadResolutionEvidence(reader, 12, 13, 14)));
            }
        }

        return new MetadataTaskDetailProjection(summary, source, ai, files);
    }

    private static string FingerprintSourceIdentifier(
        string sourceId,
        string kind,
        string value) =>
        StableHash.Sha256LowerHex($"animegonet-source-id\0{sourceId}\0{kind}\0{value}");

    private static MetadataTaskListProjection ReadTaskListProjection(SqliteDataReader reader)
    {
        var status = reader.GetString(3);
        var seriesResolution = ReadResolutionEvidence(reader, 8, 9, 10);
        var seasonResolution = ReadResolutionEvidence(reader, 11, 12, 13);
        var episodeResolution = ReadResolutionEvidence(reader, 14, 15, 16);
        var episodeResolutionMixed = reader.GetInt64(17) != 0;
        var failureKind = reader.IsDBNull(18) ? null : reader.GetString(18);
        var failureStage = reader.IsDBNull(20) ? null : reader.GetString(20);
        var failureCode = reader.IsDBNull(21) ? null : reader.GetString(21);
        bool? failureRetryable = reader.IsDBNull(22)
            ? null
            : reader.GetInt64(22) != 0;
        return new MetadataTaskListProjection(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            status,
            reader.IsDBNull(4) ? null : reader.GetInt32(4),
            reader.IsDBNull(5) ? null : reader.GetInt32(5),
            reader.IsDBNull(6) ? null : reader.GetInt32(6),
            reader.IsDBNull(7) ? null : reader.GetInt32(7),
            seriesResolution?.Strategy,
            seasonResolution?.Strategy,
            episodeResolutionMixed
                ? "mixed"
                : episodeResolution?.Strategy,
            failureKind,
            reader.IsDBNull(19) ? null : reader.GetString(19),
            failureStage,
            failureCode,
            failureRetryable,
            reader.IsDBNull(23) ? null : reader.GetString(23),
            reader.IsDBNull(24) ? null : reader.GetInt64(24) != 0,
            reader.IsDBNull(25) ? null : reader.GetInt64(25) != 0,
            reader.IsDBNull(26) ? null : reader.GetString(26),
            ClassifyHandling(status, failureKind, failureRetryable),
            reader.GetInt32(27),
            reader.GetInt32(28),
            reader.GetInt32(29),
            reader.GetInt32(30),
            DateTimeOffset.Parse(
                reader.GetString(31),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind),
            reader.GetString(32),
            seriesResolution,
            seasonResolution,
            episodeResolution,
            episodeResolutionMixed);
    }

    private static TmdbResolutionEvidence? ReadResolutionEvidence(
        SqliteDataReader reader,
        int sourceIndex,
        int runIndex,
        int attemptIndex)
    {
        if (reader.IsDBNull(sourceIndex)
            || reader.IsDBNull(runIndex)
            || reader.IsDBNull(attemptIndex))
        {
            return null;
        }

        return new TmdbResolutionEvidence(
            reader.GetString(sourceIndex).ParseTmdbResolutionSource(),
            reader.GetString(runIndex),
            reader.GetString(attemptIndex));
    }

    private static string ClassifyHandling(
        string status,
        string? failureKind,
        bool? failureRetryable)
    {
        if (string.Equals(failureKind, "tmdb_completion_pending", StringComparison.Ordinal))
        {
            return "fallback";
        }

        if (status is "download_skipped_duplicate")
        {
            return "skipped";
        }

        if (status is "metadata_resolving" or "metadata_episode_resolving")
        {
            return "active";
        }

        if (status == "metadata_failed")
        {
            if (failureKind is "Authentication" or "Configuration" or "InvalidInput")
            {
                return "configuration";
            }

            return failureRetryable == true ? "explicit_retry" : "manual";
        }

        return status is "metadata_resolved" or "metadata_season_resolved"
            ? "resolved"
            : "other";
    }

    public async Task<MikanWorkImpactProjection> GetMikanWorkImpactAsync(
        int mikanId,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(mikanId, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 500);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        await using (var summary = connection.CreateCommand())
        {
            summary.CommandText = """
                SELECT category, COUNT(*)
                FROM (
                    SELECT CASE
                        WHEN task.status IN ('organizing_cleanup', 'organized')
                          OR job.organization_state IN ('organizing', 'cleanup', 'completed')
                            THEN 'completed_protected'
                        WHEN EXISTS (
                            SELECT 1 FROM metadata_resolution_runs AS active_run
                            WHERE active_run.task_id = task.id AND active_run.status = 'running')
                          OR task.status IN ('metadata_resolving', 'metadata_episode_resolving')
                            THEN 'active'
                        WHEN task.status = 'metadata_failed'
                            THEN 'retryable_failed'
                        WHEN task.status IN (
                            'received', 'staged', 'dispatching',
                            'download_preparing', 'download_queued',
                            'downloading', 'downloaded')
                            THEN 'future'
                        WHEN task.status IN ('metadata_season_resolved', 'metadata_resolved')
                          OR job.organization_state = 'pending'
                            THEN 'resolved_protected'
                        ELSE 'other'
                    END AS category
                    FROM ingest_tasks AS task
                    LEFT JOIN download_jobs AS job ON job.task_id = task.id
                    WHERE task.mikanid = $mikanid
                )
                GROUP BY category;
                """;
            summary.Parameters.AddWithValue("$mikanid", mikanId);
            await using var reader = await summary.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                counts[reader.GetString(0)] = reader.GetInt32(1);
            }
        }

        var tasks = new List<MikanWorkImpactTaskProjection>();
        await using (var list = connection.CreateCommand())
        {
            list.CommandText = """
                SELECT task.id, task.title, task.source_id, task.status,
                       task.bangumi_subject_id,
                       (SELECT run.tmdb_series_id
                        FROM metadata_resolution_runs AS run
                        WHERE run.task_id = task.id AND run.tmdb_series_id IS NOT NULL
                        ORDER BY run.attempt_number DESC LIMIT 1),
                       (SELECT run.tmdb_season_number
                        FROM metadata_resolution_runs AS run
                        WHERE run.task_id = task.id AND run.tmdb_season_number IS NOT NULL
                        ORDER BY run.attempt_number DESC LIMIT 1),
                       job.organization_state,
                       CASE
                           WHEN task.status IN ('organizing_cleanup', 'organized')
                             OR job.organization_state IN ('organizing', 'cleanup', 'completed')
                               THEN 'completed_protected'
                           WHEN EXISTS (
                               SELECT 1 FROM metadata_resolution_runs AS active_run
                               WHERE active_run.task_id = task.id AND active_run.status = 'running')
                             OR task.status IN ('metadata_resolving', 'metadata_episode_resolving')
                               THEN 'active'
                           WHEN task.status = 'metadata_failed'
                               THEN 'retryable_failed'
                           WHEN task.status IN (
                               'received', 'staged', 'dispatching',
                               'download_preparing', 'download_queued',
                               'downloading', 'downloaded')
                               THEN 'future'
                           WHEN task.status IN ('metadata_season_resolved', 'metadata_resolved')
                             OR job.organization_state = 'pending'
                               THEN 'resolved_protected'
                           ELSE 'other'
                       END,
                       task.updated_at_utc
                FROM ingest_tasks AS task
                LEFT JOIN download_jobs AS job ON job.task_id = task.id
                WHERE task.mikanid = $mikanid
                ORDER BY task.updated_at_utc DESC, task.id DESC
                LIMIT $limit;
                """;
            list.Parameters.AddWithValue("$mikanid", mikanId);
            list.Parameters.AddWithValue("$limit", limit);
            await using var reader = await list.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                tasks.Add(new MikanWorkImpactTaskProjection(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    reader.IsDBNull(5) ? null : reader.GetInt32(5),
                    reader.IsDBNull(6) ? null : reader.GetInt32(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    ParseMikanImpactCategory(reader.GetString(8)),
                    DateTimeOffset.Parse(
                        reader.GetString(9),
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind)));
            }
        }

        var future = Count("future");
        var retryable = Count("retryable_failed");
        var active = Count("active");
        var resolved = Count("resolved_protected");
        var completed = Count("completed_protected");
        var other = Count("other");
        var total = future + retryable + active + resolved + completed + other;
        return new MikanWorkImpactProjection(
            mikanId,
            total,
            future,
            retryable,
            active,
            resolved,
            completed,
            other,
            total > tasks.Count,
            tasks);

        int Count(string category) => counts.TryGetValue(category, out var value) ? value : 0;
    }

    public async Task<int> RematchFailedMikanTasksAsync(
        int mikanId,
        long expectedRuleRevision,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(mikanId, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedRuleRevision);
        var now = Format(utcNow);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        long actualRevision;
        await using (var revision = connection.CreateCommand())
        {
            revision.Transaction = transaction;
            revision.CommandText = """
                SELECT COALESCE(
                    (SELECT revision FROM mikan_work_rules WHERE mikanid = $mikanid),
                    0);
                """;
            revision.Parameters.AddWithValue("$mikanid", mikanId);
            actualRevision = Convert.ToInt64(
                await revision.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
                CultureInfo.InvariantCulture);
        }

        if (actualRevision != expectedRuleRevision)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw new MikanWorkRuleRematchRevisionException(
                mikanId,
                expectedRuleRevision,
                actualRevision);
        }

        var retried = 0;
        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE ingest_tasks
                SET status = CASE WHEN EXISTS (
                        SELECT 1 FROM download_jobs
                        WHERE download_jobs.task_id = ingest_tasks.id
                          AND download_jobs.preparation_state IN ('pending', 'preparing'))
                    THEN 'download_preparing' ELSE 'downloaded' END,
                    failure_kind = NULL,
                    failure_reason = NULL,
                    updated_at_utc = $now
                WHERE mikanid = $mikanid
                  AND status = 'metadata_failed'
                  AND NOT EXISTS (
                      SELECT 1 FROM metadata_resolution_runs
                      WHERE metadata_resolution_runs.task_id = ingest_tasks.id
                        AND metadata_resolution_runs.status = 'running')
                RETURNING id;
                """;
            update.Parameters.AddWithValue("$mikanid", mikanId);
            update.Parameters.AddWithValue("$now", now);
            await using var reader = await update.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                retried++;
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return retried;
    }

    private static MikanWorkImpactCategory ParseMikanImpactCategory(string value) => value switch
    {
        "future" => MikanWorkImpactCategory.Future,
        "retryable_failed" => MikanWorkImpactCategory.RetryableFailed,
        "active" => MikanWorkImpactCategory.Active,
        "resolved_protected" => MikanWorkImpactCategory.ResolvedProtected,
        "completed_protected" => MikanWorkImpactCategory.CompletedProtected,
        _ => MikanWorkImpactCategory.Other,
    };

    private enum EpisodeClaimDecision
    {
        Owned,
        AlreadyCompleted,
        ClaimedByAnotherTask,
    }

    private static string CanonicalName(TmdbSeries series) =>
        !string.IsNullOrWhiteSpace(series.Name) ? series.Name.Trim() : series.OriginalName.Trim();

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static AiMetadataProviderUsage? NormalizeAiUsage(AiMetadataProviderUsage? usage)
    {
        if (usage is null || usage.RequestCount <= 0)
        {
            return null;
        }

        var model = usage.Model.Trim();
        if (model.Length is < 1 or > 256 || model.Any(char.IsControl))
        {
            throw new ArgumentException(
                "AI model must be between 1 and 256 printable characters.",
                nameof(usage));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(usage.ToolCallCount);
        if (usage.PromptTokens is < 0
            || usage.CompletionTokens is < 0
            || usage.TotalTokens is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(usage),
                "AI token counts cannot be negative.");
        }

        return usage with { Model = model };
    }

    private static AiMetadataProviderUsage? ReadAiUsage(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        return new AiMetadataProviderUsage(
            reader.GetString(ordinal),
            reader.IsDBNull(ordinal + 1) ? null : reader.GetInt64(ordinal + 1),
            reader.IsDBNull(ordinal + 2) ? null : reader.GetInt64(ordinal + 2),
            reader.IsDBNull(ordinal + 3) ? null : reader.GetInt64(ordinal + 3),
            reader.GetInt32(ordinal + 4),
            reader.GetInt32(ordinal + 5));
    }

    private static string? NormalizeAttemptReason(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length > 512
            || normalized.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Metadata attempt reason must be at most 512 printable characters.",
                nameof(value));
        }

        return normalized;
    }

    private static DateTimeOffset? ParseDateTimeOffset(string value) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed
            : null;

    private static void ValidateIdentifier(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '_' or '-')))
        {
            throw new ArgumentException("Value must be a stable ASCII identifier.", parameterName);
        }
    }
}
