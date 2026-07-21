using System.Globalization;
using AnimeGoNet.Core.Library;
using AnimeGoNet.Core.Metadata;
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
        var tmdbSeriesId = 0;
        var tmdbSeasonNumber = 0;
        await using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = """
                SELECT task.id, task.title, task.mikanid, task.groupid, task.bangumi_subject_id,
                       MIN(file.tmdb_series_id), MIN(file.tmdb_season_number)
                FROM ingest_tasks AS task
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
                   AND COUNT(DISTINCT file.tmdb_season_number) = 1
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
                tmdbSeriesId = reader.GetInt32(5);
                tmdbSeasonNumber = reader.GetInt32(6);
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
            insert.Parameters.AddWithValue("$tmdb_season_number", tmdbSeasonNumber);
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
        return new MetadataEpisodeTaskClaim(
            new MetadataTaskClaim(
                runId, taskId, title!, mikanId, groupId, bangumiSubjectId, attemptNumber, leaseToken),
            tmdbSeriesId,
            tmdbSeasonNumber,
            files);
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
        await using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = """
                SELECT task.id, task.title, task.mikanid, task.groupid, task.bangumi_subject_id
                FROM ingest_tasks AS task
                WHERE task.status IN ('download_preparing', 'downloaded')
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
                WHERE id = $task_id AND status IN ('download_preparing', 'downloaded');
                """;
            update.Parameters.AddWithValue("$task_id", taskId);
            update.Parameters.AddWithValue("$now", now);
            if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            {
                throw new InvalidOperationException("Metadata task was not claimable.");
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
            leaseToken);
    }

    public async Task RecordAttemptAsync(
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
            ValidateIdentifier(attempt.ErrorCode, nameof(attempt.ErrorCode));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(attempt.DurationMilliseconds);
        ArgumentOutOfRangeException.ThrowIfLessThan(attempt.AttemptNumber, 1);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO metadata_resolution_attempts (
                id, run_id, stage, strategy, priority, result, error_code,
                reason, retryable, attempt_number, duration_ms, created_at_utc)
            SELECT $id, id, $stage, $strategy, $priority, $result, $error_code,
                   NULL, $retryable, $attempt_number, $duration_ms, $created_at_utc
            FROM metadata_resolution_runs
            WHERE id = $run_id AND status = 'running' AND lease_token = $lease_token;
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("$run_id", claim.RunId);
        command.Parameters.AddWithValue("$lease_token", claim.LeaseToken);
        command.Parameters.AddWithValue("$stage", attempt.Stage);
        command.Parameters.AddWithValue("$strategy", attempt.Strategy);
        command.Parameters.AddWithValue("$priority", (object?)attempt.Priority ?? DBNull.Value);
        command.Parameters.AddWithValue("$result", attempt.Result);
        command.Parameters.AddWithValue("$error_code", (object?)attempt.ErrorCode ?? DBNull.Value);
        command.Parameters.AddWithValue("$retryable", attempt.Retryable ? 1 : 0);
        command.Parameters.AddWithValue("$attempt_number", attempt.AttemptNumber);
        command.Parameters.AddWithValue("$duration_ms", attempt.DurationMilliseconds);
        command.Parameters.AddWithValue("$created_at_utc", Format(utcNow));
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            throw new InvalidOperationException("Metadata resolution lease is no longer active.");
        }
    }

    public async Task CompleteSeasonAsync(
        MetadataTaskClaim claim,
        TmdbSeries series,
        TmdbSeason season,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(season);
        if (series.Id <= 0 || season.Id <= 0 || season.SeriesId != series.Id || season.SeasonNumber <= 0)
        {
            throw new ArgumentException("TMDB Series/Season identity is invalid.", nameof(season));
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
                    original_name, poster_path, needs_tmdb_completion,
                    created_at_utc, updated_at_utc)
                VALUES ($id, $tmdb_id, NULL, $canonical_name, $original_name, NULL, 0, $now, $now)
                ON CONFLICT(tmdb_series_id) WHERE tmdb_series_id > 0 DO UPDATE SET
                    canonical_name = excluded.canonical_name,
                    original_name = excluded.original_name,
                    updated_at_utc = excluded.updated_at_utc;
                """;
            upsertSeries.Parameters.AddWithValue("$id", seriesRowId);
            upsertSeries.Parameters.AddWithValue("$tmdb_id", series.Id);
            upsertSeries.Parameters.AddWithValue("$canonical_name", CanonicalName(series));
            upsertSeries.Parameters.AddWithValue("$original_name", series.OriginalName);
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

        await using (var upsertSeason = connection.CreateCommand())
        {
            upsertSeason.Transaction = transaction;
            upsertSeason.CommandText = """
                INSERT INTO anime_seasons (
                    id, series_id, season_number, canonical_name, poster_path,
                    created_at_utc, updated_at_utc)
                VALUES ($id, $series_id, $season_number, $canonical_name, NULL, $now, $now)
                ON CONFLICT(series_id, season_number) DO UPDATE SET
                    canonical_name = excluded.canonical_name,
                    updated_at_utc = excluded.updated_at_utc;

                UPDATE task_files
                SET tmdb_series_id = $tmdb_id, tmdb_season_number = $season_number
                WHERE task_id = $task_id AND disposition = 'pending';
                """;
            upsertSeason.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            upsertSeason.Parameters.AddWithValue("$series_id", seriesRowId);
            upsertSeason.Parameters.AddWithValue("$season_number", season.SeasonNumber);
            upsertSeason.Parameters.AddWithValue("$canonical_name", season.Name);
            upsertSeason.Parameters.AddWithValue("$tmdb_id", series.Id);
            upsertSeason.Parameters.AddWithValue("$task_id", claim.TaskId);
            upsertSeason.Parameters.AddWithValue("$now", now);
            await upsertSeason.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
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
                    tmdb_series_id = $tmdb_id, tmdb_season_number = $season_number
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
            finish.Parameters.AddWithValue("$season_number", season.SeasonNumber);
            if (await finish.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 2)
            {
                throw new InvalidOperationException("Metadata resolution lease is no longer active.");
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task CompleteEpisodesAsync(
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

        foreach (var resolution in fileResolutions)
        {
            if (resolution.Disposition is not ("episode" or "other"))
            {
                throw new ArgumentException("Episode resolution disposition must be episode or other.", nameof(fileResolutions));
            }

            if (resolution.Disposition == "episode")
            {
                if (resolution.Episode is null
                    || resolution.Episode.SeriesId != claim.TmdbSeriesId
                    || resolution.Episode.SeasonNumber != claim.TmdbSeasonNumber
                    || resolution.Episode.EpisodeNumber <= 0)
                {
                    throw new ArgumentException("TMDB Episode identity is invalid.", nameof(fileResolutions));
                }
            }
            else
            {
                if (resolution.Episode is not null || resolution.OtherReason is null)
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
            if (resolution.Episode is null)
            {
                continue;
            }

            var identity = new TmdbEpisodeIdentity(
                resolution.Episode.SeriesId,
                resolution.Episode.SeasonNumber,
                resolution.Episode.EpisodeNumber);
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

        foreach (var resolution in fileResolutions)
        {
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
            if (resolution.Episode is not null)
            {
                var identity = new TmdbEpisodeIdentity(
                    resolution.Episode.SeriesId,
                    resolution.Episode.SeasonNumber,
                    resolution.Episode.EpisodeNumber);
                var decision = episodeClaims[identity];
                if (decision != EpisodeClaimDecision.Owned)
                {
                    disposition = "duplicate";
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
                    rename_suffix = $rename_suffix
                WHERE id = $file_id AND task_id = $task_id
                  AND disposition = 'pending'
                  AND tmdb_series_id = $tmdb_series_id
                  AND tmdb_season_number = $tmdb_season_number;
                """;
            updateFile.Parameters.AddWithValue("$file_id", resolution.FileId);
            updateFile.Parameters.AddWithValue("$task_id", claim.Resolution.TaskId);
            updateFile.Parameters.AddWithValue("$tmdb_series_id", claim.TmdbSeriesId);
            updateFile.Parameters.AddWithValue("$tmdb_season_number", claim.TmdbSeasonNumber);
            updateFile.Parameters.AddWithValue(
                "$tmdb_episode_number",
                (object?)resolution.Episode?.EpisodeNumber ?? DBNull.Value);
            updateFile.Parameters.AddWithValue("$tmdb_episode_id", (object?)resolution.Episode?.Id ?? DBNull.Value);
            updateFile.Parameters.AddWithValue("$disposition", disposition);
            updateFile.Parameters.AddWithValue("$other_reason", (object?)otherReason ?? DBNull.Value);
            updateFile.Parameters.AddWithValue("$associated_file_id", (object?)resolution.AssociatedFileId ?? DBNull.Value);
            updateFile.Parameters.AddWithValue("$rename_suffix", (object?)resolution.RenameSuffix ?? DBNull.Value);
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
                SET status = 'metadata_resolved', failure_kind = NULL,
                    failure_reason = NULL, updated_at_utc = $now
                WHERE id = $task_id AND status = 'metadata_episode_resolving';
                """;
            finish.Parameters.AddWithValue("$now", now);
            finish.Parameters.AddWithValue("$run_id", claim.Resolution.RunId);
            finish.Parameters.AddWithValue("$task_id", claim.Resolution.TaskId);
            finish.Parameters.AddWithValue("$lease_token", claim.Resolution.LeaseToken);
            if (await finish.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 2)
            {
                throw new InvalidOperationException("Metadata Episode resolution lease is no longer active.");
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
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

    public async Task FailEpisodesAsync(
        MetadataEpisodeTaskClaim claim,
        MetadataFailure failure,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(failure);
        ValidateIdentifier(failure.Code, nameof(failure.Code));
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
        ValidateIdentifier(failure.Code, nameof(failure.Code));
        ValidateIdentifier(fallbackDenialReason, nameof(fallbackDenialReason));
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
                   (SELECT run.tmdb_series_id FROM metadata_resolution_runs AS run
                    WHERE run.task_id = task.id AND run.tmdb_series_id IS NOT NULL
                    ORDER BY run.attempt_number DESC LIMIT 1),
                   (SELECT run.tmdb_season_number FROM metadata_resolution_runs AS run
                    WHERE run.task_id = task.id AND run.tmdb_season_number IS NOT NULL
                    ORDER BY run.attempt_number DESC LIMIT 1),
                   (SELECT attempt.strategy
                    FROM metadata_resolution_attempts AS attempt
                    JOIN metadata_resolution_runs AS run ON run.id = attempt.run_id
                    WHERE run.task_id = task.id AND attempt.stage = 'series' AND attempt.result = 'matched'
                    ORDER BY attempt.created_at_utc DESC, attempt.id DESC LIMIT 1),
                   (SELECT attempt.strategy
                    FROM metadata_resolution_attempts AS attempt
                    JOIN metadata_resolution_runs AS run ON run.id = attempt.run_id
                    WHERE run.task_id = task.id AND attempt.stage = 'season' AND attempt.result = 'matched'
                    ORDER BY attempt.created_at_utc DESC, attempt.id DESC LIMIT 1),
                   (SELECT attempt.strategy
                    FROM metadata_resolution_attempts AS attempt
                    JOIN metadata_resolution_runs AS run ON run.id = attempt.run_id
                    WHERE run.task_id = task.id AND attempt.stage = 'episode' AND attempt.result = 'matched'
                    ORDER BY attempt.created_at_utc DESC, attempt.id DESC LIMIT 1),
                   task.failure_kind, task.failure_reason,
                   SUM(CASE WHEN file.disposition = 'episode' THEN 1 ELSE 0 END),
                   SUM(CASE WHEN file.disposition = 'other' THEN 1 ELSE 0 END),
                   SUM(CASE WHEN file.disposition = 'duplicate' THEN 1 ELSE 0 END),
                   SUM(CASE WHEN file.disposition = 'pending' THEN 1 ELSE 0 END),
                   task.updated_at_utc
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
            items.Add(new MetadataTaskListProjection(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4),
                reader.IsDBNull(5) ? null : reader.GetInt32(5),
                reader.IsDBNull(6) ? null : reader.GetInt32(6),
                reader.IsDBNull(7) ? null : reader.GetInt32(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.GetInt32(13),
                reader.GetInt32(14),
                reader.GetInt32(15),
                reader.GetInt32(16),
                DateTimeOffset.Parse(reader.GetString(17), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)));
        }

        return items;
    }

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

    private static void ValidateIdentifier(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '_' or '-')))
        {
            throw new ArgumentException("Value must be a stable ASCII identifier.", parameterName);
        }
    }
}
