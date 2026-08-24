using System.Globalization;
using AnimeGoNet.Data.Sqlite;

namespace AnimeGoNet.Data.Mikan;

public sealed record MikanManualSeriesMapping(
    int MikanId,
    int GroupId,
    int ExpectedTmdbSeriesId,
    int TmdbSeriesId,
    int TmdbSeasonNumber,
    string AcceptedFromTaskId,
    DateTimeOffset AcceptedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed class MikanManualSeriesMappingStore(AnimeGoSqliteDatabase database)
{
    public async Task<MikanManualSeriesMapping?> GetAsync(
        int mikanId,
        int groupId,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(mikanId, groupId);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT mikanid, groupid, expected_tmdb_series_id, tmdb_series_id,
                   tmdb_season_number, accepted_from_task_id,
                   accepted_at_utc, updated_at_utc
            FROM mikan_manual_series_mappings
            WHERE mikanid = $mikanid AND groupid = $groupid;
            """;
        command.Parameters.AddWithValue("$mikanid", mikanId);
        command.Parameters.AddWithValue("$groupid", groupId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? Read(reader)
            : null;
    }

    public async Task<IReadOnlyList<MikanManualSeriesMapping>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT mikanid, groupid, expected_tmdb_series_id, tmdb_series_id,
                   tmdb_season_number, accepted_from_task_id,
                   accepted_at_utc, updated_at_utc
            FROM mikan_manual_series_mappings
            ORDER BY updated_at_utc DESC, mikanid, groupid;
            """;
        var values = new List<MikanManualSeriesMapping>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            values.Add(Read(reader));
        }
        return values;
    }

    public async Task<MikanManualSeriesMapping> UpsertAsync(
        int mikanId,
        int groupId,
        int expectedTmdbSeriesId,
        int tmdbSeriesId,
        int tmdbSeasonNumber,
        string acceptedFromTaskId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(mikanId, groupId);
        ArgumentOutOfRangeException.ThrowIfLessThan(expectedTmdbSeriesId, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(tmdbSeriesId, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(tmdbSeasonNumber, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(acceptedFromTaskId);
        var now = Format(utcNow);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO mikan_manual_series_mappings (
                mikanid, groupid, expected_tmdb_series_id, tmdb_series_id,
                tmdb_season_number, accepted_from_task_id,
                accepted_at_utc, updated_at_utc)
            VALUES (
                $mikanid, $groupid, $expected_series, $series,
                $season, $task_id, $now, $now)
            ON CONFLICT(mikanid, groupid) DO UPDATE SET
                expected_tmdb_series_id = excluded.expected_tmdb_series_id,
                tmdb_series_id = excluded.tmdb_series_id,
                tmdb_season_number = excluded.tmdb_season_number,
                accepted_from_task_id = excluded.accepted_from_task_id,
                accepted_at_utc = excluded.accepted_at_utc,
                updated_at_utc = excluded.updated_at_utc;
            """;
        command.Parameters.AddWithValue("$mikanid", mikanId);
        command.Parameters.AddWithValue("$groupid", groupId);
        command.Parameters.AddWithValue("$expected_series", expectedTmdbSeriesId);
        command.Parameters.AddWithValue("$series", tmdbSeriesId);
        command.Parameters.AddWithValue("$season", tmdbSeasonNumber);
        command.Parameters.AddWithValue("$task_id", acceptedFromTaskId.Trim());
        command.Parameters.AddWithValue("$now", now);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return (await GetAsync(mikanId, groupId, cancellationToken).ConfigureAwait(false))!;
    }

    public async Task<bool> DeleteAsync(
        int mikanId,
        int groupId,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(mikanId, groupId);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM mikan_manual_series_mappings
            WHERE mikanid = $mikanid AND groupid = $groupid;
            """;
        command.Parameters.AddWithValue("$mikanid", mikanId);
        command.Parameters.AddWithValue("$groupid", groupId);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    private static MikanManualSeriesMapping Read(Microsoft.Data.Sqlite.SqliteDataReader reader) =>
        new(
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetInt32(3),
            reader.GetInt32(4),
            reader.GetString(5),
            Parse(reader.GetString(6)),
            Parse(reader.GetString(7)));

    private static void ValidateKey(int mikanId, int groupId)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(mikanId, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(groupId, 1);
    }

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
