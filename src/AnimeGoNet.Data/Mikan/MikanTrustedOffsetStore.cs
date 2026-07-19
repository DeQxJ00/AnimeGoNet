using System.Globalization;
using AnimeGoNet.Data.Sqlite;
using Microsoft.Data.Sqlite;

namespace AnimeGoNet.Data.Mikan;

public sealed class MikanTrustedOffsetStore(AnimeGoSqliteDatabase database)
{
    public const int RequiredDistinctEpisodes = 3;

    public async Task<MikanTrustedOffset?> ObserveAsync(
        MikanOffsetEvidenceObservation observation,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observation);
        Validate(observation);
        var now = Format(utcNow);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (var upsert = connection.CreateCommand())
        {
            upsert.Transaction = transaction;
            upsert.CommandText = """
                INSERT INTO mikan_offset_evidence (
                    id, mikanid, groupid, source_episode, tmdb_series_id,
                    tmdb_season_number, episode_offset, observed_at_utc)
                VALUES (
                    $id, $mikanid, $groupid, $source_episode, $tmdb_series_id,
                    $tmdb_season_number, $episode_offset, $observed_at_utc)
                ON CONFLICT(mikanid, groupid, source_episode) DO UPDATE SET
                    tmdb_series_id = excluded.tmdb_series_id,
                    tmdb_season_number = excluded.tmdb_season_number,
                    episode_offset = excluded.episode_offset,
                    observed_at_utc = excluded.observed_at_utc;
                """;
            upsert.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            upsert.Parameters.AddWithValue("$mikanid", observation.MikanId);
            upsert.Parameters.AddWithValue("$groupid", observation.GroupId);
            upsert.Parameters.AddWithValue("$source_episode", FormatEpisode(observation.SourceEpisode));
            upsert.Parameters.AddWithValue("$tmdb_series_id", observation.TmdbSeriesId);
            upsert.Parameters.AddWithValue("$tmdb_season_number", observation.TmdbSeasonNumber);
            upsert.Parameters.AddWithValue("$episode_offset", observation.EpisodeOffset);
            upsert.Parameters.AddWithValue("$observed_at_utc", now);
            await upsert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var candidates = new List<OffsetCandidate>();
        await using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = """
                SELECT tmdb_series_id, tmdb_season_number, episode_offset,
                       COUNT(DISTINCT source_episode)
                FROM mikan_offset_evidence
                WHERE mikanid = $mikanid AND groupid = $groupid
                GROUP BY tmdb_series_id, tmdb_season_number, episode_offset
                HAVING COUNT(DISTINCT source_episode) >= $required_count
                ORDER BY tmdb_series_id, tmdb_season_number, episode_offset;
                """;
            select.Parameters.AddWithValue("$mikanid", observation.MikanId);
            select.Parameters.AddWithValue("$groupid", observation.GroupId);
            select.Parameters.AddWithValue("$required_count", RequiredDistinctEpisodes);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                candidates.Add(new OffsetCandidate(
                    reader.GetInt32(0),
                    reader.GetInt32(1),
                    reader.GetInt32(2),
                    reader.GetInt32(3)));
            }
        }

        if (candidates.Count == 1)
        {
            var candidate = candidates[0];
            await using var trust = connection.CreateCommand();
            trust.Transaction = transaction;
            trust.CommandText = """
                INSERT INTO mikan_trusted_offsets (
                    mikanid, groupid, tmdb_series_id, tmdb_season_number,
                    episode_offset, distinct_episode_count, state, updated_at_utc)
                VALUES (
                    $mikanid, $groupid, $tmdb_series_id, $tmdb_season_number,
                    $episode_offset, $distinct_episode_count, 'trusted', $updated_at_utc)
                ON CONFLICT(mikanid, groupid) DO UPDATE SET
                    tmdb_series_id = excluded.tmdb_series_id,
                    tmdb_season_number = excluded.tmdb_season_number,
                    episode_offset = excluded.episode_offset,
                    distinct_episode_count = excluded.distinct_episode_count,
                    state = 'trusted',
                    updated_at_utc = excluded.updated_at_utc;
                """;
            trust.Parameters.AddWithValue("$mikanid", observation.MikanId);
            trust.Parameters.AddWithValue("$groupid", observation.GroupId);
            trust.Parameters.AddWithValue("$tmdb_series_id", candidate.TmdbSeriesId);
            trust.Parameters.AddWithValue("$tmdb_season_number", candidate.TmdbSeasonNumber);
            trust.Parameters.AddWithValue("$episode_offset", candidate.EpisodeOffset);
            trust.Parameters.AddWithValue("$distinct_episode_count", candidate.DistinctEpisodeCount);
            trust.Parameters.AddWithValue("$updated_at_utc", now);
            await trust.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await using var revoke = connection.CreateCommand();
            revoke.Transaction = transaction;
            revoke.CommandText = """
                UPDATE mikan_trusted_offsets
                SET state = 'revoked', updated_at_utc = $updated_at_utc
                WHERE mikanid = $mikanid AND groupid = $groupid AND state = 'trusted';
                """;
            revoke.Parameters.AddWithValue("$mikanid", observation.MikanId);
            revoke.Parameters.AddWithValue("$groupid", observation.GroupId);
            revoke.Parameters.AddWithValue("$updated_at_utc", now);
            await revoke.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var result = await ReadAsync(
            connection,
            transaction,
            observation.MikanId,
            observation.GroupId,
            trustedOnly: false,
            cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<MikanTrustedOffset?> GetTrustedAsync(
        int mikanId,
        int groupId,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(mikanId, groupId);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await ReadAsync(
            connection,
            null,
            mikanId,
            groupId,
            trustedOnly: true,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<MikanTrustedEpisodeResolution?> TryResolveEpisodeAsync(
        int mikanId,
        int groupId,
        int? sourceEpisode,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(mikanId, groupId);
        if (!enabled || sourceEpisode is null or <= 0)
        {
            return null;
        }

        var trusted = await GetTrustedAsync(mikanId, groupId, cancellationToken).ConfigureAwait(false);
        if (trusted is null
            || trusted.TmdbSeriesId <= 0
            || trusted.TmdbSeasonNumber <= 0
            || trusted.DistinctEpisodeCount < RequiredDistinctEpisodes)
        {
            return null;
        }

        int targetEpisode;
        try
        {
            targetEpisode = checked(sourceEpisode.Value + trusted.EpisodeOffset);
        }
        catch (OverflowException)
        {
            return null;
        }

        return targetEpisode > 0
            ? new MikanTrustedEpisodeResolution(
                mikanId,
                groupId,
                trusted.TmdbSeriesId,
                trusted.TmdbSeasonNumber,
                sourceEpisode.Value,
                targetEpisode,
                trusted.EpisodeOffset)
            : null;
    }

    private static async Task<MikanTrustedOffset?> ReadAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        int mikanId,
        int groupId,
        bool trustedOnly,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT mikanid, groupid, tmdb_series_id, tmdb_season_number,
                   episode_offset, distinct_episode_count, state, updated_at_utc
            FROM mikan_trusted_offsets
            WHERE mikanid = $mikanid AND groupid = $groupid
              AND ($trusted_only = 0 OR state = 'trusted');
            """;
        command.Parameters.AddWithValue("$mikanid", mikanId);
        command.Parameters.AddWithValue("$groupid", groupId);
        command.Parameters.AddWithValue("$trusted_only", trustedOnly ? 1 : 0);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new MikanTrustedOffset(
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetInt32(3),
            reader.GetInt32(4),
            reader.GetInt32(5),
            string.Equals(reader.GetString(6), "trusted", StringComparison.Ordinal),
            DateTimeOffset.Parse(reader.GetString(7), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
    }

    private static void Validate(MikanOffsetEvidenceObservation observation)
    {
        ValidateKey(observation.MikanId, observation.GroupId);
        ArgumentOutOfRangeException.ThrowIfLessThan(observation.SourceEpisode, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(observation.TmdbSeriesId, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(observation.TmdbSeasonNumber, 1);
    }

    private static void ValidateKey(int mikanId, int groupId)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(mikanId, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(groupId, 1);
    }

    private static string FormatEpisode(int episode) =>
        episode.ToString(CultureInfo.InvariantCulture);

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private sealed record OffsetCandidate(
        int TmdbSeriesId,
        int TmdbSeasonNumber,
        int EpisodeOffset,
        int DistinctEpisodeCount);
}
