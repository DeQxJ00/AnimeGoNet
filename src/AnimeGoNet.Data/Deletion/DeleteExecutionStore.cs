using System.Globalization;
using AnimeGoNet.Data.Sqlite;
using Microsoft.Data.Sqlite;

namespace AnimeGoNet.Data.Deletion;

public sealed class DeleteExecutionStore(AnimeGoSqliteDatabase database)
{
    public async Task<DeleteExecutionStatus?> GetAsync(
        string executionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        string taskId;
        string state;
        string? failureReason;
        int attemptCount;
        DateTimeOffset createdAt;
        DateTimeOffset? completedAt;
        await using (var execution = connection.CreateCommand())
        {
            execution.CommandText = """
                SELECT task_id, state, failure_reason, attempt_count, created_at_utc, completed_at_utc
                FROM delete_executions WHERE id = $id;
                """;
            execution.Parameters.AddWithValue("$id", executionId);
            await using var reader = await execution.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            taskId = reader.GetString(0);
            state = reader.GetString(1);
            failureReason = reader.IsDBNull(2) ? null : reader.GetString(2);
            attemptCount = reader.GetInt32(3);
            createdAt = DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture);
            completedAt = reader.IsDBNull(5)
                ? null
                : DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture);
        }

        var items = new List<DeleteExecutionItem>();
        await using (var query = connection.CreateCommand())
        {
            query.CommandText = """
                SELECT id, item_kind, target_key, root_path, downloader_id, display_value, state
                FROM delete_execution_items WHERE execution_id = $id ORDER BY ordinal, id;
                """;
            query.Parameters.AddWithValue("$id", executionId);
            await using var reader = await query.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                items.Add(new DeleteExecutionItem(
                    reader.GetString(0), reader.GetString(1), reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetString(5), reader.GetString(6)));
            }
        }

        return new DeleteExecutionStatus(
            executionId, taskId, state, failureReason, attemptCount, createdAt, completedAt, items);
    }

    public async Task<DeleteExecutionClaim?> TryClaimNextAsync(
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
                UPDATE delete_executions
                SET state = 'pending', lease_token = NULL, lease_expires_at_utc = NULL,
                    failure_reason = 'delete_execution_lease_expired', next_attempt_at_utc = $now
                WHERE state = 'executing' AND lease_expires_at_utc <= $now;
                """;
            recover.Parameters.AddWithValue("$now", now);
            await recover.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        string? executionId = null;
        string? taskId = null;
        await using (var candidate = connection.CreateCommand())
        {
            candidate.Transaction = transaction;
            candidate.CommandText = """
                SELECT id, task_id FROM delete_executions
                WHERE state = 'pending'
                  AND (next_attempt_at_utc IS NULL OR next_attempt_at_utc <= $now)
                ORDER BY created_at_utc, id LIMIT 1;
                """;
            candidate.Parameters.AddWithValue("$now", now);
            await using var reader = await candidate.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                executionId = reader.GetString(0);
                taskId = reader.GetString(1);
            }
        }

        if (executionId is null || taskId is null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }

        int attempt;
        await using (var claim = connection.CreateCommand())
        {
            claim.Transaction = transaction;
            claim.CommandText = """
                UPDATE delete_executions
                SET state = 'executing', lease_token = $token, lease_expires_at_utc = $expires,
                    attempt_count = attempt_count + 1, next_attempt_at_utc = NULL,
                    failure_reason = NULL
                WHERE id = $id AND state = 'pending'
                RETURNING attempt_count;
                """;
            claim.Parameters.AddWithValue("$token", token);
            claim.Parameters.AddWithValue("$expires", Format(utcNow.Add(leaseDuration)));
            claim.Parameters.AddWithValue("$id", executionId);
            var result = await claim.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Delete execution candidate changed concurrently.");
            attempt = Convert.ToInt32(result, CultureInfo.InvariantCulture);
        }

        var items = new List<DeleteExecutionItem>();
        await using (var query = connection.CreateCommand())
        {
            query.Transaction = transaction;
            query.CommandText = """
                SELECT id, item_kind, target_key, root_path, downloader_id, display_value, state
                FROM delete_execution_items
                WHERE execution_id = $id AND state IN ('pending', 'failed')
                ORDER BY CASE item_kind
                    WHEN 'downloader_task' THEN 0
                    WHEN 'source_file' THEN 1
                    WHEN 'media_file' THEN 2
                    WHEN 'business_record' THEN 3
                    ELSE 4 END, ordinal, id;
                """;
            query.Parameters.AddWithValue("$id", executionId);
            await using var reader = await query.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                items.Add(new DeleteExecutionItem(
                    reader.GetString(0), reader.GetString(1), reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetString(5), reader.GetString(6)));
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new DeleteExecutionClaim(executionId, taskId, token, attempt, items);
    }

    public Task CompleteItemAsync(
        DeleteExecutionClaim claim,
        DeleteExecutionItem item,
        bool targetExisted,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default) =>
        SetItemCompletedAsync(claim, item, targetExisted ? "completed" : "skipped", utcNow, cancellationToken);

    public async Task CompleteBusinessRecordAsync(
        DeleteExecutionClaim claim,
        DeleteExecutionItem item,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        if (item.ItemKind != DeleteItemKinds.BusinessRecord)
        {
            throw new ArgumentException("Delete item is not a business record.", nameof(item));
        }

        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await GuardLeaseAsync(connection, transaction, claim, cancellationToken).ConfigureAwait(false);
        int? series = null;
        int? season = null;
        int? episode = null;
        await using (var identity = connection.CreateCommand())
        {
            identity.Transaction = transaction;
            identity.CommandText = """
                SELECT tmdb_series_id, tmdb_season_number, tmdb_episode_number
                FROM completion_records WHERE id = $id;
                """;
            identity.Parameters.AddWithValue("$id", item.TargetKey);
            await using var reader = await identity.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                series = reader.GetInt32(0);
                season = reader.GetInt32(1);
                episode = reader.GetInt32(2);
            }
        }

        if (series is not null)
        {
            await using var delete = connection.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText = """
                DELETE FROM completion_records WHERE id = $id;
                DELETE FROM episode_claims
                WHERE tmdb_series_id = $series AND tmdb_season_number = $season
                  AND tmdb_episode_number = $episode AND state = 'completed';
                """;
            delete.Parameters.AddWithValue("$id", item.TargetKey);
            delete.Parameters.AddWithValue("$series", series.Value);
            delete.Parameters.AddWithValue("$season", season!.Value);
            delete.Parameters.AddWithValue("$episode", episode!.Value);
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await SetItemStateAsync(
            connection, transaction, claim, item,
            series is null ? "skipped" : "completed", null, Format(utcNow), cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task CompleteAsync(
        DeleteExecutionClaim claim,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await GuardLeaseAsync(connection, transaction, claim, cancellationToken).ConfigureAwait(false);
        await using var pending = connection.CreateCommand();
        pending.Transaction = transaction;
        pending.CommandText = """
            SELECT COUNT(*) FROM delete_execution_items
            WHERE execution_id = $id AND state NOT IN ('completed', 'skipped');
            """;
        pending.Parameters.AddWithValue("$id", claim.ExecutionId);
        if (Convert.ToInt32(await pending.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture) != 0)
        {
            throw new InvalidOperationException("Delete execution still has incomplete items.");
        }

        await using var finish = connection.CreateCommand();
        finish.Transaction = transaction;
        finish.CommandText = """
            UPDATE delete_executions
            SET state = 'completed', lease_token = NULL, lease_expires_at_utc = NULL,
                next_attempt_at_utc = NULL, failure_reason = NULL, completed_at_utc = $now
            WHERE id = $id AND state = 'executing' AND lease_token = $token;
            """;
        finish.Parameters.AddWithValue("$now", Format(utcNow));
        AddIdentity(finish, claim);
        if (await finish.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            throw new InvalidOperationException("Delete execution changed concurrently.");
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ReleaseAsync(
        DeleteExecutionClaim claim,
        DeleteExecutionItem item,
        string failureCode,
        DateTimeOffset retryAtUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateFailureCode(failureCode);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await GuardLeaseAsync(connection, transaction, claim, cancellationToken).ConfigureAwait(false);
        await SetItemStateAsync(
            connection, transaction, claim, item, "failed", failureCode, null, cancellationToken).ConfigureAwait(false);
        await using var release = connection.CreateCommand();
        release.Transaction = transaction;
        release.CommandText = """
            UPDATE delete_executions
            SET state = 'pending', lease_token = NULL, lease_expires_at_utc = NULL,
                next_attempt_at_utc = $retry, failure_reason = $failure
            WHERE id = $id AND state = 'executing' AND lease_token = $token;
            """;
        release.Parameters.AddWithValue("$retry", Format(retryAtUtc));
        release.Parameters.AddWithValue("$failure", failureCode);
        AddIdentity(release, claim);
        if (await release.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            throw new InvalidOperationException("Delete execution release changed concurrently.");
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task SetItemCompletedAsync(
        DeleteExecutionClaim claim,
        DeleteExecutionItem item,
        string state,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await GuardLeaseAsync(connection, transaction, claim, cancellationToken).ConfigureAwait(false);
        await SetItemStateAsync(
            connection, transaction, claim, item, state, null, Format(utcNow), cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task SetItemStateAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DeleteExecutionClaim claim,
        DeleteExecutionItem item,
        string state,
        string? failureCode,
        string? completedAt,
        CancellationToken cancellationToken)
    {
        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE delete_execution_items
            SET state = $state, failure_code = $failure, completed_at_utc = $completed
            WHERE id = $item_id AND execution_id = $execution_id
              AND state IN ('pending', 'failed');
            """;
        update.Parameters.AddWithValue("$state", state);
        update.Parameters.AddWithValue("$failure", (object?)failureCode ?? DBNull.Value);
        update.Parameters.AddWithValue("$completed", (object?)completedAt ?? DBNull.Value);
        update.Parameters.AddWithValue("$item_id", item.ItemId);
        update.Parameters.AddWithValue("$execution_id", claim.ExecutionId);
        if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            throw new InvalidOperationException("Delete execution item changed concurrently.");
        }
    }

    private static async Task GuardLeaseAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DeleteExecutionClaim claim,
        CancellationToken cancellationToken)
    {
        await using var guard = connection.CreateCommand();
        guard.Transaction = transaction;
        guard.CommandText = """
            SELECT COUNT(*) FROM delete_executions
            WHERE id = $id AND state = 'executing' AND lease_token = $token;
            """;
        AddIdentity(guard, claim);
        if (Convert.ToInt32(await guard.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture) != 1)
        {
            throw new InvalidOperationException("Delete execution lease is no longer owned.");
        }
    }

    private static void AddIdentity(SqliteCommand command, DeleteExecutionClaim claim)
    {
        command.Parameters.AddWithValue("$id", claim.ExecutionId);
        command.Parameters.AddWithValue("$token", claim.LeaseToken);
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
