using System.Globalization;
using AnimeGoNet.Data.Sqlite;
using Microsoft.Data.Sqlite;

namespace AnimeGoNet.Data.Metadata;

public sealed record PendingTmdbNfoRewriteClaim(
    string JobId,
    string LeaseToken,
    int BangumiSubjectId,
    int TmdbSeriesId,
    string SaveRootPath,
    string SeriesDirectoryName,
    string CanonicalSeriesName,
    int AttemptCount);

public sealed record PendingTmdbNfoRewriteProjection(
    string JobId,
    int BangumiSubjectId,
    int TmdbSeriesId,
    string State,
    int AttemptCount,
    string? FailureCode,
    DateTimeOffset? NextAttemptAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public sealed class PendingTmdbNfoRewriteStore(AnimeGoSqliteDatabase database)
{
    public async Task<IReadOnlyList<PendingTmdbNfoRewriteProjection>> ListForTaskAsync(
        string taskId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        await using var connection = await database.OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT DISTINCT rewrite.id, rewrite.bangumi_subject_id,
                   rewrite.tmdb_series_id, rewrite.state, rewrite.attempt_count,
                   rewrite.failure_code, rewrite.next_attempt_at_utc,
                   rewrite.updated_at_utc, rewrite.completed_at_utc
            FROM ingest_tasks AS task
            JOIN task_files AS file ON file.task_id = task.id
            JOIN download_jobs AS download ON download.task_id = task.id
            JOIN pending_tmdb_nfo_rewrite_jobs AS rewrite
              ON rewrite.bangumi_subject_id = task.bangumi_subject_id
             AND rewrite.tmdb_series_id = file.tmdb_series_id
             AND rewrite.save_root_path = download.save_root_path
            WHERE task.id = $task_id
              AND task.bangumi_subject_id IS NOT NULL
              AND file.tmdb_series_id IS NOT NULL
            ORDER BY rewrite.updated_at_utc DESC, rewrite.id;
            """;
        command.Parameters.AddWithValue("$task_id", taskId);
        var results = new List<PendingTmdbNfoRewriteProjection>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new PendingTmdbNfoRewriteProjection(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetString(3),
                reader.GetInt32(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : Parse(reader.GetString(6)),
                Parse(reader.GetString(7)),
                reader.IsDBNull(8) ? null : Parse(reader.GetString(8))));
        }

        return results;
    }

    public async Task<PendingTmdbNfoRewriteClaim?> TryClaimNextAsync(
        DateTimeOffset utcNow,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(leaseDuration, TimeSpan.Zero);
        var now = Format(utcNow);
        var token = Guid.NewGuid().ToString("N");
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using (var recover = connection.CreateCommand())
        {
            recover.Transaction = transaction;
            recover.CommandText = """
                UPDATE pending_tmdb_nfo_rewrite_jobs
                SET state = 'failed', lease_token = NULL, lease_expires_at_utc = NULL,
                    next_attempt_at_utc = $now, failure_code = 'nfo_rewrite_lease_expired',
                    updated_at_utc = $now
                WHERE state = 'writing' AND lease_expires_at_utc <= $now;
                """;
            recover.Parameters.AddWithValue("$now", now);
            await recover.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var claim = connection.CreateCommand();
        claim.Transaction = transaction;
        claim.CommandText = """
            UPDATE pending_tmdb_nfo_rewrite_jobs
            SET state = 'writing', lease_token = $token, lease_expires_at_utc = $expires,
                attempt_count = attempt_count + 1, next_attempt_at_utc = NULL,
                failure_code = NULL, updated_at_utc = $now
            WHERE id = (
                SELECT id FROM pending_tmdb_nfo_rewrite_jobs
                WHERE state IN ('pending', 'failed')
                  AND (next_attempt_at_utc IS NULL OR next_attempt_at_utc <= $now)
                ORDER BY updated_at_utc, id
                LIMIT 1)
            RETURNING id, bangumi_subject_id, tmdb_series_id, save_root_path,
                      series_directory_name, canonical_series_name, attempt_count;
            """;
        claim.Parameters.AddWithValue("$token", token);
        claim.Parameters.AddWithValue("$expires", Format(utcNow.Add(leaseDuration)));
        claim.Parameters.AddWithValue("$now", now);
        await using var reader = await claim.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        PendingTmdbNfoRewriteClaim? result = null;
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result = new PendingTmdbNfoRewriteClaim(
                reader.GetString(0),
                token,
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetInt32(6));
        }

        await reader.DisposeAsync().ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task CompleteAsync(
        PendingTmdbNfoRewriteClaim claim,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE pending_tmdb_nfo_rewrite_jobs
            SET state = 'completed', lease_token = NULL, lease_expires_at_utc = NULL,
                failure_code = NULL, completed_at_utc = $now, updated_at_utc = $now
            WHERE id = $id AND state = 'writing' AND lease_token = $token;
            """;
        command.Parameters.AddWithValue("$id", claim.JobId);
        command.Parameters.AddWithValue("$token", claim.LeaseToken);
        command.Parameters.AddWithValue("$now", Format(utcNow));
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            throw new InvalidOperationException("Pending TMDB NFO rewrite lease is no longer active.");
        }
    }

    public async Task FailAsync(
        PendingTmdbNfoRewriteClaim claim,
        string failureCode,
        DateTimeOffset utcNow,
        TimeSpan retryDelay,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);
        ArgumentOutOfRangeException.ThrowIfLessThan(retryDelay, TimeSpan.Zero);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE pending_tmdb_nfo_rewrite_jobs
            SET state = 'failed', lease_token = NULL, lease_expires_at_utc = NULL,
                failure_code = $failure, next_attempt_at_utc = $retry,
                updated_at_utc = $now
            WHERE id = $id AND state = 'writing' AND lease_token = $token;
            """;
        command.Parameters.AddWithValue("$id", claim.JobId);
        command.Parameters.AddWithValue("$token", claim.LeaseToken);
        command.Parameters.AddWithValue("$failure", failureCode);
        command.Parameters.AddWithValue("$retry", Format(utcNow.Add(retryDelay)));
        command.Parameters.AddWithValue("$now", Format(utcNow));
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            throw new InvalidOperationException("Pending TMDB NFO rewrite lease is no longer active.");
        }
    }

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
}
