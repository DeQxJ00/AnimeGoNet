using System.Globalization;
using AnimeGoNet.Core.Library;

namespace AnimeGoNet.Data.Library;

public sealed class CompletionRecordStore(Sqlite.AnimeGoSqliteDatabase database)
{
    public async Task<bool> TryAddAsync(
        CompletionRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO completion_records(
                id,
                tmdb_series_id,
                tmdb_season_number,
                tmdb_episode_number,
                source_id,
                source_item_id,
                completed_at_utc)
            VALUES ($id, $seriesId, $seasonNumber, $episodeNumber, $sourceId, $sourceItemId, $completedAtUtc);
            """;
        command.Parameters.AddWithValue("$id", record.Id);
        command.Parameters.AddWithValue("$seriesId", record.Episode.SeriesId);
        command.Parameters.AddWithValue("$seasonNumber", record.Episode.SeasonNumber);
        command.Parameters.AddWithValue("$episodeNumber", record.Episode.EpisodeNumber);
        command.Parameters.AddWithValue("$sourceId", record.SourceId.ToLowerInvariant());
        command.Parameters.AddWithValue("$sourceItemId", (object?)record.SourceItemId ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$completedAtUtc",
            record.CompletedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    public async Task<bool> ExistsAsync(
        TmdbEpisodeIdentity episode,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS(
                SELECT 1
                FROM completion_records
                WHERE tmdb_series_id = $seriesId
                  AND tmdb_season_number = $seasonNumber
                  AND tmdb_episode_number = $episodeNumber);
            """;
        command.Parameters.AddWithValue("$seriesId", episode.SeriesId);
        command.Parameters.AddWithValue("$seasonNumber", episode.SeasonNumber);
        command.Parameters.AddWithValue("$episodeNumber", episode.EpisodeNumber);
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt64(result, CultureInfo.InvariantCulture) == 1;
    }
}
