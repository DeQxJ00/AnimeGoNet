using System.Globalization;
using AnimeGoNet.Data.Sqlite;
using Microsoft.Data.Sqlite;

namespace AnimeGoNet.Data.Mikan;

public sealed class MikanWorkMetadataRuleStore(AnimeGoSqliteDatabase database)
{
    public async Task<IReadOnlyList<MikanWorkMetadataRule>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT mikanid, bangumi_subject_id, tmdb_series_id, tmdb_season_number,
                   episode_offset, enabled, revision, created_at_utc, updated_at_utc
            FROM mikan_work_rules
            ORDER BY mikanid;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<MikanWorkMetadataRule>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(Read(reader));
        }
        return result;
    }

    public Task<MikanWorkMetadataRule?> GetAsync(
        int mikanId,
        CancellationToken cancellationToken = default) =>
        ReadAsync(mikanId, enabledOnly: false, cancellationToken);

    public Task<MikanWorkMetadataRule?> GetEnabledAsync(
        int mikanId,
        CancellationToken cancellationToken = default) =>
        ReadAsync(mikanId, enabledOnly: true, cancellationToken);

    public async Task<MikanWorkMetadataRule> SaveAsync(
        MikanWorkMetadataRuleUpdate update,
        long expectedRevision,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        Validate(update);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedRevision);
        var now = Format(utcNow);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = expectedRevision == 0
            ? """
                INSERT INTO mikan_work_rules (
                    mikanid, bangumi_subject_id, tmdb_series_id, tmdb_season_number,
                    episode_offset, enabled, revision, created_at_utc, updated_at_utc)
                VALUES (
                    $mikanid, $bangumi_subject_id, $tmdb_series_id, $tmdb_season_number,
                    $episode_offset, $enabled, 1, $now, $now)
                ON CONFLICT(mikanid) DO NOTHING;
                """
            : """
                UPDATE mikan_work_rules
                SET bangumi_subject_id = $bangumi_subject_id,
                    tmdb_series_id = $tmdb_series_id,
                    tmdb_season_number = $tmdb_season_number,
                    episode_offset = $episode_offset,
                    enabled = $enabled,
                    revision = revision + 1,
                    updated_at_utc = $now
                WHERE mikanid = $mikanid AND revision = $expected_revision;
                """;
        AddValues(command, update, now);
        command.Parameters.AddWithValue("$expected_revision", expectedRevision);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            throw new MikanWorkMetadataRuleRevisionException(update.MikanId, expectedRevision);
        }

        var result = await ReadAsync(
            connection,
            transaction,
            update.MikanId,
            enabledOnly: false,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Saved Mikan work metadata rule could not be read.");
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<MikanWorkMetadataRule> SetEnabledAsync(
        int mikanId,
        bool enabled,
        long expectedRevision,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        var current = await GetAsync(mikanId, cancellationToken).ConfigureAwait(false)
            ?? throw new MikanWorkMetadataRuleRevisionException(mikanId, expectedRevision);
        return await SaveAsync(
            new MikanWorkMetadataRuleUpdate(
                current.MikanId,
                current.BangumiSubjectId,
                current.TmdbSeriesId,
                current.TmdbSeasonNumber,
                current.EpisodeOffset,
                enabled),
            expectedRevision,
            utcNow,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(
        int mikanId,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(mikanId, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(expectedRevision, 1);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM mikan_work_rules WHERE mikanid = $mikanid AND revision = $revision;";
        command.Parameters.AddWithValue("$mikanid", mikanId);
        command.Parameters.AddWithValue("$revision", expectedRevision);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            throw new MikanWorkMetadataRuleRevisionException(mikanId, expectedRevision);
        }
    }

    private async Task<MikanWorkMetadataRule?> ReadAsync(
        int mikanId,
        bool enabledOnly,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(mikanId, 1);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await ReadAsync(connection, null, mikanId, enabledOnly, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<MikanWorkMetadataRule?> ReadAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        int mikanId,
        bool enabledOnly,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT mikanid, bangumi_subject_id, tmdb_series_id, tmdb_season_number,
                   episode_offset, enabled, revision, created_at_utc, updated_at_utc
            FROM mikan_work_rules
            WHERE mikanid = $mikanid AND ($enabled_only = 0 OR enabled = 1);
            """;
        command.Parameters.AddWithValue("$mikanid", mikanId);
        command.Parameters.AddWithValue("$enabled_only", enabledOnly ? 1 : 0);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return Read(reader);
    }

    private static MikanWorkMetadataRule Read(SqliteDataReader reader) =>
        new(
            reader.GetInt32(0),
            OptionalInt32(reader, 1),
            OptionalInt32(reader, 2),
            OptionalInt32(reader, 3),
            OptionalInt32(reader, 4),
            reader.GetInt64(5) != 0,
            reader.GetInt64(6),
            Parse(reader.GetString(7)),
            Parse(reader.GetString(8)));

    private static void AddValues(SqliteCommand command, MikanWorkMetadataRuleUpdate update, string now)
    {
        command.Parameters.AddWithValue("$mikanid", update.MikanId);
        command.Parameters.AddWithValue("$bangumi_subject_id", (object?)update.BangumiSubjectId ?? DBNull.Value);
        command.Parameters.AddWithValue("$tmdb_series_id", (object?)update.TmdbSeriesId ?? DBNull.Value);
        command.Parameters.AddWithValue("$tmdb_season_number", (object?)update.TmdbSeasonNumber ?? DBNull.Value);
        command.Parameters.AddWithValue("$episode_offset", (object?)update.EpisodeOffset ?? DBNull.Value);
        command.Parameters.AddWithValue("$enabled", update.Enabled ? 1 : 0);
        command.Parameters.AddWithValue("$now", now);
    }

    private static void Validate(MikanWorkMetadataRuleUpdate update)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(update.MikanId, 1);
        ValidatePositive(update.BangumiSubjectId, nameof(update.BangumiSubjectId));
        ValidatePositive(update.TmdbSeriesId, nameof(update.TmdbSeriesId));
        ValidatePositive(update.TmdbSeasonNumber, nameof(update.TmdbSeasonNumber));
        if (update.BangumiSubjectId is null && update.TmdbSeriesId is null && update.EpisodeOffset is null)
        {
            throw new ArgumentException("A Mikan work rule must contain at least one manual override.", nameof(update));
        }

        if (update.TmdbSeasonNumber is not null && update.TmdbSeriesId is null)
        {
            throw new ArgumentException("TMDB season requires a TMDB series override.", nameof(update));
        }

        if (update.EpisodeOffset is not null
            && (update.TmdbSeriesId is null || update.TmdbSeasonNumber is null))
        {
            throw new ArgumentException("Episode offset requires TMDB series and season overrides.", nameof(update));
        }
    }

    private static void ValidatePositive(int? value, string parameterName)
    {
        if (value is <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Identifiers must be positive.");
        }
    }

    private static int? OptionalInt32(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
