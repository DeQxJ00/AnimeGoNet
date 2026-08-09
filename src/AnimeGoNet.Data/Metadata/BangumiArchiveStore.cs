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

    public async Task<IReadOnlyList<BangumiSubjectRelation>?> GetRelatedSubjectsAsync(
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
        string? dataVersion;
        await using (var support = connection.CreateCommand())
        {
            support.Transaction = transaction;
            support.CommandText = """
                SELECT version.data_version
                FROM data_update_state AS state
                JOIN data_update_versions AS version
                  ON version.data_version = state.active_version
                JOIN bangumi_archive_subjects AS subject
                  ON subject.data_version = version.data_version
                 AND subject.subject_id = $subject_id
                WHERE state.singleton = 1
                  AND version.schema_version >= 2;
                """;
            support.Parameters.AddWithValue("$subject_id", subjectId);
            dataVersion = await support.ExecuteScalarAsync(cancellationToken)
                .ConfigureAwait(false) as string;
        }
        if (dataVersion is null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }

        var relations = new List<BangumiSubjectRelation>();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT relation.related_subject_id, target.name, target.name_cn,
                       relation.relation_type
                FROM bangumi_archive_subject_relations AS relation
                JOIN bangumi_archive_subjects AS target
                  ON target.data_version = relation.data_version
                 AND target.subject_id = relation.related_subject_id
                WHERE relation.data_version = $data_version
                  AND relation.subject_id = $subject_id
                ORDER BY relation.relation_order, relation.related_subject_id,
                         relation.relation_type;
                """;
            command.Parameters.AddWithValue("$data_version", dataVersion);
            command.Parameters.AddWithValue("$subject_id", subjectId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                relations.Add(new BangumiSubjectRelation(
                    reader.GetInt32(0),
                    Type: 2,
                    reader.GetString(1),
                    reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    RelationName(reader.GetInt32(3))));
            }
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return relations;
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
            SELECT episode_id, episode_number, air_date, sort_number
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
                    : ParseDate(reader.GetString(2)),
                decimal.Parse(
                    reader.GetString(3),
                    NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture)));
        }

        return episodes;
    }

    private static DateOnly ParseDate(string value) =>
        DateOnly.ParseExact(
            value,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None);

    private static string RelationName(int relationType) => relationType switch
    {
        1 => "改编",
        2 => "前传",
        3 => "续集",
        4 => "总集篇",
        5 => "全集",
        6 => "番外篇",
        7 => "角色出演",
        8 => "相同世界观",
        9 => "不同世界观",
        10 => "不同演绎",
        11 => "衍生",
        12 => "主线故事",
        14 => "联动",
        _ => "其他",
    };
}
