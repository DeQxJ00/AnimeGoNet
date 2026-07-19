using System.Globalization;
using System.Text;
using System.Text.Json;
using AnimeGoNet.Core.Ingest;
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
            writer.WriteBoolean("rss_filter_enabled", profile.RssFilterEnabled);
            writer.WriteBoolean("rss_priority_enabled", profile.RssPriorityEnabled);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
