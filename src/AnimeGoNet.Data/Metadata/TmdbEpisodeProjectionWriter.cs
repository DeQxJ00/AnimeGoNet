using System.Globalization;
using AnimeGoNet.Core.Metadata;
using Microsoft.Data.Sqlite;

namespace AnimeGoNet.Data.Metadata;

internal static class TmdbEpisodeProjectionWriter
{
    public static async Task UpsertAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string seriesRowId,
        int tmdbSeriesId,
        int seasonNumber,
        int expectedEpisodeCount,
        IReadOnlyList<TmdbEpisode>? episodes,
        string fetchedAtUtc,
        CancellationToken cancellationToken)
    {
        if (episodes is null)
        {
            return;
        }

        if (tmdbSeriesId <= 0
            || seasonNumber <= 0
            || expectedEpisodeCount != episodes.Count
            || episodes.Select(value => value.Id).Distinct().Count() != episodes.Count
            || episodes.Select(value => value.EpisodeNumber).Distinct().Count() != episodes.Count
            || episodes.Any(value =>
                value.Id <= 0
                || value.SeriesId != tmdbSeriesId
                || value.SeasonNumber != seasonNumber
                || value.EpisodeNumber <= 0))
        {
            throw new ArgumentException("TMDB Season Episode snapshot identity is invalid.", nameof(episodes));
        }

        foreach (var episode in episodes)
        {
            await EnsureEpisodeIdIdentityAsync(
                connection,
                transaction,
                seriesRowId,
                episode,
                cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO tmdb_episodes (
                    tmdb_episode_id, series_id, season_number, episode_number,
                    name, air_date, runtime_minutes, fetched_at_utc)
                VALUES (
                    $tmdb_episode_id, $series_id, $season_number, $episode_number,
                    $name, $air_date, NULL, $fetched_at_utc)
                ON CONFLICT(series_id, season_number, episode_number) DO UPDATE SET
                    tmdb_episode_id = excluded.tmdb_episode_id,
                    name = excluded.name,
                    air_date = excluded.air_date,
                    fetched_at_utc = excluded.fetched_at_utc;
                """;
            command.Parameters.AddWithValue("$tmdb_episode_id", episode.Id);
            command.Parameters.AddWithValue("$series_id", seriesRowId);
            command.Parameters.AddWithValue("$season_number", seasonNumber);
            command.Parameters.AddWithValue("$episode_number", episode.EpisodeNumber);
            command.Parameters.AddWithValue("$name", episode.Name);
            command.Parameters.AddWithValue(
                "$air_date",
                episode.AirDate is null
                    ? DBNull.Value
                    : episode.AirDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$fetched_at_utc", fetchedAtUtc);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task EnsureEpisodeIdIdentityAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string seriesRowId,
        TmdbEpisode episode,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT series_id, season_number, episode_number
            FROM tmdb_episodes
            WHERE tmdb_episode_id = $tmdb_episode_id;
            """;
        command.Parameters.AddWithValue("$tmdb_episode_id", episode.Id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        if (!string.Equals(reader.GetString(0), seriesRowId, StringComparison.Ordinal)
            || reader.GetInt32(1) != episode.SeasonNumber
            || reader.GetInt32(2) != episode.EpisodeNumber)
        {
            throw new InvalidOperationException(
                "The TMDB Episode ID is already bound to another canonical identity.");
        }
    }
}
