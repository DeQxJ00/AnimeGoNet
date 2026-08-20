using System.Globalization;
using AnimeGoNet.Data.Sqlite;
using Microsoft.Data.Sqlite;

namespace AnimeGoNet.Data.Mikan;

public sealed class MikanTrustedOffsetStore(AnimeGoSqliteDatabase database)
{
    public const int DefaultRequiredDistinctEpisodes = 3;
    public const int MinimumRequiredDistinctEpisodes = 1;
    public const int MaximumRequiredDistinctEpisodes = 100;

    public Task<MikanTrustedOffset?> ObserveAsync(
        MikanOffsetEvidenceObservation observation,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default) =>
        ObserveAsync(
            observation,
            utcNow,
            DefaultRequiredDistinctEpisodes,
            cancellationToken);

    public async Task<MikanTrustedOffset?> ObserveAsync(
        MikanOffsetEvidenceObservation observation,
        DateTimeOffset utcNow,
        int requiredDistinctEpisodes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observation);
        Validate(observation);
        ValidateRequiredDistinctEpisodes(requiredDistinctEpisodes);
        var now = Format(utcNow);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        if (await IsBlacklistedAsync(
                connection,
                transaction,
                observation.MikanId,
                observation.GroupId,
                cancellationToken).ConfigureAwait(false))
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }

        var previous = await ReadAsync(
            connection,
            transaction,
            observation.MikanId,
            observation.GroupId,
            trustedOnly: false,
            requiredDistinctEpisodes,
            cancellationToken).ConfigureAwait(false);
        var trustedConflict = previous?.IsTrusted == true
            && (previous.TmdbSeriesId != observation.TmdbSeriesId
                || previous.TmdbSeasonNumber != observation.TmdbSeasonNumber
                || previous.EpisodeOffset != observation.EpisodeOffset);
        var signatureConflict = false;
        await using (var conflict = connection.CreateCommand())
        {
            conflict.Transaction = transaction;
            conflict.CommandText = """
                SELECT EXISTS(
                    SELECT 1
                    FROM mikan_offset_evidence
                    WHERE mikanid = $mikanid AND groupid = $groupid
                      AND (tmdb_series_id != $tmdb_series_id
                        OR tmdb_season_number != $tmdb_season_number
                        OR episode_offset != $episode_offset));
                """;
            conflict.Parameters.AddWithValue("$mikanid", observation.MikanId);
            conflict.Parameters.AddWithValue("$groupid", observation.GroupId);
            conflict.Parameters.AddWithValue("$tmdb_series_id", observation.TmdbSeriesId);
            conflict.Parameters.AddWithValue("$tmdb_season_number", observation.TmdbSeasonNumber);
            conflict.Parameters.AddWithValue("$episode_offset", observation.EpisodeOffset);
            signatureConflict = Convert.ToInt32(
                await conflict.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
                CultureInfo.InvariantCulture) == 1;
        }

        if (signatureConflict)
        {
            await using var reset = connection.CreateCommand();
            reset.Transaction = transaction;
            reset.CommandText = """
                DELETE FROM mikan_offset_evidence
                WHERE mikanid = $mikanid AND groupid = $groupid;
                """;
            reset.Parameters.AddWithValue("$mikanid", observation.MikanId);
            reset.Parameters.AddWithValue("$groupid", observation.GroupId);
            await reset.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

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
            select.Parameters.AddWithValue("$required_count", requiredDistinctEpisodes);
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

        if (candidates.Count == 1 && !trustedConflict)
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
            requiredDistinctEpisodes,
            cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<IReadOnlyList<MikanTrustedOffsetBlacklistEntry>> ListBlacklistAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT scope, mikanid, groupid, created_at_utc
            FROM mikan_trusted_offset_blacklist
            ORDER BY CASE scope WHEN 'mikanid' THEN 0 WHEN 'groupid' THEN 1 ELSE 2 END,
                     mikanid, groupid;
            """;
        var result = new List<MikanTrustedOffsetBlacklistEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var mikanId = reader.GetInt32(1);
            var groupId = reader.GetInt32(2);
            result.Add(new MikanTrustedOffsetBlacklistEntry(
                reader.GetString(0),
                mikanId == 0 ? null : mikanId,
                groupId == 0 ? null : groupId,
                DateTimeOffset.Parse(
                    reader.GetString(3),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind)));
        }

        return result;
    }

    public async Task<MikanTrustedOffsetBlacklistEntry> AddBlacklistAsync(
        string scope,
        int? mikanId,
        int? groupId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        var key = ValidateBlacklistKey(scope, mikanId, groupId);
        var now = Format(utcNow);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO mikan_trusted_offset_blacklist (
                    scope, mikanid, groupid, created_at_utc)
                VALUES ($scope, $mikanid, $groupid, $created_at_utc)
                ON CONFLICT(scope, mikanid, groupid) DO UPDATE SET
                    created_at_utc = excluded.created_at_utc;
                """;
            AddBlacklistKeyParameters(insert, key);
            insert.Parameters.AddWithValue("$created_at_utc", now);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var table in new[] { "mikan_offset_evidence", "mikan_trusted_offsets" })
        {
            await using var purge = connection.CreateCommand();
            purge.Transaction = transaction;
            purge.CommandText = $"""
                DELETE FROM {table}
                WHERE ($scope = 'mikanid' AND mikanid = $mikanid)
                   OR ($scope = 'groupid' AND groupid = $groupid)
                   OR ($scope = 'pair' AND mikanid = $mikanid AND groupid = $groupid);
                """;
            AddBlacklistKeyParameters(purge, key);
            await purge.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new MikanTrustedOffsetBlacklistEntry(
            key.Scope,
            key.MikanId == 0 ? null : key.MikanId,
            key.GroupId == 0 ? null : key.GroupId,
            utcNow.ToUniversalTime());
    }

    public async Task<bool> RemoveBlacklistAsync(
        string scope,
        int? mikanId,
        int? groupId,
        CancellationToken cancellationToken = default)
    {
        var key = ValidateBlacklistKey(scope, mikanId, groupId);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM mikan_trusted_offset_blacklist
            WHERE scope = $scope AND mikanid = $mikanid AND groupid = $groupid;
            """;
        AddBlacklistKeyParameters(command, key);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
    }

    public async Task<bool> IsBlacklistedAsync(
        int mikanId,
        int groupId,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(mikanId, groupId);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await IsBlacklistedAsync(
            connection,
            null,
            mikanId,
            groupId,
            cancellationToken).ConfigureAwait(false);
    }

    public Task<MikanTrustedOffset?> GetTrustedAsync(
        int mikanId,
        int groupId,
        CancellationToken cancellationToken = default) =>
        GetTrustedAsync(
            mikanId,
            groupId,
            DefaultRequiredDistinctEpisodes,
            cancellationToken);

    public async Task<MikanTrustedOffset?> GetTrustedAsync(
        int mikanId,
        int groupId,
        int requiredDistinctEpisodes,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(mikanId, groupId);
        ValidateRequiredDistinctEpisodes(requiredDistinctEpisodes);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await ReadAsync(
            connection,
            null,
            mikanId,
            groupId,
            trustedOnly: true,
            requiredDistinctEpisodes,
            cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<MikanOffsetCandidateState>> ListAsync(
        int? mikanId = null,
        int? groupId = null,
        CancellationToken cancellationToken = default) =>
        ListAsync(
            mikanId,
            groupId,
            DefaultRequiredDistinctEpisodes,
            cancellationToken);

    public async Task<IReadOnlyList<MikanOffsetCandidateState>> ListAsync(
        int? mikanId,
        int? groupId,
        int requiredDistinctEpisodes,
        CancellationToken cancellationToken = default)
    {
        ValidateRequiredDistinctEpisodes(requiredDistinctEpisodes);
        if (mikanId is not null)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(mikanId.Value, 1);
        }

        if (groupId is not null)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(groupId.Value, 1);
        }

        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT evidence.mikanid, evidence.groupid, evidence.tmdb_series_id,
                   evidence.tmdb_season_number, evidence.episode_offset,
                   COUNT(DISTINCT evidence.source_episode),
                   CASE
                     WHEN trusted.state = 'trusted'
                      AND COUNT(DISTINCT evidence.source_episode) >= $required_count
                      AND trusted.tmdb_series_id = evidence.tmdb_series_id
                      AND trusted.tmdb_season_number = evidence.tmdb_season_number
                      AND trusted.episode_offset = evidence.episode_offset
                       THEN 'trusted'
                     WHEN trusted.state = 'revoked' THEN 'conflict_reset'
                     ELSE 'learning'
                   END,
                   MAX(evidence.observed_at_utc)
            FROM mikan_offset_evidence AS evidence
            LEFT JOIN mikan_trusted_offsets AS trusted
              ON trusted.mikanid = evidence.mikanid
             AND trusted.groupid = evidence.groupid
            WHERE ($mikanid IS NULL OR evidence.mikanid = $mikanid)
              AND ($groupid IS NULL OR evidence.groupid = $groupid)
            GROUP BY evidence.mikanid, evidence.groupid, evidence.tmdb_series_id,
                     evidence.tmdb_season_number, evidence.episode_offset,
                     trusted.state, trusted.tmdb_series_id,
                     trusted.tmdb_season_number, trusted.episode_offset
            ORDER BY evidence.mikanid, evidence.groupid,
                     COUNT(DISTINCT evidence.source_episode) DESC,
                     evidence.tmdb_series_id, evidence.tmdb_season_number,
                     evidence.episode_offset;
            """;
        command.Parameters.AddWithValue("$mikanid", (object?)mikanId ?? DBNull.Value);
        command.Parameters.AddWithValue("$groupid", (object?)groupId ?? DBNull.Value);
        command.Parameters.AddWithValue("$required_count", requiredDistinctEpisodes);
        var result = new List<MikanOffsetCandidateState>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(new MikanOffsetCandidateState(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                reader.GetInt32(5),
                reader.GetString(6),
                DateTimeOffset.Parse(
                    reader.GetString(7),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind)));
        }

        return result;
    }

    public async Task<bool> ClearAsync(
        int mikanId,
        int groupId,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(mikanId, groupId);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        var affected = 0;
        await using (var evidence = connection.CreateCommand())
        {
            evidence.Transaction = transaction;
            evidence.CommandText = """
                DELETE FROM mikan_offset_evidence
                WHERE mikanid = $mikanid AND groupid = $groupid;
                """;
            evidence.Parameters.AddWithValue("$mikanid", mikanId);
            evidence.Parameters.AddWithValue("$groupid", groupId);
            affected += await evidence.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var trusted = connection.CreateCommand())
        {
            trusted.Transaction = transaction;
            trusted.CommandText = """
                DELETE FROM mikan_trusted_offsets
                WHERE mikanid = $mikanid AND groupid = $groupid;
                """;
            trusted.Parameters.AddWithValue("$mikanid", mikanId);
            trusted.Parameters.AddWithValue("$groupid", groupId);
            affected += await trusted.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return affected > 0;
    }

    public Task<MikanTrustedEpisodeResolution?> TryResolveEpisodeAsync(
        int mikanId,
        int groupId,
        int? sourceEpisode,
        bool enabled,
        CancellationToken cancellationToken = default) =>
        TryResolveEpisodeAsync(
            mikanId,
            groupId,
            sourceEpisode,
            enabled,
            DefaultRequiredDistinctEpisodes,
            cancellationToken);

    public async Task<MikanTrustedEpisodeResolution?> TryResolveEpisodeAsync(
        int mikanId,
        int groupId,
        int? sourceEpisode,
        bool enabled,
        int requiredDistinctEpisodes,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(mikanId, groupId);
        if (!enabled || sourceEpisode is null or <= 0)
        {
            return null;
        }

        var trusted = await GetTrustedAsync(
            mikanId,
            groupId,
            requiredDistinctEpisodes,
            cancellationToken).ConfigureAwait(false);
        if (trusted is null
            || trusted.TmdbSeriesId <= 0
            || trusted.TmdbSeasonNumber <= 0
            || trusted.DistinctEpisodeCount < requiredDistinctEpisodes)
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
        int requiredDistinctEpisodes,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT mikanid, groupid, tmdb_series_id, tmdb_season_number,
                   episode_offset, distinct_episode_count, state, updated_at_utc
            FROM mikan_trusted_offsets
            WHERE mikanid = $mikanid AND groupid = $groupid
              AND ($trusted_only = 0 OR (state = 'trusted'
                   AND distinct_episode_count >= $required_count))
              AND NOT EXISTS (
                  SELECT 1
                  FROM mikan_trusted_offset_blacklist AS blacklist
                  WHERE (blacklist.scope = 'mikanid' AND blacklist.mikanid = $mikanid)
                     OR (blacklist.scope = 'groupid' AND blacklist.groupid = $groupid)
                     OR (blacklist.scope = 'pair'
                         AND blacklist.mikanid = $mikanid
                         AND blacklist.groupid = $groupid));
            """;
        command.Parameters.AddWithValue("$mikanid", mikanId);
        command.Parameters.AddWithValue("$groupid", groupId);
        command.Parameters.AddWithValue("$trusted_only", trustedOnly ? 1 : 0);
        command.Parameters.AddWithValue("$required_count", requiredDistinctEpisodes);
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

    private static void ValidateRequiredDistinctEpisodes(int value)
    {
        if (value is < MinimumRequiredDistinctEpisodes or > MaximumRequiredDistinctEpisodes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                $"Required distinct episodes must be between {MinimumRequiredDistinctEpisodes} and {MaximumRequiredDistinctEpisodes}.");
        }
    }

    private static async Task<bool> IsBlacklistedAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        int mikanId,
        int groupId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT EXISTS(
                SELECT 1
                FROM mikan_trusted_offset_blacklist
                WHERE (scope = 'mikanid' AND mikanid = $mikanid)
                   OR (scope = 'groupid' AND groupid = $groupid)
                   OR (scope = 'pair' AND mikanid = $mikanid AND groupid = $groupid));
            """;
        command.Parameters.AddWithValue("$mikanid", mikanId);
        command.Parameters.AddWithValue("$groupid", groupId);
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture) == 1;
    }

    private static BlacklistKey ValidateBlacklistKey(
        string scope,
        int? mikanId,
        int? groupId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        scope = scope.Trim().ToLowerInvariant();
        if (!MikanTrustedOffsetBlacklistScope.IsValid(scope))
        {
            throw new ArgumentException("Blacklist scope must be mikanid, groupid or pair.", nameof(scope));
        }

        var normalizedMikanId = mikanId ?? 0;
        var normalizedGroupId = groupId ?? 0;
        var valid = scope switch
        {
            MikanTrustedOffsetBlacklistScope.MikanId => normalizedMikanId > 0 && normalizedGroupId == 0,
            MikanTrustedOffsetBlacklistScope.GroupId => normalizedMikanId == 0 && normalizedGroupId > 0,
            MikanTrustedOffsetBlacklistScope.Pair => normalizedMikanId > 0 && normalizedGroupId > 0,
            _ => false,
        };
        if (!valid)
        {
            throw new ArgumentException(
                "Blacklist key does not match its scope. Single scopes require one positive ID; pair requires both.");
        }

        return new BlacklistKey(scope, normalizedMikanId, normalizedGroupId);
    }

    private static void AddBlacklistKeyParameters(SqliteCommand command, BlacklistKey key)
    {
        command.Parameters.AddWithValue("$scope", key.Scope);
        command.Parameters.AddWithValue("$mikanid", key.MikanId);
        command.Parameters.AddWithValue("$groupid", key.GroupId);
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

    private sealed record BlacklistKey(string Scope, int MikanId, int GroupId);
}
