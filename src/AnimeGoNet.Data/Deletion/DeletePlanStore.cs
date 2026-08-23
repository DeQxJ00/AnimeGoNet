using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AnimeGoNet.Data.Sqlite;
using Microsoft.Data.Sqlite;

namespace AnimeGoNet.Data.Deletion;

public sealed class DeletePlanStore(AnimeGoSqliteDatabase database)
{
    public async Task<DeletePlanPreview?> GetPreviewAsync(
        string taskId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await ReadPreviewAsync(connection, null, taskId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DeleteExecutionPlan> CreateAsync(
        string taskId,
        string expectedFingerprint,
        DeleteSelection selection,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedFingerprint);
        ArgumentNullException.ThrowIfNull(selection);
        if (!selection.Any)
        {
            throw new ArgumentException("At least one delete option must be selected.", nameof(selection));
        }

        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var preview = await ReadPreviewAsync(connection, transaction, taskId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Ingest task '{taskId}' was not found.");
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(preview.Fingerprint),
                Encoding.ASCII.GetBytes(expectedFingerprint.ToLowerInvariant())))
        {
            throw new InvalidOperationException("Delete preview is stale; request a new preview before confirming deletion.");
        }

        if (selection.DeleteTaskRecord && !preview.TaskRecordDeletionAllowed)
        {
            throw new InvalidOperationException(
                preview.TaskRecordDeletionDenialReason ?? "Task record deletion is not allowed.");
        }

        if (selection.DeleteTaskRecord && preview.DownloaderTasks.Count > 0 && !selection.DeleteDownloaderTask)
        {
            throw new InvalidOperationException(
                "Delete the downloader task before deleting its AnimeGoNet task record.");
        }

        var targets = preview.AllTargets
            .Where(target => selection.Includes(target.ItemKind))
            .ToArray();
        var executionId = Guid.NewGuid().ToString("N");
        var now = Format(utcNow);
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO delete_executions (
                    id, task_id, delete_business_record, delete_downloader_task,
                    delete_source_files, delete_media_files, plan_json, state,
                    failure_reason, created_at_utc, completed_at_utc, plan_fingerprint)
                VALUES ($id, $task_id, $business, $downloader, $source, $media,
                        $plan_json, 'pending', NULL, $now, NULL, $fingerprint);
                """;
            insert.Parameters.AddWithValue("$id", executionId);
            insert.Parameters.AddWithValue("$task_id", taskId);
            insert.Parameters.AddWithValue("$business", selection.DeleteBusinessRecord);
            insert.Parameters.AddWithValue("$downloader", selection.DeleteDownloaderTask);
            insert.Parameters.AddWithValue("$source", selection.DeleteSourceFiles);
            insert.Parameters.AddWithValue("$media", selection.DeleteMediaFiles);
            insert.Parameters.AddWithValue("$plan_json", CreatePlanSummary(preview.Fingerprint, targets.Length));
            insert.Parameters.AddWithValue("$now", now);
            insert.Parameters.AddWithValue("$fingerprint", preview.Fingerprint);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        for (var index = 0; index < targets.Length; index++)
        {
            var target = targets[index];
            await using var item = connection.CreateCommand();
            item.Transaction = transaction;
            item.CommandText = """
                INSERT INTO delete_execution_items (
                    id, execution_id, item_kind, target_key, root_path, downloader_id,
                    display_value, ordinal, state, failure_code, completed_at_utc)
                VALUES ($id, $execution_id, $kind, $target, $root, $downloader,
                        $display, $ordinal, 'pending', NULL, NULL);
                """;
            item.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            item.Parameters.AddWithValue("$execution_id", executionId);
            item.Parameters.AddWithValue("$kind", target.ItemKind);
            item.Parameters.AddWithValue("$target", target.TargetKey);
            item.Parameters.AddWithValue("$root", (object?)target.RootPath ?? DBNull.Value);
            item.Parameters.AddWithValue("$downloader", (object?)target.DownloaderId ?? DBNull.Value);
            item.Parameters.AddWithValue("$display", target.DisplayValue);
            item.Parameters.AddWithValue("$ordinal", index);
            await item.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new DeleteExecutionPlan(
            executionId, taskId, preview.Fingerprint, selection, "pending", targets, utcNow.ToUniversalTime());
    }

    private static async Task<DeletePlanPreview?> ReadPreviewAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string taskId,
        CancellationToken cancellationToken)
    {
        string taskTitle;
        string taskStatus;
        string reviewState;
        await using (var task = connection.CreateCommand())
        {
            task.Transaction = transaction;
            task.CommandText = "SELECT title, status, readaptation_review_state FROM ingest_tasks WHERE id = $task_id;";
            task.Parameters.AddWithValue("$task_id", taskId);
            await using var reader = await task.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            taskTitle = reader.GetString(0);
            taskStatus = reader.GetString(1);
            reviewState = reader.GetString(2);
        }

        var business = await ReadTargetsAsync(connection, transaction, """
            SELECT DISTINCT 'business_record', 'tv:' || completion.id, NULL, NULL,
                   printf('TMDB %d S%02dE%03d', completion.tmdb_series_id,
                          completion.tmdb_season_number, completion.tmdb_episode_number)
            FROM task_files AS file
            JOIN completion_records AS completion
              ON completion.tmdb_series_id = file.tmdb_series_id
             AND completion.tmdb_season_number = file.tmdb_season_number
             AND completion.tmdb_episode_number = file.tmdb_episode_number
            WHERE file.task_id = $task_id AND file.associated_task_file_id IS NULL
            UNION ALL
            SELECT DISTINCT 'business_record', 'movie:' || completion.id, NULL, NULL,
                   printf('TMDB Movie %d', completion.tmdb_movie_id)
            FROM task_files AS file
            JOIN movie_completion_records AS completion
              ON completion.tmdb_movie_id = file.tmdb_movie_id
            WHERE file.task_id = $task_id AND file.associated_task_file_id IS NULL
            ORDER BY 2;
            """, taskId, cancellationToken).ConfigureAwait(false);
        var downloader = await ReadTargetsAsync(connection, transaction, """
            SELECT DISTINCT 'downloader_task', lower(info_hash), NULL, downloader_id,
                   downloader_id || ':' || lower(info_hash)
            FROM download_jobs
            WHERE task_id = $task_id AND info_hash IS NOT NULL AND length(info_hash) > 0
            ORDER BY downloader_id, lower(info_hash);
            """, taskId, cancellationToken).ConfigureAwait(false);
        var source = await ReadTargetsAsync(connection, transaction, """
            SELECT DISTINCT 'source_file', operation.source_path, job.download_root_path, NULL,
                   operation.source_path
            FROM file_operations AS operation
            JOIN task_files AS file ON file.id = operation.task_file_id
            JOIN download_jobs AS job ON job.task_id = file.task_id
            WHERE file.task_id = $task_id
              AND operation.source_path <> operation.target_path
            ORDER BY operation.source_path;
            """, taskId, cancellationToken).ConfigureAwait(false);
        var media = await ReadTargetsAsync(connection, transaction, """
            SELECT DISTINCT 'media_file', operation.target_path, job.save_root_path, NULL,
                   operation.target_path
            FROM file_operations AS operation
            JOIN task_files AS file ON file.id = operation.task_file_id
            JOIN download_jobs AS job ON job.task_id = file.task_id
            WHERE file.task_id = $task_id
            ORDER BY operation.target_path;
            """, taskId, cancellationToken).ConfigureAwait(false);
        var taskRecords = new[]
        {
            new DeletePlanTarget(DeleteItemKinds.TaskRecord, taskId, null, null, $"任务记录：{taskTitle}"),
        };
        var taskRecordDeletionAllowed = reviewState != "pending";
        var denialReason = taskRecordDeletionAllowed
            ? null
            : "Other 重新适配结果尚未人工审核，不能删除任务记录。";
        var all = business.Concat(downloader).Concat(source).Concat(media).Concat(taskRecords).ToArray();
        return new DeletePlanPreview(
            taskId, taskTitle, taskStatus, ComputeFingerprint(taskId, all),
            business, downloader, source, media, taskRecords,
            taskRecordDeletionAllowed, denialReason);
    }

    private static async Task<IReadOnlyList<DeletePlanTarget>> ReadTargetsAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sql,
        string taskId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$task_id", taskId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var targets = new List<DeletePlanTarget>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            targets.Add(new DeletePlanTarget(
                reader.GetString(0), reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetString(4)));
        }

        return targets;
    }

    private static string ComputeFingerprint(string taskId, IReadOnlyList<DeletePlanTarget> targets)
    {
        var canonical = new StringBuilder(taskId);
        foreach (var target in targets.OrderBy(item => item.ItemKind, StringComparer.Ordinal)
                     .ThenBy(item => item.TargetKey, StringComparer.Ordinal))
        {
            canonical.Append('\n').Append(target.ItemKind).Append('\0')
                .Append(target.TargetKey).Append('\0').Append(target.RootPath).Append('\0')
                .Append(target.DownloaderId);
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    private static string CreatePlanSummary(string fingerprint, int targetCount) =>
        FormattableString.Invariant($"{{\"version\":1,\"fingerprint\":\"{fingerprint}\",\"target_count\":{targetCount}}}");

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
