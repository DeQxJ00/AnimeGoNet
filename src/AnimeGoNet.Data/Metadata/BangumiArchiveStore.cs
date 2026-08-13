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

public sealed record BangumiArchiveRelationsSnapshot(
    string DataVersion,
    IReadOnlyList<BangumiSubjectRelation> Relations);

public sealed record BangumiArchiveUsage(
    long SubjectHits,
    long EpisodeHits,
    long RelationHits,
    DateTimeOffset? LastHitAtUtc)
{
    public long TotalHits => SubjectHits + EpisodeHits + RelationHits;
}

public sealed record BangumiArchiveUsageEvent(
    long Id,
    string DataVersion,
    string HitKind,
    int SubjectId,
    int ResultCount,
    DateTimeOffset HitAtUtc);

public sealed record BangumiArchiveUsagePage(
    int Page,
    int PageSize,
    long TotalItems,
    string? HitKind,
    IReadOnlyList<BangumiArchiveUsageEvent> Items);

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
        var snapshot = await GetRelatedSubjectsSnapshotAsync(
            subjectId,
            cancellationToken).ConfigureAwait(false);
        return snapshot?.Relations;
    }

    public async Task<BangumiArchiveRelationsSnapshot?> GetRelatedSubjectsSnapshotAsync(
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
        return new BangumiArchiveRelationsSnapshot(dataVersion, relations);
    }

    public Task RecordSubjectHitAsync(
        string dataVersion,
        int subjectId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default) =>
        RecordHitAsync(
            dataVersion,
            "subject",
            "subject_hit_count",
            subjectId,
            1,
            utcNow,
            cancellationToken);

    public Task RecordEpisodeHitAsync(
        string dataVersion,
        int subjectId,
        int resultCount,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default) =>
        RecordHitAsync(
            dataVersion,
            "episodes",
            "episode_hit_count",
            subjectId,
            resultCount,
            utcNow,
            cancellationToken);

    public Task RecordRelationHitAsync(
        string dataVersion,
        int subjectId,
        int resultCount,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default) =>
        RecordHitAsync(
            dataVersion,
            "relations",
            "relation_hit_count",
            subjectId,
            resultCount,
            utcNow,
            cancellationToken);

    public async Task<BangumiArchiveUsage> GetUsageAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await database
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COALESCE(SUM(subject_hit_count), 0),
                   COALESCE(SUM(episode_hit_count), 0),
                   COALESCE(SUM(relation_hit_count), 0),
                   MAX(last_hit_at_utc)
            FROM bangumi_archive_usage;
            """;
        await using var reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        return new BangumiArchiveUsage(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.IsDBNull(3)
                ? null
                : DateTimeOffset.Parse(
                    reader.GetString(3),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind));
    }

    public async Task<BangumiArchiveUsagePage> ListUsageEventsAsync(
        int page,
        int pageSize,
        string? hitKind = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        if (pageSize is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        }

        var normalizedKind = string.IsNullOrWhiteSpace(hitKind)
            ? null
            : hitKind.Trim().ToLowerInvariant();
        if (normalizedKind is not null
            && normalizedKind is not ("subject" or "episodes" or "relations"))
        {
            throw new ArgumentException(
                "Bangumi archive hit kind is invalid.",
                nameof(hitKind));
        }

        await using var connection = await database
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        long totalItems;
        await using (var count = connection.CreateCommand())
        {
            count.Transaction = transaction;
            count.CommandText = """
                SELECT COUNT(*)
                FROM bangumi_archive_usage_events
                WHERE $hit_kind IS NULL OR hit_kind = $hit_kind;
                """;
            count.Parameters.AddWithValue(
                "$hit_kind",
                (object?)normalizedKind ?? DBNull.Value);
            totalItems = Convert.ToInt64(
                await count.ExecuteScalarAsync(cancellationToken)
                    .ConfigureAwait(false),
                CultureInfo.InvariantCulture);
        }

        var items = new List<BangumiArchiveUsageEvent>();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT id, data_version, hit_kind, subject_id, result_count,
                       hit_at_utc
                FROM bangumi_archive_usage_events
                WHERE $hit_kind IS NULL OR hit_kind = $hit_kind
                ORDER BY hit_at_utc DESC, id DESC
                LIMIT $limit OFFSET $offset;
                """;
            command.Parameters.AddWithValue(
                "$hit_kind",
                (object?)normalizedKind ?? DBNull.Value);
            command.Parameters.AddWithValue("$limit", pageSize);
            command.Parameters.AddWithValue("$offset", checked((page - 1L) * pageSize));
            await using var reader = await command
                .ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                items.Add(new BangumiArchiveUsageEvent(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetInt32(3),
                    reader.GetInt32(4),
                    DateTimeOffset.Parse(
                        reader.GetString(5),
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind)));
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new BangumiArchiveUsagePage(
            page,
            pageSize,
            totalItems,
            normalizedKind,
            items);
    }

    private async Task RecordHitAsync(
        string dataVersion,
        string hitKind,
        string counterColumn,
        int subjectId,
        int resultCount,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataVersion);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(subjectId, 0);
        ArgumentOutOfRangeException.ThrowIfNegative(resultCount);
        if (hitKind is not ("subject" or "episodes" or "relations"))
        {
            throw new ArgumentOutOfRangeException(nameof(hitKind));
        }
        if (counterColumn is not (
            "subject_hit_count" or
            "episode_hit_count" or
            "relation_hit_count"))
        {
            throw new ArgumentOutOfRangeException(nameof(counterColumn));
        }

        await using var connection = await database
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            INSERT INTO bangumi_archive_usage (
                data_version, subject_hit_count, episode_hit_count,
                relation_hit_count, last_hit_at_utc)
            VALUES (
                $data_version,
                {(counterColumn == "subject_hit_count" ? 1 : 0)},
                {(counterColumn == "episode_hit_count" ? 1 : 0)},
                {(counterColumn == "relation_hit_count" ? 1 : 0)},
                $last_hit_at_utc)
            ON CONFLICT(data_version) DO UPDATE SET
                {counterColumn} = {counterColumn} + 1,
                last_hit_at_utc = MAX(
                    last_hit_at_utc,
                    excluded.last_hit_at_utc);

            INSERT INTO bangumi_archive_usage_events (
                data_version, hit_kind, subject_id, result_count, hit_at_utc)
            VALUES (
                $data_version, $hit_kind, $subject_id, $result_count,
                $last_hit_at_utc);
            """;
        command.Parameters.AddWithValue("$data_version", dataVersion);
        command.Parameters.AddWithValue("$hit_kind", hitKind);
        command.Parameters.AddWithValue("$subject_id", subjectId);
        command.Parameters.AddWithValue("$result_count", resultCount);
        command.Parameters.AddWithValue(
            "$last_hit_at_utc",
            utcNow.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
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
