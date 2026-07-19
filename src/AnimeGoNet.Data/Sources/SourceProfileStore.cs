using System.Globalization;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Data.Serialization;
using AnimeGoNet.Data.Sqlite;

namespace AnimeGoNet.Data.Sources;

public sealed class SourceProfileStore(AnimeGoSqliteDatabase database)
{
    public async Task EnsureSeedsAsync(
        IReadOnlyList<SourceProfileSeed> seeds,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var seed in seeds)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)transaction;
            command.CommandText = """
                INSERT INTO source_profiles (
                    id, display_name, adapter, downloader_id, file_strategy,
                    allowed_torrent_hosts_json, rss_filter_enabled, rss_priority_enabled, revision, enabled,
                    created_at_utc, updated_at_utc)
                VALUES (
                    $id, $display_name, $adapter, $downloader_id, $file_strategy,
                    $allowed_torrent_hosts_json, $rss_filter_enabled, $rss_priority_enabled, 1, 1,
                    $created_at_utc, $updated_at_utc)
                ON CONFLICT(id) DO UPDATE SET
                    allowed_torrent_hosts_json = excluded.allowed_torrent_hosts_json,
                    revision = source_profiles.revision + 1,
                    updated_at_utc = excluded.updated_at_utc
                WHERE source_profiles.allowed_torrent_hosts_json = '[]';
                """;
            command.Parameters.AddWithValue("$id", seed.Id);
            command.Parameters.AddWithValue("$display_name", seed.Id);
            command.Parameters.AddWithValue("$adapter", seed.Adapter);
            command.Parameters.AddWithValue("$downloader_id", seed.DownloaderId);
            command.Parameters.AddWithValue("$file_strategy", ToDatabaseValue(seed.FileStrategy));
            command.Parameters.AddWithValue(
                "$allowed_torrent_hosts_json",
                System.Text.Json.JsonSerializer.Serialize(seed.AllowedTorrentHosts.ToArray(), DataJsonContext.Default.StringArray));
            command.Parameters.AddWithValue("$rss_filter_enabled", seed.RssFilterEnabled ? 1 : 0);
            command.Parameters.AddWithValue("$rss_priority_enabled", seed.RssPriorityEnabled ? 1 : 0);
            command.Parameters.AddWithValue("$created_at_utc", now);
            command.Parameters.AddWithValue("$updated_at_utc", now);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<SourceProfileRecord?> GetEnabledAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, adapter, downloader_id, file_strategy,
                   allowed_torrent_hosts_json, rss_filter_enabled, rss_priority_enabled, revision
            FROM source_profiles
            WHERE id = $id AND enabled = 1;
            """;
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new SourceProfileRecord(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            System.Text.Json.JsonSerializer.Deserialize(reader.GetString(4), DataJsonContext.Default.StringArray) ?? [],
            reader.GetInt64(5) != 0,
            reader.GetInt64(6) != 0,
            reader.GetInt64(7));
    }

    private static string ToDatabaseValue(FileStrategy strategy) => strategy switch
    {
        FileStrategy.Link => "link",
        FileStrategy.LinkDelete => "link_delete",
        FileStrategy.Move => "move",
        FileStrategy.WaitMove => "wait_move",
        _ => throw new ArgumentOutOfRangeException(nameof(strategy)),
    };
}
