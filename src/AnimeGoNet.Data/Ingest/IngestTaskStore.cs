using System.Globalization;
using System.Text;
using System.Text.Json;
using AnimeGoNet.Core.Ingest;
using AnimeGoNet.Core.Torrents;
using AnimeGoNet.Core.Downloads;
using AnimeGoNet.Core.Metadata;
using AnimeGoNet.Data.Sources;
using AnimeGoNet.Data.Sqlite;

namespace AnimeGoNet.Data.Ingest;

public sealed class IngestTaskStore(AnimeGoSqliteDatabase database)
{
    public async Task<IngestTaskRecord> AddAsync(
        NormalizedIngestItem item,
        SourceProfileRecord profile,
        CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ingest_tasks (
                id, source_profile_id, source_profile_revision, source_id,
                source_item_id, source_work_id, mikanid, groupid,
                bangumi_subject_id, anidb_id, imdb_id, title,
                torrent_url_fingerprint, downloader_id, route_snapshot_json,
                status, failure_kind, failure_reason, created_at_utc, updated_at_utc)
            VALUES (
                $id, $source_profile_id, $source_profile_revision, $source_id,
                $source_item_id, $source_work_id, $mikanid, NULL,
                $bangumi_subject_id, $anidb_id, $imdb_id, $title,
                $torrent_url_fingerprint, $downloader_id, $route_snapshot_json,
                'received', NULL, NULL, $created_at_utc, $updated_at_utc);
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$source_profile_id", profile.Id);
        command.Parameters.AddWithValue("$source_profile_revision", profile.Revision);
        command.Parameters.AddWithValue("$source_id", item.Source);
        command.Parameters.AddWithValue("$source_item_id", (object?)item.SourceItemId ?? DBNull.Value);
        command.Parameters.AddWithValue("$source_work_id", (object?)item.SourceWorkId ?? DBNull.Value);
        command.Parameters.AddWithValue("$mikanid", (object?)item.MikanId ?? DBNull.Value);
        command.Parameters.AddWithValue("$bangumi_subject_id", (object?)item.BangumiId ?? DBNull.Value);
        command.Parameters.AddWithValue("$anidb_id", (object?)item.AniDbId ?? DBNull.Value);
        command.Parameters.AddWithValue("$imdb_id", (object?)item.ImdbId ?? DBNull.Value);
        command.Parameters.AddWithValue("$title", item.Title);
        command.Parameters.AddWithValue("$torrent_url_fingerprint", item.TorrentUrlFingerprint);
        command.Parameters.AddWithValue("$downloader_id", profile.DownloaderId);
        command.Parameters.AddWithValue("$route_snapshot_json", CreateRouteSnapshot(profile));
        command.Parameters.AddWithValue("$created_at_utc", now);
        command.Parameters.AddWithValue("$updated_at_utc", now);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return new IngestTaskRecord(id, profile.Id, profile.Revision, profile.DownloaderId, "received");
    }

    public async Task<StagedIngestTaskRecord> AddStagedAsync(
        NormalizedIngestItem item,
        SourceProfileRecord profile,
        TorrentMetadata metadata,
        string stagingFileName,
        DateTimeOffset expiresAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(metadata);
        ValidateStagingFileName(stagingFileName);
        if (metadata.Files.Count == 0)
        {
            throw new ArgumentException("Staged Torrent must contain at least one file.", nameof(metadata));
        }

        if (metadata.InfoHash.Length != 40
            || metadata.InfoHash.Any(character => !Uri.IsHexDigit(character))
            || !string.Equals(metadata.InfoHash, metadata.InfoHash.ToLowerInvariant(), StringComparison.Ordinal))
        {
            throw new ArgumentException("Staged Torrent info hash must be 40 lowercase hexadecimal characters.", nameof(metadata));
        }

        var id = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (Microsoft.Data.Sqlite.SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO ingest_tasks (
                    id, source_profile_id, source_profile_revision, source_id,
                    source_item_id, source_work_id, mikanid, groupid,
                    bangumi_subject_id, anidb_id, imdb_id, title,
                    torrent_url_fingerprint, downloader_id, route_snapshot_json,
                    status, failure_kind, failure_reason, created_at_utc, updated_at_utc)
                VALUES (
                    $id, $source_profile_id, $source_profile_revision, $source_id,
                    $source_item_id, $source_work_id, $mikanid, NULL,
                    $bangumi_subject_id, $anidb_id, $imdb_id, $title,
                    $torrent_url_fingerprint, $downloader_id, $route_snapshot_json,
                    'staged', NULL, NULL, $created_at_utc, $updated_at_utc);
                """;
            AddTaskParameters(command, id, item, profile, now);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var file in metadata.Files)
        {
            var episode = file.IsPadding
                ? null
                : TorrentEpisodeCandidateParser.Parse(file.RelativePath);
            await using var fileCommand = connection.CreateCommand();
            fileCommand.Transaction = transaction;
            fileCommand.CommandText = """
                INSERT INTO task_files (
                    id, task_id, relative_path, size_bytes, source_episode,
                    file_episode_candidate, tmdb_series_id, tmdb_season_number,
                    tmdb_episode_number, tmdb_episode_id, disposition, other_reason)
                VALUES (
                    $id, $task_id, $relative_path, $size_bytes,
                    $source_episode, $file_episode_candidate, NULL, NULL, NULL, NULL, $disposition, $other_reason);
                """;
            fileCommand.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            fileCommand.Parameters.AddWithValue("$task_id", id);
            fileCommand.Parameters.AddWithValue("$relative_path", file.RelativePath);
            fileCommand.Parameters.AddWithValue("$size_bytes", file.Size);
            fileCommand.Parameters.AddWithValue("$source_episode", (object?)episode?.SourceEpisode ?? DBNull.Value);
            fileCommand.Parameters.AddWithValue(
                "$file_episode_candidate",
                episode?.NormalEpisode is int normalEpisode
                    ? normalEpisode.ToString(CultureInfo.InvariantCulture)
                    : DBNull.Value);
            fileCommand.Parameters.AddWithValue("$disposition", file.IsPadding ? "ignored" : "pending");
            fileCommand.Parameters.AddWithValue("$other_reason", file.IsPadding ? "padding_file" : DBNull.Value);
            await fileCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var stagingCommand = connection.CreateCommand())
        {
            stagingCommand.Transaction = transaction;
            stagingCommand.CommandText = """
                INSERT INTO staged_torrents (
                    task_id, staging_file_name, info_hash, total_size_bytes,
                    expires_at_utc, created_at_utc)
                VALUES (
                    $task_id, $staging_file_name, $info_hash, $total_size_bytes,
                    $expires_at_utc, $created_at_utc);
                """;
            stagingCommand.Parameters.AddWithValue("$task_id", id);
            stagingCommand.Parameters.AddWithValue("$staging_file_name", stagingFileName);
            stagingCommand.Parameters.AddWithValue("$info_hash", metadata.InfoHash);
            stagingCommand.Parameters.AddWithValue("$total_size_bytes", metadata.TotalSize);
            stagingCommand.Parameters.AddWithValue(
                "$expires_at_utc",
                expiresAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            stagingCommand.Parameters.AddWithValue("$created_at_utc", now);
            await stagingCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new StagedIngestTaskRecord(
            id,
            profile.Id,
            profile.Revision,
            profile.DownloaderId,
            "staged",
            metadata.InfoHash,
            metadata.Files.Count);
    }

    public async Task<IReadOnlyList<ExpiredStagedTorrentRecord>> ExpireStagedAsync(
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        var now = utcNow.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (Microsoft.Data.Sqlite.SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        var expired = new List<ExpiredStagedTorrentRecord>();
        await using (var query = connection.CreateCommand())
        {
            query.Transaction = transaction;
            query.CommandText = """
                SELECT staged_torrents.task_id, staged_torrents.staging_file_name
                FROM staged_torrents
                JOIN ingest_tasks ON ingest_tasks.id = staged_torrents.task_id
                WHERE staged_torrents.expires_at_utc <= $now
                ORDER BY staged_torrents.task_id;
                """;
            query.Parameters.AddWithValue("$now", now);
            await using var reader = await query.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                expired.Add(new ExpiredStagedTorrentRecord(reader.GetString(0), reader.GetString(1)));
            }
        }

        if (expired.Count > 0)
        {
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE ingest_tasks
                SET status = 'failed',
                    failure_kind = 'staging_expired',
                    failure_reason = 'Staged Torrent expired before downloader receipt.',
                    updated_at_utc = $now
                WHERE id IN (SELECT task_id FROM staged_torrents WHERE expires_at_utc <= $now);

                DELETE FROM staged_torrents
                WHERE expires_at_utc <= $now;
                """;
            update.Parameters.AddWithValue("$now", now);
            await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return expired;
    }

    public async Task<ClaimedStagedTorrentRecord?> TryClaimNextStagedAsync(
        DateTimeOffset utcNow,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(leaseDuration, TimeSpan.Zero);

        var now = utcNow.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        var leaseExpires = utcNow.Add(leaseDuration).ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        var leaseToken = Guid.NewGuid().ToString("N");
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (Microsoft.Data.Sqlite.SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        await using (var recover = connection.CreateCommand())
        {
            recover.Transaction = transaction;
            recover.CommandText = """
                UPDATE staged_torrents
                SET dispatch_state = 'ready', lease_token = NULL, lease_expires_at_utc = NULL,
                    last_failure_code = 'dispatch_lease_expired'
                WHERE dispatch_state = 'dispatching' AND lease_expires_at_utc <= $now;

                UPDATE ingest_tasks
                SET status = 'staged', failure_kind = 'download_dispatch_retry',
                    failure_reason = 'dispatch_lease_expired', updated_at_utc = $now
                WHERE status = 'dispatching'
                  AND id IN (SELECT task_id FROM staged_torrents WHERE dispatch_state = 'ready' AND last_failure_code = 'dispatch_lease_expired');
                """;
            recover.Parameters.AddWithValue("$now", now);
            await recover.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        string? taskId = null;
        var attemptCount = 0;
        await using (var claim = connection.CreateCommand())
        {
            claim.Transaction = transaction;
            claim.CommandText = """
                UPDATE staged_torrents
                SET dispatch_state = 'dispatching', lease_token = $lease_token,
                    lease_expires_at_utc = $lease_expires_at_utc,
                    attempt_count = attempt_count + 1,
                    last_failure_code = NULL
                WHERE task_id = (
                    SELECT task_id FROM staged_torrents
                    WHERE dispatch_state = 'ready'
                      AND expires_at_utc > $now
                      AND (next_attempt_at_utc IS NULL OR next_attempt_at_utc <= $now)
                    ORDER BY created_at_utc, task_id
                    LIMIT 1)
                  AND dispatch_state = 'ready'
                RETURNING task_id, attempt_count;
                """;
            claim.Parameters.AddWithValue("$lease_token", leaseToken);
            claim.Parameters.AddWithValue("$lease_expires_at_utc", leaseExpires);
            claim.Parameters.AddWithValue("$now", now);
            await using var reader = await claim.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                taskId = reader.GetString(0);
                attemptCount = reader.GetInt32(1);
            }
        }

        if (taskId is null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }

        ClaimedStagedTorrentRecord result;
        await using (var query = connection.CreateCommand())
        {
            query.Transaction = transaction;
            query.CommandText = """
                SELECT staged_torrents.task_id, staged_torrents.staging_file_name,
                       staged_torrents.info_hash, staged_torrents.total_size_bytes,
                       ingest_tasks.downloader_id, ingest_tasks.source_id, ingest_tasks.title,
                       json_extract(ingest_tasks.route_snapshot_json, '$.file_strategy')
                FROM staged_torrents
                JOIN ingest_tasks ON ingest_tasks.id = staged_torrents.task_id
                WHERE staged_torrents.task_id = $task_id
                  AND staged_torrents.lease_token = $lease_token;
                """;
            query.Parameters.AddWithValue("$task_id", taskId);
            query.Parameters.AddWithValue("$lease_token", leaseToken);
            await using var reader = await query.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("Claimed staged Torrent disappeared before projection.");
            }

            result = new ClaimedStagedTorrentRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt64(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                leaseToken,
                attemptCount);
        }

        await using (var updateTask = connection.CreateCommand())
        {
            updateTask.Transaction = transaction;
            updateTask.CommandText = """
                UPDATE ingest_tasks
                SET status = 'dispatching', failure_kind = NULL, failure_reason = NULL, updated_at_utc = $now
                WHERE id = $task_id AND status = 'staged';
                """;
            updateTask.Parameters.AddWithValue("$now", now);
            updateTask.Parameters.AddWithValue("$task_id", taskId);
            if (await updateTask.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            {
                throw new InvalidOperationException("Staged task was not claimable.");
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task CompleteDispatchAsync(
        ClaimedStagedTorrentRecord claim,
        DownloadTaskSnapshot snapshot,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!string.Equals(snapshot.Hash, claim.InfoHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Confirmed download hash does not match the staged Torrent.", nameof(snapshot));
        }

        var now = utcNow.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (Microsoft.Data.Sqlite.SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using (var guard = connection.CreateCommand())
        {
            guard.Transaction = transaction;
            guard.CommandText = """
                SELECT COUNT(*)
                FROM staged_torrents
                JOIN ingest_tasks ON ingest_tasks.id = staged_torrents.task_id
                WHERE staged_torrents.task_id = $task_id
                  AND staged_torrents.dispatch_state = 'dispatching'
                  AND staged_torrents.lease_token = $lease_token
                  AND ingest_tasks.status = 'dispatching';
                """;
            guard.Parameters.AddWithValue("$task_id", claim.TaskId);
            guard.Parameters.AddWithValue("$lease_token", claim.LeaseToken);
            if (Convert.ToInt32(await guard.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture) != 1)
            {
                throw new InvalidOperationException("Staged Torrent dispatch lease is no longer owned.");
            }
        }

        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO download_jobs (
                    id, task_id, downloader_id, info_hash, state, progress,
                    downloaded_bytes, total_bytes, speed_bytes_per_second,
                    eta_seconds, failure_reason, created_at_utc, updated_at_utc,
                    seeds, peers, snapshot_at_utc, is_stale, revision)
                VALUES (
                    $id, $task_id, $downloader_id, $info_hash, $state, $progress,
                    $downloaded_bytes, $total_bytes, $speed_bytes_per_second,
                    $eta_seconds, NULL, $created_at_utc, $updated_at_utc,
                    $seeds, $peers, $snapshot_at_utc, 0, 1);
                """;
            insert.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            insert.Parameters.AddWithValue("$task_id", claim.TaskId);
            insert.Parameters.AddWithValue("$downloader_id", claim.DownloaderId);
            insert.Parameters.AddWithValue("$info_hash", claim.InfoHash);
            insert.Parameters.AddWithValue("$state", ToDatabaseValue(snapshot.State));
            insert.Parameters.AddWithValue("$progress", snapshot.Progress);
            insert.Parameters.AddWithValue("$downloaded_bytes", snapshot.DownloadedBytes);
            insert.Parameters.AddWithValue("$total_bytes", snapshot.TotalBytes);
            insert.Parameters.AddWithValue("$speed_bytes_per_second", snapshot.DownloadSpeedBytesPerSecond);
            insert.Parameters.AddWithValue("$eta_seconds", (object?)snapshot.EtaSeconds ?? DBNull.Value);
            insert.Parameters.AddWithValue("$seeds", Math.Max(0, snapshot.Seeds));
            insert.Parameters.AddWithValue("$peers", Math.Max(0, snapshot.Peers));
            insert.Parameters.AddWithValue("$snapshot_at_utc", now);
            insert.Parameters.AddWithValue("$created_at_utc", now);
            insert.Parameters.AddWithValue("$updated_at_utc", now);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var finish = connection.CreateCommand())
        {
            finish.Transaction = transaction;
            finish.CommandText = """
                UPDATE ingest_tasks
                SET status = 'download_queued', failure_kind = NULL, failure_reason = NULL, updated_at_utc = $now
                WHERE id = $task_id AND status = 'dispatching';

                DELETE FROM staged_torrents
                WHERE task_id = $task_id AND dispatch_state = 'dispatching' AND lease_token = $lease_token;
                """;
            finish.Parameters.AddWithValue("$now", now);
            finish.Parameters.AddWithValue("$task_id", claim.TaskId);
            finish.Parameters.AddWithValue("$lease_token", claim.LeaseToken);
            await finish.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> ReleaseDispatchAsync(
        ClaimedStagedTorrentRecord claim,
        string safeFailureCode,
        DateTimeOffset retryAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        if (string.IsNullOrWhiteSpace(safeFailureCode)
            || safeFailureCode.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '_' or '-')))
        {
            throw new ArgumentException("Failure code must be a stable ASCII identifier.", nameof(safeFailureCode));
        }

        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (Microsoft.Data.Sqlite.SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var release = connection.CreateCommand();
        release.Transaction = transaction;
        release.CommandText = """
            UPDATE staged_torrents
            SET dispatch_state = 'ready', lease_token = NULL, lease_expires_at_utc = NULL,
                next_attempt_at_utc = $retry_at_utc, last_failure_code = $failure_code
            WHERE task_id = $task_id AND dispatch_state = 'dispatching' AND lease_token = $lease_token;
            """;
        release.Parameters.AddWithValue("$retry_at_utc", retryAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        release.Parameters.AddWithValue("$failure_code", safeFailureCode);
        release.Parameters.AddWithValue("$task_id", claim.TaskId);
        release.Parameters.AddWithValue("$lease_token", claim.LeaseToken);
        var released = await release.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
        if (released)
        {
            await using var updateTask = connection.CreateCommand();
            updateTask.Transaction = transaction;
            updateTask.CommandText = """
                UPDATE ingest_tasks
                SET status = 'staged', failure_kind = 'download_dispatch_retry',
                    failure_reason = $failure_code, updated_at_utc = $now
                WHERE id = $task_id AND status = 'dispatching';
                """;
            updateTask.Parameters.AddWithValue("$failure_code", safeFailureCode);
            updateTask.Parameters.AddWithValue("$now", now);
            updateTask.Parameters.AddWithValue("$task_id", claim.TaskId);
            await updateTask.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return released;
    }

    private static string CreateRouteSnapshot(SourceProfileRecord profile)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("source_profile_id", profile.Id);
            writer.WriteNumber("revision", profile.Revision);
            writer.WriteString("downloader_id", profile.DownloaderId);
            writer.WriteString("file_strategy", profile.FileStrategy);
            writer.WriteStartArray("allowed_torrent_hosts");
            foreach (var host in profile.AllowedTorrentHosts)
            {
                writer.WriteStringValue(host);
            }

            writer.WriteEndArray();
            writer.WriteBoolean("rss_filter_enabled", profile.RssFilterEnabled);
            writer.WriteBoolean("rss_priority_enabled", profile.RssPriorityEnabled);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void AddTaskParameters(
        Microsoft.Data.Sqlite.SqliteCommand command,
        string id,
        NormalizedIngestItem item,
        SourceProfileRecord profile,
        string now)
    {
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$source_profile_id", profile.Id);
        command.Parameters.AddWithValue("$source_profile_revision", profile.Revision);
        command.Parameters.AddWithValue("$source_id", item.Source);
        command.Parameters.AddWithValue("$source_item_id", (object?)item.SourceItemId ?? DBNull.Value);
        command.Parameters.AddWithValue("$source_work_id", (object?)item.SourceWorkId ?? DBNull.Value);
        command.Parameters.AddWithValue("$mikanid", (object?)item.MikanId ?? DBNull.Value);
        command.Parameters.AddWithValue("$bangumi_subject_id", (object?)item.BangumiId ?? DBNull.Value);
        command.Parameters.AddWithValue("$anidb_id", (object?)item.AniDbId ?? DBNull.Value);
        command.Parameters.AddWithValue("$imdb_id", (object?)item.ImdbId ?? DBNull.Value);
        command.Parameters.AddWithValue("$title", item.Title);
        command.Parameters.AddWithValue("$torrent_url_fingerprint", item.TorrentUrlFingerprint);
        command.Parameters.AddWithValue("$downloader_id", profile.DownloaderId);
        command.Parameters.AddWithValue("$route_snapshot_json", CreateRouteSnapshot(profile));
        command.Parameters.AddWithValue("$created_at_utc", now);
        command.Parameters.AddWithValue("$updated_at_utc", now);
    }

    private static void ValidateStagingFileName(string stagingFileName)
    {
        if (string.IsNullOrWhiteSpace(stagingFileName)
            || !string.Equals(stagingFileName, Path.GetFileName(stagingFileName), StringComparison.Ordinal)
            || !stagingFileName.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Staging file name must be a leaf .torrent file name.", nameof(stagingFileName));
        }
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
}
