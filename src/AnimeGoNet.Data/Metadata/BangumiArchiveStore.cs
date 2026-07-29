using System.Globalization;
using AnimeGoNet.Core.Metadata;
using AnimeGoNet.Data.Sqlite;
using Microsoft.Data.Sqlite;

namespace AnimeGoNet.Data.Metadata;

public sealed record BangumiArchiveSnapshot(
    string DataVersion,
    BangumiSubject Subject,
    IReadOnlyList<BangumiEpisode> Episodes)
{
    public bool HasCompleteEpisodeSet =>
        Episodes.Count > 0
        && (Subject.EpisodeCount == 0
            || Episodes.Count >= Subject.EpisodeCount);
}

public sealed class BangumiArchiveStore(AnimeGoSqliteDatabase database)
{
    public async Task<BangumiArchiveSnapshot?> GetAsync(
        int subjectId,
        CancellationToken cancellationToken = default)
    {
        if (subjectId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(subjectId),
                "Bangumi subject id must be positive.");
        }

        await using var connection = await database
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        var subject = await ReadSubjectAsync(
            connection,
            transaction,
            subjectId,
            cancellationToken).ConfigureAwait(false);
        if (subject is null)
        {
            await transaction.CommitAsync(cancellationToken)
                .ConfigureAwait(false);
            return null;
        }

        var episodes = await ReadEpisodesAsync(
            connection,
            transaction,
            subject.Value.DataVersion,
            subjectId,
            cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new BangumiArchiveSnapshot(
            subject.Value.DataVersion,
            subject.Value.Subject,
            episodes);
    }

    private static async Task<(string DataVersion, BangumiSubject Subject)?>
        ReadSubjectAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int subjectId,
            CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT archive.data_version, archive.subject_id, archive.name,
                   archive.name_cn, archive.air_date, archive.episode_count
            FROM data_update_state AS state
            JOIN bangumi_archive_subjects AS archive
              ON archive.data_version = state.active_version
            WHERE state.singleton = 1
              AND archive.subject_id = $subject_id;
            """;
        command.Parameters.AddWithValue("$subject_id", subjectId);
        await using var reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return (
            reader.GetString(0),
            new BangumiSubject(
                reader.GetInt32(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                reader.IsDBNull(4)
                    ? null
                    : ParseDate(reader.GetString(4)),
                reader.GetInt32(5)));
    }

    private static async Task<IReadOnlyList<BangumiEpisode>>
        ReadEpisodesAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string dataVersion,
            int subjectId,
            CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT episode_id, episode_number, air_date
            FROM bangumi_archive_episodes
            WHERE data_version = $data_version
              AND subject_id = $subject_id
            ORDER BY sort_number, episode_id;
            """;
        command.Parameters.AddWithValue("$data_version", dataVersion);
        command.Parameters.AddWithValue("$subject_id", subjectId);
        var episodes = new List<BangumiEpisode>();
        await using var reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            episodes.Add(new BangumiEpisode(
                reader.GetInt32(0),
                Type: 0,
                decimal.Parse(
                    reader.GetString(1),
                    NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture),
                reader.IsDBNull(2)
                    ? null
                    : ParseDate(reader.GetString(2))));
        }

        return episodes;
    }

    private static DateOnly ParseDate(string value) =>
        DateOnly.ParseExact(
            value,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None);
}
