using System.Globalization;
using AnimeGoNet.Data.Sqlite;
using Microsoft.Data.Sqlite;

namespace AnimeGoNet.Data.Downloads;

public sealed class DownloadPreparationStore(AnimeGoSqliteDatabase database)
{
    public async Task<DownloadPreparationClaim?> TryClaimNextAsync(
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
                UPDATE download_jobs
                SET preparation_state = 'pending', preparation_lease_token = NULL,
                    preparation_lease_expires_at_utc = NULL,
                    preparation_failure_code = 'download_preparation_lease_expired',
                    preparation_next_attempt_at_utc = $now,
                    updated_at_utc = $now
                WHERE preparation_state = 'preparing'
                  AND preparation_lease_expires_at_utc <= $now;
                """;
            recover.Parameters.AddWithValue("$now", now);
            await recover.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        string? jobId = null;
        string? taskId = null;
        var attemptCount = 0;
        await using (var claim = connection.CreateCommand())
        {
            claim.Transaction = transaction;
            claim.CommandText = """
                UPDATE download_jobs
                SET preparation_state = 'preparing',
                    preparation_lease_token = $lease_token,
                    preparation_lease_expires_at_utc = $lease_expires_at_utc,
                    preparation_attempt_count = preparation_attempt_count + 1,
                    preparation_failure_code = NULL,
                    preparation_next_attempt_at_utc = NULL,
                    updated_at_utc = $now
                WHERE id = (
                    SELECT job.id
                    FROM download_jobs AS job
                    JOIN ingest_tasks AS task ON task.id = job.task_id
                    WHERE job.preparation_state = 'pending'
                      AND task.status = 'metadata_resolved'
                      AND (job.preparation_next_attempt_at_utc IS NULL
                           OR job.preparation_next_attempt_at_utc <= $now)
                    ORDER BY job.updated_at_utc, job.id
                    LIMIT 1)
                  AND preparation_state = 'pending'
                RETURNING id, task_id, preparation_attempt_count;
                """;
            claim.Parameters.AddWithValue("$lease_token", leaseToken);
            claim.Parameters.AddWithValue("$lease_expires_at_utc", Format(utcNow.Add(leaseDuration)));
            claim.Parameters.AddWithValue("$now", now);
            await using var reader = await claim.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                jobId = reader.GetString(0);
                taskId = reader.GetString(1);
                attemptCount = reader.GetInt32(2);
            }
        }

        if (jobId is null || taskId is null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }

        string downloaderId;
        string infoHash;
        await using (var job = connection.CreateCommand())
        {
            job.Transaction = transaction;
            job.CommandText = """
                SELECT downloader_id, info_hash
                FROM download_jobs
                WHERE id = $job_id AND task_id = $task_id
                  AND preparation_state = 'preparing'
                  AND preparation_lease_token = $lease_token;
                """;
            job.Parameters.AddWithValue("$job_id", jobId);
            job.Parameters.AddWithValue("$task_id", taskId);
            job.Parameters.AddWithValue("$lease_token", leaseToken);
            await using var reader = await job.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("Claimed download preparation job disappeared.");
            }

            downloaderId = reader.GetString(0);
            infoHash = reader.GetString(1);
        }

        var files = new List<DownloadPreparationFile>();
        await using (var queryFiles = connection.CreateCommand())
        {
            queryFiles.Transaction = transaction;
            queryFiles.CommandText = """
                SELECT id, relative_path, size_bytes, disposition
                FROM task_files
                WHERE task_id = $task_id
                ORDER BY relative_path, id;
                """;
            queryFiles.Parameters.AddWithValue("$task_id", taskId);
            await using var reader = await queryFiles.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                files.Add(new DownloadPreparationFile(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetInt64(2),
                    reader.GetString(3)));
            }
        }

        if (files.Count == 0)
        {
            throw new InvalidOperationException("Download preparation task has no files.");
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new DownloadPreparationClaim(
            jobId,
            taskId,
            downloaderId,
            infoHash,
            leaseToken,
            attemptCount,
            files);
    }

    public async Task CompleteAsync(
        DownloadPreparationClaim claim,
        IReadOnlyList<DownloadFileAssignment> assignments,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(assignments);
        if (assignments.Count != claim.Files.Count
            || !assignments.Select(item => item.FileId).ToHashSet(StringComparer.Ordinal)
                .SetEquals(claim.Files.Select(item => item.FileId))
            || assignments.Select(item => item.DownloadFileIndex).Distinct().Count() != assignments.Count)
        {
            throw new ArgumentException("Every task file must have one unique downloader file assignment.", nameof(assignments));
        }

        foreach (var assignment in assignments)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(assignment.DownloadFileIndex);
            ArgumentOutOfRangeException.ThrowIfLessThan(assignment.Priority, 0);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(assignment.Priority, 7);
            if (assignment.Wanted != (assignment.Priority > 0))
            {
                throw new ArgumentException("Wanted state must match non-zero downloader priority.", nameof(assignments));
            }
        }

        var allSkipped = assignments.All(item => !item.Wanted);
        var now = Format(utcNow);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (var guard = connection.CreateCommand())
        {
            guard.Transaction = transaction;
            guard.CommandText = """
                SELECT COUNT(*) FROM download_jobs
                WHERE id = $job_id AND task_id = $task_id
                  AND preparation_state = 'preparing'
                  AND preparation_lease_token = $lease_token;
                """;
            AddIdentity(guard, claim);
            if (Convert.ToInt32(await guard.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture) != 1)
            {
                throw new InvalidOperationException("Download preparation lease is no longer owned.");
            }
        }

        foreach (var assignment in assignments)
        {
            await using var updateFile = connection.CreateCommand();
            updateFile.Transaction = transaction;
            updateFile.CommandText = """
                UPDATE task_files
                SET download_file_index = $download_file_index,
                    download_priority = $download_priority,
                    download_wanted = $download_wanted
                WHERE id = $file_id AND task_id = $task_id;
                """;
            updateFile.Parameters.AddWithValue("$download_file_index", assignment.DownloadFileIndex);
            updateFile.Parameters.AddWithValue("$download_priority", assignment.Priority);
            updateFile.Parameters.AddWithValue("$download_wanted", assignment.Wanted ? 1 : 0);
            updateFile.Parameters.AddWithValue("$file_id", assignment.FileId);
            updateFile.Parameters.AddWithValue("$task_id", claim.TaskId);
            if (await updateFile.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            {
                throw new InvalidOperationException("Download preparation task file changed concurrently.");
            }
        }

        await using (var finish = connection.CreateCommand())
        {
            finish.Transaction = transaction;
            finish.CommandText = """
                UPDATE download_jobs
                SET preparation_state = 'completed', preparation_lease_token = NULL,
                    preparation_lease_expires_at_utc = NULL,
                    preparation_next_attempt_at_utc = NULL,
                    preparation_failure_code = NULL,
                    state = $job_state, updated_at_utc = $now,
                    revision = revision + 1
                WHERE id = $job_id AND task_id = $task_id
                  AND preparation_state = 'preparing'
                  AND preparation_lease_token = $lease_token;

                UPDATE ingest_tasks
                SET status = $task_status, failure_kind = NULL,
                    failure_reason = NULL, updated_at_utc = $now
                WHERE id = $task_id AND status = 'metadata_resolved';
                """;
            AddIdentity(finish, claim);
            finish.Parameters.AddWithValue("$job_state", allSkipped ? "skipped_duplicate" : "waiting");
            finish.Parameters.AddWithValue("$task_status", allSkipped ? "download_skipped_duplicate" : "download_queued");
            finish.Parameters.AddWithValue("$now", now);
            if (await finish.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 2)
            {
                throw new InvalidOperationException("Download preparation completion changed concurrently.");
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> ReleaseAsync(
        DownloadPreparationClaim claim,
        string safeFailureCode,
        DateTimeOffset retryAtUtc,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ValidateFailureCode(safeFailureCode);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE download_jobs
            SET preparation_state = 'pending', preparation_lease_token = NULL,
                preparation_lease_expires_at_utc = NULL,
                preparation_next_attempt_at_utc = $retry_at_utc,
                preparation_failure_code = $failure_code,
                updated_at_utc = $now,
                revision = revision + 1
            WHERE id = $job_id AND task_id = $task_id
              AND preparation_state = 'preparing'
              AND preparation_lease_token = $lease_token;
            """;
        AddIdentity(command, claim);
        command.Parameters.AddWithValue("$retry_at_utc", Format(retryAtUtc));
        command.Parameters.AddWithValue("$failure_code", safeFailureCode);
        command.Parameters.AddWithValue("$now", Format(utcNow));
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    private static void AddIdentity(SqliteCommand command, DownloadPreparationClaim claim)
    {
        command.Parameters.AddWithValue("$job_id", claim.JobId);
        command.Parameters.AddWithValue("$task_id", claim.TaskId);
        command.Parameters.AddWithValue("$lease_token", claim.LeaseToken);
    }

    private static void ValidateFailureCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '_' or '-')))
        {
            throw new ArgumentException("Failure code must be a stable ASCII identifier.", nameof(value));
        }
    }

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
