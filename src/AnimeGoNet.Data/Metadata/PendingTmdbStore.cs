using System.Globalization;
using AnimeGoNet.Data.Sqlite;

namespace AnimeGoNet.Data.Metadata;

public sealed class PendingTmdbStore(AnimeGoSqliteDatabase database)
{
    public async Task<IReadOnlyList<PendingTmdbSeriesSummary>> ListAsync(
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 500);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT series.id, series.bangumi_subject_id,
                   COALESCE(NULLIF(series.canonical_name, ''), NULLIF(series.original_name, ''),
                            'Bangumi ' || series.bangumi_subject_id),
                   series.updated_at_utc
            FROM anime_series AS series
            WHERE series.tmdb_series_id = 0
              AND series.needs_tmdb_completion = 1
            ORDER BY series.updated_at_utc DESC, series.bangumi_subject_id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);
        var identities = new List<(string Id, int BgmId, string Name, DateTimeOffset Updated)>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                identities.Add((
                    reader.GetString(0),
                    reader.GetInt32(1),
                    reader.GetString(2),
                    Parse(reader.GetString(3))));
            }
        }

        var results = new List<PendingTmdbSeriesSummary>(identities.Count);
        foreach (var identity in identities)
        {
            results.Add(await ReadSummaryAsync(
                connection,
                identity.Id,
                identity.BgmId,
                identity.Name,
                identity.Updated,
                cancellationToken).ConfigureAwait(false));
        }

        return results;
    }

    public async Task<PendingTmdbSeriesDetail?> GetAsync(
        int bangumiSubjectId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(bangumiSubjectId, 1);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        string? seriesId = null;
        string? name = null;
        DateTimeOffset updated = default;
        await using (var identity = connection.CreateCommand())
        {
            identity.CommandText = """
                SELECT id,
                       COALESCE(NULLIF(canonical_name, ''), NULLIF(original_name, ''),
                                'Bangumi ' || bangumi_subject_id),
                       updated_at_utc
                FROM anime_series
                WHERE tmdb_series_id = 0
                  AND needs_tmdb_completion = 1
                  AND bangumi_subject_id = $bgmid;
                """;
            identity.Parameters.AddWithValue("$bgmid", bangumiSubjectId);
            await using var reader = await identity.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                seriesId = reader.GetString(0);
                name = reader.GetString(1);
                updated = Parse(reader.GetString(2));
            }
        }

        if (seriesId is null)
        {
            return null;
        }

        var summary = await ReadSummaryAsync(
            connection,
            seriesId,
            bangumiSubjectId,
            name!,
            updated,
            cancellationToken).ConfigureAwait(false);
        var tasks = await ReadTasksAsync(connection, bangumiSubjectId, cancellationToken).ConfigureAwait(false);
        var scopes = await ReadScopesAsync(
            connection,
            seriesId,
            bangumiSubjectId,
            cancellationToken).ConfigureAwait(false);
        return new PendingTmdbSeriesDetail(summary, tasks, scopes);
    }

    private static async Task<PendingTmdbSeriesSummary> ReadSummaryAsync(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        string seriesId,
        int bangumiSubjectId,
        string canonicalName,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken)
    {
        var seasons = new List<int>();
        await using (var seasonQuery = connection.CreateCommand())
        {
            seasonQuery.CommandText = """
                SELECT season_number FROM anime_seasons
                WHERE series_id = $series_id
                ORDER BY season_number;
                """;
            seasonQuery.Parameters.AddWithValue("$series_id", seriesId);
            await using var reader = await seasonQuery.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                seasons.Add(reader.GetInt32(0));
            }
        }

        await using var aggregate = connection.CreateCommand();
        aggregate.CommandText = """
            SELECT
                (SELECT COUNT(DISTINCT task.id)
                 FROM ingest_tasks AS task
                 WHERE task.bangumi_subject_id = $bgmid
                   AND EXISTS (
                       SELECT 1 FROM task_files AS file
                       WHERE file.task_id = task.id
                         AND (file.other_reason = 'tmdb_fallback_pending_completion'
                              OR file.other_reason GLOB 'fallback_*'))),
                (SELECT COUNT(*)
                 FROM file_operations AS operation
                 JOIN task_files AS file ON file.id = operation.task_file_id
                 JOIN ingest_tasks AS task ON task.id = file.task_id
                 WHERE task.bangumi_subject_id = $bgmid
                   AND operation.state = 'completed'
                   AND file.tmdb_series_id IS NULL
                   AND file.tmdb_season_number IS NOT NULL
                   AND file.other_reason = 'tmdb_fallback_pending_completion'),
                (SELECT COUNT(*) FROM fallback_completion_records
                 WHERE anime_series_id = $series_id),
                (SELECT COUNT(*)
                 FROM fallback_claims AS claim
                 JOIN task_files AS file ON file.id = claim.task_file_id
                 JOIN ingest_tasks AS task ON task.id = file.task_id
                 WHERE task.bangumi_subject_id = $bgmid AND claim.state = 'active'),
                (SELECT COUNT(*)
                 FROM fallback_claims AS claim
                 JOIN task_files AS file ON file.id = claim.task_file_id
                 JOIN ingest_tasks AS task ON task.id = file.task_id
                 WHERE task.bangumi_subject_id = $bgmid AND claim.state = 'completed'),
                (SELECT COUNT(*)
                 FROM task_files AS file
                 JOIN ingest_tasks AS task ON task.id = file.task_id
                 WHERE task.bangumi_subject_id = $bgmid
                   AND file.disposition = 'duplicate'
                   AND file.other_reason GLOB 'fallback_*'),
                (SELECT task.failure_kind FROM ingest_tasks AS task
                 WHERE task.bangumi_subject_id = $bgmid
                   AND (task.failure_kind IS NOT NULL OR task.failure_reason IS NOT NULL)
                 ORDER BY task.updated_at_utc DESC, task.id DESC LIMIT 1),
                (SELECT task.failure_reason FROM ingest_tasks AS task
                 WHERE task.bangumi_subject_id = $bgmid
                   AND (task.failure_kind IS NOT NULL OR task.failure_reason IS NOT NULL)
                 ORDER BY task.updated_at_utc DESC, task.id DESC LIMIT 1);
            """;
        aggregate.Parameters.AddWithValue("$series_id", seriesId);
        aggregate.Parameters.AddWithValue("$bgmid", bangumiSubjectId);
        await using var aggregateReader = await aggregate.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await aggregateReader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Pending TMDB aggregate projection was not returned.");
        }

        return new PendingTmdbSeriesSummary(
            seriesId,
            bangumiSubjectId,
            canonicalName,
            seasons,
            aggregateReader.GetInt32(0),
            aggregateReader.GetInt32(1),
            aggregateReader.GetInt32(2),
            aggregateReader.GetInt32(3),
            aggregateReader.GetInt32(4),
            aggregateReader.GetInt32(5),
            aggregateReader.IsDBNull(6) ? null : aggregateReader.GetString(6),
            aggregateReader.IsDBNull(7) ? null : aggregateReader.GetString(7),
            updatedAtUtc);
    }

    private static async Task<IReadOnlyList<PendingTmdbTaskProjection>> ReadTasksAsync(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        int bangumiSubjectId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT task.id, task.title, task.source_id, task.status,
                   MAX(file.tmdb_season_number),
                   SUM(CASE WHEN file.disposition = 'other' THEN 1 ELSE 0 END),
                   SUM(CASE WHEN file.disposition = 'duplicate'
                                  AND file.other_reason GLOB 'fallback_*'
                            THEN 1 ELSE 0 END),
                   task.failure_kind, task.failure_reason, task.updated_at_utc
            FROM ingest_tasks AS task
            JOIN task_files AS file ON file.task_id = task.id
            WHERE task.bangumi_subject_id = $bgmid
              AND (file.other_reason = 'tmdb_fallback_pending_completion'
                   OR file.other_reason GLOB 'fallback_*')
            GROUP BY task.id
            ORDER BY task.updated_at_utc DESC, task.id DESC;
            """;
        command.Parameters.AddWithValue("$bgmid", bangumiSubjectId);
        var results = new List<PendingTmdbTaskProjection>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new PendingTmdbTaskProjection(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4),
                reader.GetInt32(5),
                reader.GetInt32(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                Parse(reader.GetString(9))));
        }

        return results;
    }

    private static async Task<IReadOnlyList<PendingTmdbScopeProjection>> ReadScopesAsync(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        string seriesId,
        int bangumiSubjectId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT completion.scope_kind, completion.scope_key, 'completed',
                   completion.source_id, completion.source_episode,
                   completion.completed_at_utc
            FROM fallback_completion_records AS completion
            WHERE completion.anime_series_id = $series_id
            UNION ALL
            SELECT claim.scope_kind, claim.scope_key, claim.state,
                   task.source_id, file.source_episode, NULL
            FROM fallback_claims AS claim
            JOIN task_files AS file ON file.id = claim.task_file_id
            JOIN ingest_tasks AS task ON task.id = file.task_id
            WHERE task.bangumi_subject_id = $bgmid
              AND NOT EXISTS (
                  SELECT 1 FROM fallback_completion_records AS completion
                  WHERE completion.scope_kind = claim.scope_kind
                    AND completion.scope_key = claim.scope_key)
            ORDER BY 1, 2;
            """;
        command.Parameters.AddWithValue("$series_id", seriesId);
        command.Parameters.AddWithValue("$bgmid", bangumiSubjectId);
        var results = new List<PendingTmdbScopeProjection>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new PendingTmdbScopeProjection(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : Parse(reader.GetString(5))));
        }

        return results;
    }

    private static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
