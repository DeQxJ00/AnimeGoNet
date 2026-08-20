using System.Globalization;
using System.Text.Json;
using AnimeGoNet.Core.Diagnostics;
using AnimeGoNet.Core.Downloads;
using AnimeGoNet.Data.Serialization;
using AnimeGoNet.Data.Sqlite;

namespace AnimeGoNet.Data.Downloads;

public sealed class DownloadJobStore(AnimeGoSqliteDatabase database)
{
    public async Task<int> CountActiveAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM download_jobs
            JOIN ingest_tasks ON ingest_tasks.id = download_jobs.task_id
            WHERE ingest_tasks.status IN ('download_queued', 'downloading', 'downloaded', 'download_error');
            """;
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    public async Task<DownloadSyncResult> ApplyInstanceSnapshotAsync(
        string downloaderId,
        IReadOnlyList<DownloadTaskSnapshot> snapshots,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(downloaderId);
        ArgumentNullException.ThrowIfNull(snapshots);
        var now = utcNow.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        var byHash = snapshots
            .GroupBy(snapshot => snapshot.Hash, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (Microsoft.Data.Sqlite.SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        var jobs = new List<(
            string JobId,
            string TaskId,
            string InfoHash,
            string State,
            bool IsStale,
            string SeedingState,
            int SeedingTargetMinutes,
            long SeedingElapsedSeconds)>();
        await using (var query = connection.CreateCommand())
        {
            query.Transaction = transaction;
            query.CommandText = """
                SELECT download_jobs.id, download_jobs.task_id, download_jobs.info_hash,
                       download_jobs.state, download_jobs.is_stale,
                       download_jobs.seeding_state,
                       download_jobs.seeding_target_minutes,
                       download_jobs.seeding_elapsed_seconds
                FROM download_jobs
                JOIN ingest_tasks ON ingest_tasks.id = download_jobs.task_id
                WHERE download_jobs.downloader_id = $downloader_id
                  AND ingest_tasks.status IN ('download_queued', 'downloading', 'downloaded', 'download_error');
                """;
            query.Parameters.AddWithValue("$downloader_id", downloaderId);
            await using var reader = await query.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                jobs.Add((
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetInt64(4) != 0,
                    reader.GetString(5),
                    reader.GetInt32(6),
                    reader.GetInt64(7)));
            }
        }

        var matched = 0;
        foreach (var job in jobs)
        {
            if (!byHash.TryGetValue(job.InfoHash, out var snapshot))
            {
                await using var stale = connection.CreateCommand();
                stale.Transaction = transaction;
                stale.CommandText = """
                    UPDATE download_jobs
                    SET is_stale = 1, revision = revision + 1, updated_at_utc = $now
                    WHERE id = $id;
                    """;
                stale.Parameters.AddWithValue("$now", now);
                stale.Parameters.AddWithValue("$id", job.JobId);
                await stale.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                if (!job.IsStale)
                {
                    await InsertEventAsync(
                        connection, transaction, job.JobId,
                        "snapshot_missing", "stale",
                        job.State, job.State, "downloader_task_not_observed",
                        now, cancellationToken).ConfigureAwait(false);
                }
                continue;
            }

            matched++;
            var seeding = DownloadSeedingLifecycle.Project(
                job.SeedingTargetMinutes,
                snapshot.State,
                Math.Max(0, snapshot.SeedingTimeSeconds),
                ParseSeedingState(job.SeedingState),
                job.SeedingElapsedSeconds);
            var seedingState = ToDatabaseValue(seeding.State);
            await using (var update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText = """
                    UPDATE download_jobs
                    SET state = $state, progress = $progress,
                        downloaded_bytes = $downloaded_bytes, total_bytes = $total_bytes,
                        speed_bytes_per_second = $speed, eta_seconds = $eta_seconds,
                        seeds = $seeds, peers = $peers, snapshot_at_utc = $now,
                        seeding_state = $seeding_state,
                        seeding_elapsed_seconds = $seeding_elapsed_seconds,
                        seeding_completed_at_utc = CASE
                            WHEN $seeding_state = 'completed'
                                THEN COALESCE(seeding_completed_at_utc, $now)
                            ELSE NULL
                        END,
                        is_stale = 0, revision = revision + 1, updated_at_utc = $now
                    WHERE id = $id;
                    """;
                update.Parameters.AddWithValue("$state", ToDatabaseValue(snapshot.State));
                update.Parameters.AddWithValue("$progress", Math.Clamp(snapshot.Progress, 0, 1));
                update.Parameters.AddWithValue("$downloaded_bytes", Math.Max(0, snapshot.DownloadedBytes));
                update.Parameters.AddWithValue("$total_bytes", Math.Max(0, snapshot.TotalBytes));
                update.Parameters.AddWithValue("$speed", Math.Max(0, snapshot.DownloadSpeedBytesPerSecond));
                update.Parameters.AddWithValue("$eta_seconds", (object?)snapshot.EtaSeconds ?? DBNull.Value);
                update.Parameters.AddWithValue("$seeds", Math.Max(0, snapshot.Seeds));
                update.Parameters.AddWithValue("$peers", Math.Max(0, snapshot.Peers));
                update.Parameters.AddWithValue("$seeding_state", seedingState);
                update.Parameters.AddWithValue("$seeding_elapsed_seconds", seeding.ElapsedSeconds);
                update.Parameters.AddWithValue("$now", now);
                update.Parameters.AddWithValue("$id", job.JobId);
                await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            var snapshotState = ToDatabaseValue(snapshot.State);
            if (job.IsStale || !string.Equals(job.State, snapshotState, StringComparison.Ordinal))
            {
                await InsertEventAsync(
                    connection, transaction, job.JobId,
                    "snapshot_sync", "observed",
                    job.State, snapshotState, null,
                    now, cancellationToken).ConfigureAwait(false);
            }

            if (!string.Equals(job.SeedingState, seedingState, StringComparison.Ordinal))
            {
                await InsertEventAsync(
                    connection, transaction, job.JobId,
                    "seeding_state", "observed",
                    job.SeedingState, seedingState, null,
                    now, cancellationToken).ConfigureAwait(false);
            }

            await using var updateTask = connection.CreateCommand();
            updateTask.Transaction = transaction;
            updateTask.CommandText = """
                UPDATE ingest_tasks
                SET status = CASE
                        WHEN status IN ('organizing_cleanup', 'organized') THEN status
                        ELSE $status
                    END,
                    updated_at_utc = $now
                WHERE id = $task_id;
                """;
            updateTask.Parameters.AddWithValue("$status", ToBusinessStatus(snapshot.State));
            updateTask.Parameters.AddWithValue("$now", now);
            updateTask.Parameters.AddWithValue("$task_id", job.TaskId);
            await updateTask.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await UpsertRuntimeStateAsync(connection, transaction, downloaderId, connected: true, null, now, cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new DownloadSyncResult(jobs.Count, matched);
    }

    public async Task MarkInstanceUnavailableAsync(
        string downloaderId,
        string safeFailureCode,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        StableErrorCode.Require(safeFailureCode, nameof(safeFailureCode));
        var now = utcNow.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (Microsoft.Data.Sqlite.SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        await UpsertRuntimeStateAsync(
            connection,
            transaction,
            downloaderId,
            connected: false,
            safeFailureCode,
            now,
            cancellationToken).ConfigureAwait(false);
        await using var stale = connection.CreateCommand();
        stale.Transaction = transaction;
        stale.CommandText = """
            UPDATE download_jobs
            SET is_stale = 1, revision = revision + 1, updated_at_utc = $now
            WHERE downloader_id = $downloader_id
              AND task_id IN (
                  SELECT id FROM ingest_tasks WHERE status IN ('download_queued', 'downloading', 'downloaded', 'download_error'));
            """;
        stale.Parameters.AddWithValue("$now", now);
        stale.Parameters.AddWithValue("$downloader_id", downloaderId);
        await stale.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DownloadJobListItemRecord>> ListAsync(
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 500);
        var page = await ListPageAsync(
            new DownloadJobListQuery(1, limit, null, null, null, null, null, "created", "desc"),
            cancellationToken).ConfigureAwait(false);
        return page.Items;
    }

    public async Task<DownloadJobListPage> ListPageAsync(
        DownloadJobListQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(query.Page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(query.PageSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(query.PageSize, 500);
        var search = NormalizeSearch(query.Search);
        var state = NormalizeFilter(query.State, nameof(query.State));
        var businessStatus = NormalizeFilter(query.BusinessStatus, nameof(query.BusinessStatus));
        var downloaderId = NormalizeFilter(query.DownloaderId, nameof(query.DownloaderId));
        var sourceId = NormalizeFilter(query.SourceId, nameof(query.SourceId));
        var sort = NormalizeSort(query.Sort);
        var direction = NormalizeDirection(query.Direction);
        var summaryBucket = NormalizeSummaryBucket(query.SummaryBucket);
        var where = new List<string>();
        if (search is not null)
        {
            where.Add("""
                (ingest_tasks.title LIKE $search ESCAPE '\' COLLATE NOCASE
                 OR download_jobs.task_id LIKE $search ESCAPE '\' COLLATE NOCASE
                 OR download_jobs.info_hash LIKE $search ESCAPE '\' COLLATE NOCASE)
                """);
        }

        if (state is not null)
        {
            where.Add("download_jobs.state = $state");
        }

        if (businessStatus is not null)
        {
            where.Add("ingest_tasks.status = $business_status");
        }

        if (downloaderId is not null)
        {
            where.Add("download_jobs.downloader_id = $downloader_id");
        }

        if (sourceId is not null)
        {
            where.Add("ingest_tasks.source_id = $source_id");
        }

        if (summaryBucket is not null)
        {
            where.Add(SummaryBucketPredicate(summaryBucket));
        }

        var whereSql = where.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", where);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        int totalItems;
        await using (var count = connection.CreateCommand())
        {
            count.CommandText = """
                SELECT COUNT(*)
                FROM download_jobs
                JOIN ingest_tasks ON ingest_tasks.id = download_jobs.task_id
                """ + whereSql + ";";
            AddListParameters(count, search, state, businessStatus, downloaderId, sourceId);
            totalItems = Convert.ToInt32(
                await count.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
                CultureInfo.InvariantCulture);
        }

        var summary = await ReadDashboardSummaryAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = ListProjectionSelect + whereSql
            + BuildOrderBy(sort, direction)
            + " LIMIT $limit OFFSET $offset;";
        AddListParameters(command, search, state, businessStatus, downloaderId, sourceId);
        command.Parameters.AddWithValue("$limit", query.PageSize);
        command.Parameters.AddWithValue("$offset", checked((query.Page - 1) * query.PageSize));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var results = new List<DownloadJobListItemRecord>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(ReadListItem(reader));
        }

        return new DownloadJobListPage(query.Page, query.PageSize, totalItems, summary, results);
    }

    public async Task<DownloadJobDetailRecord?> GetDetailAsync(
        string jobId,
        int eventLimit = 100,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        ArgumentOutOfRangeException.ThrowIfLessThan(eventLimit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(eventLimit, 500);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        DownloadJobListItemRecord? summary;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = ListProjectionSelect + " WHERE download_jobs.id = $job_id;";
            command.Parameters.AddWithValue("$job_id", jobId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            summary = await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
                ? ReadListItem(reader)
                : null;
        }

        if (summary is null)
        {
            return null;
        }

        string? taskFailureKind;
        string? taskFailureReason;
        string preparationState;
        int preparationAttemptCount;
        DateTimeOffset? preparationNextAttemptAtUtc;
        string? preparationFailureCode;
        string organizationState;
        int organizationAttemptCount;
        DateTimeOffset? organizationNextAttemptAtUtc;
        string? organizationFailureCode;
        string organizationPhase;
        int organizationCompletedUnits;
        int organizationTotalUnits;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT task.failure_kind, task.failure_reason,
                       job.preparation_state, job.preparation_attempt_count,
                       job.preparation_next_attempt_at_utc, job.preparation_failure_code,
                       job.organization_state, job.organization_attempt_count,
                       job.organization_next_attempt_at_utc, job.organization_failure_code,
                       job.organization_phase, job.organization_completed_units,
                       job.organization_total_units
                FROM download_jobs AS job
                JOIN ingest_tasks AS task ON task.id = job.task_id
                WHERE job.id = $job_id;
                """;
            command.Parameters.AddWithValue("$job_id", jobId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            taskFailureKind = reader.IsDBNull(0) ? null : reader.GetString(0);
            taskFailureReason = reader.IsDBNull(1) ? null : reader.GetString(1);
            preparationState = reader.GetString(2);
            preparationAttemptCount = reader.GetInt32(3);
            preparationNextAttemptAtUtc = ReadDateTimeOffset(reader, 4);
            preparationFailureCode = reader.IsDBNull(5) ? null : reader.GetString(5);
            organizationState = reader.GetString(6);
            organizationAttemptCount = reader.GetInt32(7);
            organizationNextAttemptAtUtc = ReadDateTimeOffset(reader, 8);
            organizationFailureCode = reader.IsDBNull(9) ? null : reader.GetString(9);
            organizationPhase = reader.GetString(10);
            organizationCompletedUnits = reader.GetInt32(11);
            organizationTotalUnits = reader.GetInt32(12);
        }

        var files = new List<DownloadJobFileRecord>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT file.relative_path, file.size_bytes, file.download_file_index,
                       file.download_priority, file.download_wanted,
                       file.disposition, file.other_reason
                FROM task_files AS file
                JOIN download_jobs AS job ON job.task_id = file.task_id
                WHERE job.id = $job_id
                ORDER BY COALESCE(file.download_file_index, 2147483647),
                         file.relative_path COLLATE NOCASE, file.id;
                """;
            command.Parameters.AddWithValue("$job_id", jobId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                files.Add(new DownloadJobFileRecord(
                    reader.GetString(0),
                    reader.GetInt64(1),
                    reader.IsDBNull(2) ? null : reader.GetInt32(2),
                    reader.IsDBNull(3) ? null : reader.GetInt32(3),
                    reader.IsDBNull(4) ? null : reader.GetInt64(4) != 0,
                    reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6)));
            }
        }

        var events = new List<DownloadJobEventRecord>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT id, kind, result, from_state, to_state, failure_code, created_at_utc
                FROM download_job_events
                WHERE job_id = $job_id
                ORDER BY created_at_utc DESC, id DESC
                LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$job_id", jobId);
            command.Parameters.AddWithValue("$limit", eventLimit);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                events.Add(new DownloadJobEventRecord(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    ReadDateTimeOffset(reader, 6)!.Value));
            }
        }

        return new DownloadJobDetailRecord(
            summary,
            taskFailureKind,
            taskFailureReason,
            preparationState,
            preparationAttemptCount,
            preparationNextAttemptAtUtc,
            preparationFailureCode,
            organizationState,
            organizationAttemptCount,
            organizationNextAttemptAtUtc,
            organizationFailureCode,
            organizationPhase,
            organizationCompletedUnits,
            organizationTotalUnits,
            files,
            events);
    }

    public async Task<DownloadJobControlTarget?> GetControlTargetAsync(
        string jobId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT job.id, job.task_id, job.downloader_id, job.info_hash,
                   job.state, task.status, job.revision,
                   job.preparation_state, job.preparation_lease_token,
                   job.preparation_failure_code,
                   job.organization_state, job.organization_lease_token,
                   job.organization_failure_code
            FROM download_jobs AS job
            JOIN ingest_tasks AS task ON task.id = job.task_id
            WHERE job.id = $job_id;
            """;
        command.Parameters.AddWithValue("$job_id", jobId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new DownloadJobControlTarget(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetInt64(6),
            reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.GetString(10),
            reader.IsDBNull(11) ? null : reader.GetString(11),
            reader.IsDBNull(12) ? null : reader.GetString(12));
    }

    public async Task<DownloadJobControlUpdateResult> ApplyRemoteControlAsync(
        DownloadJobControlTarget target,
        string kind,
        string targetState,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ValidateStableValue(kind, nameof(kind));
        ValidateStableValue(targetState, nameof(targetState));
        var now = Format(utcNow);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (Microsoft.Data.Sqlite.SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        string? currentState = null;
        long currentRevision = 0;
        await using (var query = connection.CreateCommand())
        {
            query.Transaction = transaction;
            query.CommandText = "SELECT state, revision FROM download_jobs WHERE id = $job_id;";
            query.Parameters.AddWithValue("$job_id", target.JobId);
            await using var reader = await query.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                currentState = reader.GetString(0);
                currentRevision = reader.GetInt64(1);
            }
        }

        if (currentState is null)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return DownloadJobControlUpdateResult.NotFound;
        }

        if (currentRevision != target.Revision)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return DownloadJobControlUpdateResult.RevisionConflict;
        }

        if (!IsRemoteTransitionAllowed(kind, currentState))
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return DownloadJobControlUpdateResult.InvalidState;
        }

        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE download_jobs
                SET state = $state, failure_reason = NULL,
                    revision = revision + 1, updated_at_utc = $now
                WHERE id = $job_id AND revision = $revision;
                """;
            update.Parameters.AddWithValue("$state", targetState);
            update.Parameters.AddWithValue("$now", now);
            update.Parameters.AddWithValue("$job_id", target.JobId);
            update.Parameters.AddWithValue("$revision", target.Revision);
            if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return DownloadJobControlUpdateResult.RevisionConflict;
            }
        }

        if (kind == "retry_download")
        {
            await using var updateTask = connection.CreateCommand();
            updateTask.Transaction = transaction;
            updateTask.CommandText = """
                UPDATE ingest_tasks
                SET status = 'download_queued', failure_kind = NULL,
                    failure_reason = NULL, updated_at_utc = $now
                WHERE id = $task_id AND status = 'download_error';
                """;
            updateTask.Parameters.AddWithValue("$now", now);
            updateTask.Parameters.AddWithValue("$task_id", target.TaskId);
            await updateTask.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await InsertEventAsync(
            connection, transaction, target.JobId, kind, "succeeded",
            currentState, targetState, null, now, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return DownloadJobControlUpdateResult.Updated;
    }

    public async Task<DownloadJobControlUpdateResult> RetryBusinessStageAsync(
        DownloadJobControlTarget target,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        var now = Format(utcNow);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (Microsoft.Data.Sqlite.SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        string? kind = null;
        string? fromState = null;
        string? toState = null;
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            if (target.PreparationState == "pending"
                && target.PreparationLeaseToken is null
                && target.PreparationFailureCode is not null)
            {
                kind = "retry_preparation";
                fromState = target.PreparationState;
                toState = "pending";
                command.CommandText = """
                    UPDATE download_jobs
                    SET preparation_next_attempt_at_utc = $now,
                        preparation_failure_code = NULL,
                        revision = revision + 1, updated_at_utc = $now
                    WHERE id = $job_id AND revision = $revision
                      AND preparation_state = 'pending'
                      AND preparation_lease_token IS NULL
                      AND preparation_failure_code IS NOT NULL;
                    """;
            }
            else if ((target.OrganizationState is "pending" or "cleanup")
                     && target.OrganizationLeaseToken is null
                     && target.OrganizationFailureCode is not null)
            {
                kind = "retry_organization";
                fromState = target.OrganizationState;
                toState = target.OrganizationState;
                command.CommandText = """
                    UPDATE download_jobs
                    SET organization_next_attempt_at_utc = $now,
                        organization_failure_code = NULL,
                        revision = revision + 1, updated_at_utc = $now
                    WHERE id = $job_id AND revision = $revision
                      AND organization_state = $organization_state
                      AND organization_lease_token IS NULL
                      AND organization_failure_code IS NOT NULL;
                    """;
                command.Parameters.AddWithValue("$organization_state", target.OrganizationState);
            }
            else
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return DownloadJobControlUpdateResult.InvalidState;
            }

            command.Parameters.AddWithValue("$now", now);
            command.Parameters.AddWithValue("$job_id", target.JobId);
            command.Parameters.AddWithValue("$revision", target.Revision);
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return DownloadJobControlUpdateResult.RevisionConflict;
            }
        }

        await using (var updateTask = connection.CreateCommand())
        {
            updateTask.Transaction = transaction;
            updateTask.CommandText = """
                UPDATE ingest_tasks
                SET failure_kind = NULL, failure_reason = NULL, updated_at_utc = $now
                WHERE id = $task_id;
                """;
            updateTask.Parameters.AddWithValue("$now", now);
            updateTask.Parameters.AddWithValue("$task_id", target.TaskId);
            await updateTask.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await InsertEventAsync(
            connection, transaction, target.JobId, kind!, "scheduled",
            fromState, toState, null, now, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return DownloadJobControlUpdateResult.Updated;
    }

    public async Task RecordControlFailureAsync(
        string jobId,
        string kind,
        string failureCode,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        ValidateStableValue(kind, nameof(kind));
        ValidateStableValue(failureCode, nameof(failureCode), 128);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (Microsoft.Data.Sqlite.SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await InsertEventAsync(
            connection, transaction, jobId, kind, "failed",
            null, null, failureCode, Format(utcNow), cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private const string ListProjectionSelect = """
            SELECT download_jobs.id, download_jobs.task_id, ingest_tasks.title,
                   ingest_tasks.source_id, download_jobs.downloader_id, download_jobs.info_hash,
                   download_jobs.state, ingest_tasks.status, download_jobs.progress,
                   download_jobs.downloaded_bytes, download_jobs.total_bytes,
                   download_jobs.speed_bytes_per_second, download_jobs.eta_seconds,
                   download_jobs.seeds, download_jobs.peers,
                   download_jobs.seeding_state, download_jobs.seeding_target_minutes,
                   download_jobs.seeding_elapsed_seconds,
                   download_jobs.seeding_completed_at_utc,
                   download_jobs.dynamic_tags_json,
                   download_jobs.dynamic_tag_state,
                   download_jobs.dynamic_tag_failure_code,
                   download_jobs.is_stale,
                   download_jobs.revision, download_jobs.snapshot_at_utc,
                   ingest_tasks.created_at_utc,
                   download_jobs.updated_at_utc,
                   COALESCE(downloader_runtime_state.connected, 0),
                   downloader_runtime_state.failure_code,
                   downloader_runtime_state.last_success_at_utc
            FROM download_jobs
            JOIN ingest_tasks ON ingest_tasks.id = download_jobs.task_id
            LEFT JOIN downloader_runtime_state
              ON downloader_runtime_state.downloader_id = download_jobs.downloader_id
            """;

    private static async Task<DownloadJobDashboardSummary> ReadDashboardSummaryAsync(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*),
                   COALESCE(SUM(CASE
                       WHEN job.state IN ('waiting', 'downloading', 'moving', 'seeding') THEN 1
                       ELSE 0 END), 0),
                   COALESCE(SUM(CASE WHEN job.state = 'paused' THEN 1 ELSE 0 END), 0),
                   COALESCE(SUM(CASE
                       WHEN job.state = 'error'
                         OR task.status = 'download_error'
                         OR job.preparation_failure_code IS NOT NULL
                         OR job.organization_failure_code IS NOT NULL THEN 1
                       ELSE 0 END), 0),
                   COALESCE(SUM(job.is_stale), 0),
                   COALESCE(SUM(CASE
                       WHEN task.status = 'downloaded'
                         OR job.organization_state IN ('pending', 'organizing', 'cleanup') THEN 1
                       ELSE 0 END), 0),
                   COALESCE(SUM(CASE WHEN task.status = 'organized' THEN 1 ELSE 0 END), 0),
                   COALESCE(SUM(CASE
                       WHEN job.preparation_failure_code IS NOT NULL THEN 1 ELSE 0 END), 0),
                   COALESCE(SUM(CASE
                       WHEN job.organization_failure_code IS NOT NULL THEN 1 ELSE 0 END), 0),
                   COALESCE(SUM(CASE
                       WHEN job.is_stale = 0 AND COALESCE(runtime.connected, 0) = 1
                       THEN job.speed_bytes_per_second ELSE 0 END), 0),
                   COUNT(DISTINCT CASE
                       WHEN COALESCE(runtime.connected, 0) = 0 THEN job.downloader_id END),
                   (
                       SELECT COALESCE(
                           latest_job.organization_failure_code,
                           latest_job.preparation_failure_code,
                           latest_task.failure_kind,
                           latest_runtime.failure_code)
                       FROM download_jobs AS latest_job
                       JOIN ingest_tasks AS latest_task ON latest_task.id = latest_job.task_id
                       LEFT JOIN downloader_runtime_state AS latest_runtime
                         ON latest_runtime.downloader_id = latest_job.downloader_id
                       WHERE latest_job.organization_failure_code IS NOT NULL
                          OR latest_job.preparation_failure_code IS NOT NULL
                          OR latest_task.failure_kind IS NOT NULL
                          OR latest_runtime.failure_code IS NOT NULL
                       ORDER BY latest_job.updated_at_utc DESC, latest_job.id
                       LIMIT 1
                   ),
                   MAX(runtime.last_success_at_utc)
            FROM download_jobs AS job
            JOIN ingest_tasks AS task ON task.id = job.task_id
            LEFT JOIN downloader_runtime_state AS runtime
              ON runtime.downloader_id = job.downloader_id;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Download dashboard summary query returned no row.");
        }

        return new DownloadJobDashboardSummary(
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetInt32(3),
            reader.GetInt32(4),
            reader.GetInt32(5),
            reader.GetInt32(6),
            reader.GetInt32(7),
            reader.GetInt32(8),
            reader.GetInt64(9),
            reader.GetInt32(10),
            reader.IsDBNull(11) ? null : reader.GetString(11),
            ReadDateTimeOffset(reader, 12));
    }

    private static DownloadJobListItemRecord ReadListItem(Microsoft.Data.Sqlite.SqliteDataReader reader) =>
        new(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetDouble(8),
            reader.GetInt64(9),
            reader.GetInt64(10),
            reader.GetInt64(11),
            reader.IsDBNull(12) ? null : reader.GetInt64(12),
            reader.GetInt32(13),
            reader.GetInt32(14),
            reader.GetString(15),
            reader.GetInt32(16),
            reader.GetInt64(17),
            ReadDateTimeOffset(reader, 18),
            JsonSerializer.Deserialize(reader.GetString(19), DataJsonContext.Default.StringArray) ?? [],
            reader.GetString(20),
            reader.IsDBNull(21) ? null : reader.GetString(21),
            reader.GetInt64(22) != 0,
            reader.GetInt64(23),
            ReadDateTimeOffset(reader, 24),
            ReadDateTimeOffset(reader, 25)!.Value,
            ReadDateTimeOffset(reader, 26)!.Value,
            reader.GetInt64(27) != 0,
            reader.IsDBNull(28) ? null : reader.GetString(28),
            ReadDateTimeOffset(reader, 29));

    private static string NormalizeSort(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "created" => "created",
            "updated" => "updated",
            "priority" => "priority",
            _ => throw new ArgumentException("Unsupported download sort field.", nameof(value)),
        };

    private static string NormalizeDirection(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "desc" => "DESC",
            "asc" => "ASC",
            _ => throw new ArgumentException("Unsupported download sort direction.", nameof(value)),
        };

    private static string BuildOrderBy(string sort, string direction) => sort switch
    {
        "created" => $" ORDER BY ingest_tasks.created_at_utc {direction}, download_jobs.id {direction}",
        "updated" => $" ORDER BY download_jobs.updated_at_utc {direction}, download_jobs.id {direction}",
        "priority" => $"""
             ORDER BY CASE
                 WHEN download_jobs.state = 'error'
                   OR ingest_tasks.status = 'download_error'
                   OR download_jobs.preparation_failure_code IS NOT NULL
                   OR download_jobs.organization_failure_code IS NOT NULL THEN 0
                 WHEN download_jobs.state IN ('waiting', 'downloading', 'moving', 'seeding', 'paused')
                   OR ingest_tasks.status NOT IN ('organized', 'download_skipped_duplicate') THEN 1
                 ELSE 2
             END, download_jobs.updated_at_utc {direction}, download_jobs.id {direction}
             """,
        _ => throw new InvalidOperationException("Unexpected normalized download sort field."),
    };

    private static string? NormalizeSearch(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length > 200 || normalized.Any(char.IsControl))
        {
            throw new ArgumentException("Search must be at most 200 printable characters.", nameof(value));
        }

        return string.Concat(
            "%",
            normalized
                .Replace(@"\", @"\\", StringComparison.Ordinal)
                .Replace("%", @"\%", StringComparison.Ordinal)
                .Replace("_", @"\_", StringComparison.Ordinal),
            "%");
    }

    private static string? NormalizeFilter(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().ToLowerInvariant();
        ValidateStableValue(normalized, parameterName);
        return normalized;
    }

    private static string? NormalizeSummaryBucket(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().ToLowerInvariant() switch
            {
                "active" => "active",
                "paused" => "paused",
                "failed" => "failed",
                "waiting_organization" => "waiting_organization",
                "completed" => "completed",
                "stale" => "stale",
                _ => throw new ArgumentException(
                    "Unsupported download summary bucket.",
                    nameof(value)),
            };

    private static string SummaryBucketPredicate(string bucket) => bucket switch
    {
        "active" => "download_jobs.state IN ('waiting', 'downloading', 'moving', 'seeding')",
        "paused" => "download_jobs.state = 'paused'",
        "failed" => """
            (download_jobs.state = 'error'
             OR ingest_tasks.status = 'download_error'
             OR download_jobs.preparation_failure_code IS NOT NULL
             OR download_jobs.organization_failure_code IS NOT NULL)
            """,
        "waiting_organization" => """
            (ingest_tasks.status = 'downloaded'
             OR download_jobs.organization_state IN ('pending', 'organizing', 'cleanup'))
            """,
        "completed" => "ingest_tasks.status = 'organized'",
        "stale" => "download_jobs.is_stale = 1",
        _ => throw new InvalidOperationException("Unexpected normalized download summary bucket."),
    };

    private static void AddListParameters(
        Microsoft.Data.Sqlite.SqliteCommand command,
        string? search,
        string? state,
        string? businessStatus,
        string? downloaderId,
        string? sourceId)
    {
        if (search is not null)
        {
            command.Parameters.AddWithValue("$search", search);
        }

        if (state is not null)
        {
            command.Parameters.AddWithValue("$state", state);
        }

        if (businessStatus is not null)
        {
            command.Parameters.AddWithValue("$business_status", businessStatus);
        }

        if (downloaderId is not null)
        {
            command.Parameters.AddWithValue("$downloader_id", downloaderId);
        }

        if (sourceId is not null)
        {
            command.Parameters.AddWithValue("$source_id", sourceId);
        }
    }

    private static bool IsRemoteTransitionAllowed(string kind, string currentState) =>
        kind switch
        {
            "pause" => currentState is "waiting" or "downloading" or "moving" or "seeding",
            "resume" => currentState == "paused",
            "retry_download" => currentState == "error",
            _ => false,
        };

    private static async Task InsertEventAsync(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        Microsoft.Data.Sqlite.SqliteTransaction transaction,
        string jobId,
        string kind,
        string result,
        string? fromState,
        string? toState,
        string? failureCode,
        string now,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO download_job_events (
                id, job_id, kind, result, from_state, to_state, failure_code, created_at_utc)
            VALUES (
                $id, $job_id, $kind, $result, $from_state, $to_state, $failure_code, $now);
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("$job_id", jobId);
        command.Parameters.AddWithValue("$kind", kind);
        command.Parameters.AddWithValue("$result", result);
        command.Parameters.AddWithValue("$from_state", (object?)fromState ?? DBNull.Value);
        command.Parameters.AddWithValue("$to_state", (object?)toState ?? DBNull.Value);
        command.Parameters.AddWithValue("$failure_code", (object?)failureCode ?? DBNull.Value);
        command.Parameters.AddWithValue("$now", now);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateStableValue(string value, string parameterName, int maximumLength = 64)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > maximumLength
            || value.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.')))
        {
            throw new ArgumentException(
                $"{parameterName} must be a stable ASCII identifier.",
                parameterName);
        }
    }

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static async Task UpsertRuntimeStateAsync(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        Microsoft.Data.Sqlite.SqliteTransaction transaction,
        string downloaderId,
        bool connected,
        string? failureCode,
        string now,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO downloader_runtime_state (
                downloader_id, connected, failure_code, last_success_at_utc, updated_at_utc)
            VALUES (
                $downloader_id, $connected, $failure_code, $last_success_at_utc, $updated_at_utc)
            ON CONFLICT(downloader_id) DO UPDATE SET
                connected = excluded.connected,
                failure_code = excluded.failure_code,
                last_success_at_utc = COALESCE(excluded.last_success_at_utc, downloader_runtime_state.last_success_at_utc),
                updated_at_utc = excluded.updated_at_utc;
            """;
        command.Parameters.AddWithValue("$downloader_id", downloaderId);
        command.Parameters.AddWithValue("$connected", connected ? 1 : 0);
        command.Parameters.AddWithValue("$failure_code", (object?)failureCode ?? DBNull.Value);
        command.Parameters.AddWithValue("$last_success_at_utc", connected ? now : DBNull.Value);
        command.Parameters.AddWithValue("$updated_at_utc", now);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string ToDatabaseValue(DownloadTaskState state) => state switch
    {
        DownloadTaskState.Waiting => "waiting",
        DownloadTaskState.Downloading => "downloading",
        DownloadTaskState.Moving => "moving",
        DownloadTaskState.Seeding => "seeding",
        DownloadTaskState.Paused => "paused",
        DownloadTaskState.Complete => "complete",
        DownloadTaskState.Error => "error",
        _ => "unknown",
    };

    private static string ToDatabaseValue(DownloadSeedingState state) => state switch
    {
        DownloadSeedingState.NotRequired => "not_required",
        DownloadSeedingState.Waiting => "waiting",
        DownloadSeedingState.Seeding => "seeding",
        DownloadSeedingState.Completed => "completed",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    private static DownloadSeedingState ParseSeedingState(string value) => value switch
    {
        "not_required" => DownloadSeedingState.NotRequired,
        "waiting" => DownloadSeedingState.Waiting,
        "seeding" => DownloadSeedingState.Seeding,
        "completed" => DownloadSeedingState.Completed,
        _ => throw new InvalidOperationException("Persisted seeding state is invalid."),
    };

    private static string ToBusinessStatus(DownloadTaskState state) => state switch
    {
        DownloadTaskState.Downloading or DownloadTaskState.Moving => "downloading",
        DownloadTaskState.Seeding or DownloadTaskState.Complete => "downloaded",
        DownloadTaskState.Error => "download_error",
        _ => "download_queued",
    };

    private static DateTimeOffset? ReadDateTimeOffset(Microsoft.Data.Sqlite.SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : DateTimeOffset.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
