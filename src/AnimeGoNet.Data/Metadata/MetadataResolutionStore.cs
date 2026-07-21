using System.Globalization;
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
                SET status = 'downloaded', failure_kind = 'metadata_retry',
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
                WHERE task.status = 'downloaded'
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
                WHERE id = $task_id AND status = 'downloaded';
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
            SET status = 'downloaded', failure_kind = NULL,
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
