using System.Globalization;
using System.Text;
using System.Text.Json;
using AnimeGoNet.Core.Ingest;
using AnimeGoNet.Core.Torrents;
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
            await using var fileCommand = connection.CreateCommand();
            fileCommand.Transaction = transaction;
            fileCommand.CommandText = """
                INSERT INTO task_files (
                    id, task_id, relative_path, size_bytes, source_episode,
                    file_episode_candidate, tmdb_series_id, tmdb_season_number,
                    tmdb_episode_number, tmdb_episode_id, disposition, other_reason)
                VALUES (
                    $id, $task_id, $relative_path, $size_bytes, NULL,
                    NULL, NULL, NULL, NULL, NULL, $disposition, $other_reason);
                """;
            fileCommand.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            fileCommand.Parameters.AddWithValue("$task_id", id);
            fileCommand.Parameters.AddWithValue("$relative_path", file.RelativePath);
            fileCommand.Parameters.AddWithValue("$size_bytes", file.Size);
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
                  AND ingest_tasks.status = 'staged'
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
                WHERE status = 'staged'
                  AND id IN (SELECT task_id FROM staged_torrents WHERE expires_at_utc <= $now);

                DELETE FROM staged_torrents
                WHERE expires_at_utc <= $now
                  AND task_id IN (SELECT id FROM ingest_tasks WHERE status = 'failed' AND failure_kind = 'staging_expired');
                """;
            update.Parameters.AddWithValue("$now", now);
            await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return expired;
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
}
