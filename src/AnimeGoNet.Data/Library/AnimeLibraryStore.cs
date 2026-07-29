using System.Globalization;
using AnimeGoNet.Data.Sqlite;
using Microsoft.Data.Sqlite;

namespace AnimeGoNet.Data.Library;

public sealed class AnimeLibraryStore(AnimeGoSqliteDatabase database)
{
    private const int RelatedTaskLimit = 50;
    private const int ResolutionAttemptLimit = 200;

    public async Task<AnimeSeasonListPage> ListSeasonsAsync(
        AnimeSeasonListQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(query.Page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(query.PageSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(query.PageSize, 100);
        if (!Enum.IsDefined(query.Sort))
        {
            throw new ArgumentOutOfRangeException(nameof(query), "Library sort is invalid.");
        }

        if (!Enum.IsDefined(query.Direction))
        {
            throw new ArgumentOutOfRangeException(nameof(query), "Library sort direction is invalid.");
        }

        var offset = checked((query.Page - 1) * query.PageSize);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var count = connection.CreateCommand();
        count.CommandText = """
            SELECT COUNT(*)
            FROM anime_seasons AS season
            JOIN anime_series AS series ON series.id = season.series_id
            WHERE series.tmdb_series_id > 0
              AND series.needs_tmdb_completion = 0
              AND season.season_number > 0;
            """;
        var totalItems = Convert.ToInt32(
            await count.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);

        await using var command = connection.CreateCommand();
        command.CommandText = BuildListSql(query.Sort, query.Direction);
        command.Parameters.AddWithValue("$limit", query.PageSize);
        command.Parameters.AddWithValue("$offset", offset);
        var items = new List<AnimeSeasonListProjection>(Math.Min(query.PageSize, totalItems));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            items.Add(ReadSeasonProjection(reader));
        }

        return new AnimeSeasonListPage(query.Page, query.PageSize, totalItems, items);
    }

    public async Task<AnimeSeasonDetailProjection?> GetSeasonAsync(
        int tmdbSeriesId,
        int seasonNumber,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tmdbSeriesId, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(seasonNumber, 1);

        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var seasonCommand = connection.CreateCommand();
        seasonCommand.CommandText = LibraryProjectionSql + """

            SELECT tmdb_series_id, season_number, display_name, season_name,
                   series_poster_path, season_poster_path, air_date, added_at,
                   last_updated_at, display_name AS sort_name, episode_total,
                   episode_snapshot_count, episode_downloaded,
                   series_resolution_source,
                   series_resolution_run_id,
                   series_resolution_attempt_id,
                   season_resolution_source,
                   season_resolution_run_id,
                   season_resolution_attempt_id,
                   validation_status, last_resolution_run_id,
                   all_completion_count, missing_media_path_count,
                   series_resource_id, series_resource_updated_at,
                   season_resource_id, season_resource_updated_at
            FROM projection
            WHERE tmdb_series_id = $tmdb_series_id
              AND season_number = $season_number
            LIMIT 1;
            """;
        seasonCommand.Parameters.AddWithValue("$tmdb_series_id", tmdbSeriesId);
        seasonCommand.Parameters.AddWithValue("$season_number", seasonNumber);
        AnimeSeasonListProjection season;
        await using (var reader = await seasonCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            season = ReadSeasonProjection(reader);
        }

        await using var episodeCommand = connection.CreateCommand();
        episodeCommand.CommandText = """
            SELECT episode.tmdb_episode_id,
                   episode.episode_number,
                   episode.name,
                   episode.air_date,
                   episode.runtime_minutes,
                   episode.fetched_at_utc,
                   completion.id,
                   completion.source_id,
                   completion.completed_at_utc,
                   CASE WHEN completion.media_path IS NOT NULL THEN 1 ELSE 0 END
            FROM tmdb_episodes AS episode
            JOIN anime_series AS series ON series.id = episode.series_id
            LEFT JOIN completion_records AS completion
              ON completion.tmdb_series_id = series.tmdb_series_id
             AND completion.tmdb_season_number = episode.season_number
             AND completion.tmdb_episode_number = episode.episode_number
            WHERE series.tmdb_series_id = $tmdb_series_id
              AND series.needs_tmdb_completion = 0
              AND episode.season_number = $season_number
            ORDER BY episode.episode_number ASC, episode.tmdb_episode_id ASC;
            """;
        episodeCommand.Parameters.AddWithValue("$tmdb_series_id", tmdbSeriesId);
        episodeCommand.Parameters.AddWithValue("$season_number", seasonNumber);
        var episodes = new List<AnimeEpisodeProjection>(season.EpisodeSnapshotCount);
        await using var episodeReader = await episodeCommand
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await episodeReader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var downloaded = !episodeReader.IsDBNull(6);
            episodes.Add(new AnimeEpisodeProjection(
                episodeReader.GetInt32(0),
                episodeReader.GetInt32(1),
                episodeReader.IsDBNull(2) ? null : episodeReader.GetString(2),
                ParseDate(episodeReader, 3),
                episodeReader.IsDBNull(4) ? null : episodeReader.GetInt32(4),
                ParseTimestamp(episodeReader.GetString(5)),
                downloaded,
                downloaded ? episodeReader.GetString(7) : null,
                downloaded ? ParseTimestamp(episodeReader.GetString(8)) : null,
                episodeReader.GetInt32(9) == 1));
        }

        var audit = await ReadAuditAsync(
            connection,
            tmdbSeriesId,
            seasonNumber,
            cancellationToken).ConfigureAwait(false);
        return new AnimeSeasonDetailProjection(season, episodes, audit);
    }

    public async Task<AnimePosterProjection?> GetPosterAsync(
        int tmdbSeriesId,
        int seasonNumber,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tmdbSeriesId, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(seasonNumber, 1);

        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT season.poster_path, series.poster_path
            FROM anime_seasons AS season
            JOIN anime_series AS series ON series.id = season.series_id
            WHERE series.tmdb_series_id = $tmdb_series_id
              AND series.tmdb_series_id > 0
              AND series.needs_tmdb_completion = 0
              AND season.season_number = $season_number
              AND season.season_number > 0
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$tmdb_series_id", tmdbSeriesId);
        command.Parameters.AddWithValue("$season_number", seasonNumber);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        if (!reader.IsDBNull(0))
        {
            return new AnimePosterProjection(reader.GetString(0), "season");
        }

        return !reader.IsDBNull(1)
            ? new AnimePosterProjection(reader.GetString(1), "series")
            : new AnimePosterProjection(null, "placeholder");
    }

    private static string BuildListSql(
        AnimeLibrarySort sort,
        AnimeLibrarySortDirection direction)
    {
        var sqlDirection = direction == AnimeLibrarySortDirection.Ascending ? "ASC" : "DESC";
        var orderBy = sort switch
        {
            AnimeLibrarySort.LastUpdated =>
                $"last_updated_at {sqlDirection}, tmdb_series_id ASC, season_number ASC",
            AnimeLibrarySort.Name =>
                $"display_name COLLATE NOCASE {sqlDirection}, season_number ASC, tmdb_series_id ASC",
            AnimeLibrarySort.AirDate =>
                $"air_date IS NULL ASC, air_date {sqlDirection}, display_name COLLATE NOCASE ASC, "
                + "season_number ASC, tmdb_series_id ASC",
            AnimeLibrarySort.AddedAt =>
                $"added_at {sqlDirection}, tmdb_series_id ASC, season_number ASC",
            _ => throw new ArgumentOutOfRangeException(nameof(sort)),
        };
        return LibraryProjectionSql + $$"""

            SELECT tmdb_series_id, season_number, display_name, season_name,
                   series_poster_path, season_poster_path, air_date, added_at,
                   last_updated_at, display_name AS sort_name, episode_total,
                   episode_snapshot_count, episode_downloaded,
                   series_resolution_source,
                   series_resolution_run_id,
                   series_resolution_attempt_id,
                   season_resolution_source,
                   season_resolution_run_id,
                   season_resolution_attempt_id,
                   validation_status, last_resolution_run_id,
                   all_completion_count, missing_media_path_count,
                   series_resource_id, series_resource_updated_at,
                   season_resource_id, season_resource_updated_at
            FROM projection
            ORDER BY {{orderBy}}
            LIMIT $limit OFFSET $offset;
            """;
    }

    private const string LibraryProjectionSql = """
            WITH episode_aggregate AS (
                SELECT series_id, season_number, COUNT(*) AS snapshot_count
                FROM tmdb_episodes
                GROUP BY series_id, season_number
            ),
            completion_aggregate AS (
                SELECT completion.tmdb_series_id, completion.tmdb_season_number,
                       COUNT(*) AS completion_count,
                       MAX(completion.completed_at_utc) AS last_completed_at,
                       SUM(CASE WHEN completion.media_path IS NULL THEN 1 ELSE 0 END)
                           AS missing_media_path_count
                FROM completion_records AS completion
                GROUP BY completion.tmdb_series_id, completion.tmdb_season_number
            ),
            valid_completion_aggregate AS (
                SELECT series.tmdb_series_id, episode.season_number,
                       COUNT(*) AS downloaded_count
                FROM tmdb_episodes AS episode
                JOIN anime_series AS series ON series.id = episode.series_id
                JOIN completion_records AS completion
                  ON completion.tmdb_series_id = series.tmdb_series_id
                 AND completion.tmdb_season_number = episode.season_number
                 AND completion.tmdb_episode_number = episode.episode_number
                GROUP BY series.tmdb_series_id, episode.season_number
            ),
            task_aggregate AS (
                SELECT file.tmdb_series_id, file.tmdb_season_number,
                       MAX(task.updated_at_utc) AS last_task_update
                FROM task_files AS file
                JOIN ingest_tasks AS task ON task.id = file.task_id
                WHERE file.tmdb_series_id IS NOT NULL
                  AND file.tmdb_season_number IS NOT NULL
                GROUP BY file.tmdb_series_id, file.tmdb_season_number
            ),
            ranked_runs AS (
                SELECT run.*,
                       ROW_NUMBER() OVER (
                           PARTITION BY run.tmdb_series_id, run.tmdb_season_number
                           ORDER BY COALESCE(run.completed_at_utc, run.started_at_utc) DESC,
                                    run.id DESC) AS row_number
                FROM metadata_resolution_runs AS run
                WHERE run.tmdb_series_id IS NOT NULL
                  AND run.tmdb_season_number IS NOT NULL
                  AND run.series_resolution_source IS NOT NULL
                  AND run.season_resolution_source IS NOT NULL
            ),
            recovery_sources AS (
                SELECT fallback.anime_series_id,
                       completion.tmdb_season_number,
                       CASE
                           WHEN MAX(CASE WHEN fallback.resolution_source = 'manual'
                                         THEN 1 ELSE 0 END) = 1
                               THEN 'pending_tmdb_manual'
                           ELSE 'pending_tmdb_automatic'
                       END AS recovery_source
                FROM fallback_completion_records AS fallback
                JOIN completion_records AS completion
                  ON completion.id = fallback.resolved_completion_id
                WHERE fallback.resolution_state <> 'pending'
                GROUP BY fallback.anime_series_id, completion.tmdb_season_number
            ),
            projection AS (
                SELECT
                    series.tmdb_series_id AS tmdb_series_id,
                    season.season_number AS season_number,
                    COALESCE(NULLIF(series.canonical_name, ''),
                             NULLIF(series.original_name, ''),
                             'TMDB ' || series.tmdb_series_id) AS display_name,
                    COALESCE(NULLIF(season.canonical_name, ''),
                             'Season ' || season.season_number) AS season_name,
                    series.poster_path AS series_poster_path,
                    season.poster_path AS season_poster_path,
                    season.air_date AS air_date,
                    season.created_at_utc AS added_at,
                    MAX(
                        season.updated_at_utc,
                        series.updated_at_utc,
                        COALESCE(completion.last_completed_at, season.updated_at_utc),
                        COALESCE(tasks.last_task_update, season.updated_at_utc),
                        COALESCE(run.completed_at_utc, run.started_at_utc, season.updated_at_utc)
                    ) AS last_updated_at,
                    season.episode_count AS episode_total,
                    COALESCE(episodes.snapshot_count, 0) AS episode_snapshot_count,
                    COALESCE(valid_completion.downloaded_count, 0) AS episode_downloaded,
                    COALESCE(run.series_resolution_source, recovery.recovery_source)
                        AS series_resolution_source,
                    CASE WHEN run.series_resolution_source IS NULL
                              THEN NULL ELSE run.id END
                        AS series_resolution_run_id,
                    run.series_resolution_attempt_id
                        AS series_resolution_attempt_id,
                    COALESCE(run.season_resolution_source, recovery.recovery_source)
                        AS season_resolution_source,
                    CASE WHEN run.season_resolution_source IS NULL
                              THEN NULL ELSE run.id END
                        AS season_resolution_run_id,
                    run.season_resolution_attempt_id
                        AS season_resolution_attempt_id,
                    CASE
                        WHEN run.season_resolution_source IN ('title_season', 'first_season')
                            THEN 'local_unverified'
                        WHEN recovery.recovery_source IS NOT NULL THEN 'verified'
                        WHEN run.id IS NULL THEN 'projection_only'
                        ELSE 'verified'
                    END AS validation_status,
                    run.id AS last_resolution_run_id,
                    COALESCE(completion.completion_count, 0) AS all_completion_count,
                    COALESCE(completion.missing_media_path_count, 0)
                        AS missing_media_path_count,
                    series.id AS series_resource_id,
                    series.updated_at_utc AS series_resource_updated_at,
                    season.id AS season_resource_id,
                    season.updated_at_utc AS season_resource_updated_at
                FROM anime_seasons AS season
                JOIN anime_series AS series ON series.id = season.series_id
                LEFT JOIN episode_aggregate AS episodes
                  ON episodes.series_id = series.id
                 AND episodes.season_number = season.season_number
                LEFT JOIN completion_aggregate AS completion
                  ON completion.tmdb_series_id = series.tmdb_series_id
                 AND completion.tmdb_season_number = season.season_number
                LEFT JOIN valid_completion_aggregate AS valid_completion
                  ON valid_completion.tmdb_series_id = series.tmdb_series_id
                 AND valid_completion.season_number = season.season_number
                LEFT JOIN task_aggregate AS tasks
                  ON tasks.tmdb_series_id = series.tmdb_series_id
                 AND tasks.tmdb_season_number = season.season_number
                LEFT JOIN ranked_runs AS run
                  ON run.tmdb_series_id = series.tmdb_series_id
                 AND run.tmdb_season_number = season.season_number
                 AND run.row_number = 1
                LEFT JOIN recovery_sources AS recovery
                  ON recovery.anime_series_id = series.id
                 AND recovery.tmdb_season_number = season.season_number
                WHERE series.tmdb_series_id > 0
                  AND series.needs_tmdb_completion = 0
                  AND season.season_number > 0
            )
            """;

    private static AnimeSeasonListProjection ReadSeasonProjection(
        SqliteDataReader reader)
    {
        var episodeTotal = reader.GetInt32(10);
        var episodeSnapshotCount = reader.GetInt32(11);
        var episodeDownloaded = reader.GetInt32(12);
        var allCompletionCount = reader.GetInt32(21);
        var missingMediaPathCount = reader.GetInt32(22);
        var validationStatus = reader.GetString(19);
        var warnings = new List<string>(3);
        if (episodeSnapshotCount != episodeTotal)
        {
            warnings.Add("episode_snapshot_incomplete");
        }

        if (allCompletionCount != episodeDownloaded)
        {
            warnings.Add("completion_without_snapshot");
        }

        if (missingMediaPathCount > 0)
        {
            warnings.Add("completion_media_path_unknown");
        }

        if (validationStatus == "local_unverified")
        {
            warnings.Add("season_not_tmdb_verified");
        }

        var displayName = reader.GetString(2);
        var resourceRevision = AnimeLibraryResourceRevision.Create(
            reader.GetString(23),
            reader.GetString(24),
            reader.GetString(25),
            reader.GetString(26));
        return new AnimeSeasonListProjection(
            reader.GetInt32(0),
            reader.GetInt32(1),
            displayName,
            displayName.ToLowerInvariant(),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            ParseDate(reader, 6),
            ParseTimestamp(reader.GetString(7)),
            ParseTimestamp(reader.GetString(8)),
            resourceRevision,
            episodeTotal,
            episodeSnapshotCount,
            episodeDownloaded,
            reader.IsDBNull(13) ? null : reader.GetString(13),
            reader.IsDBNull(14) ? null : reader.GetString(14),
            reader.IsDBNull(15) ? null : reader.GetString(15),
            reader.IsDBNull(16) ? null : reader.GetString(16),
            reader.IsDBNull(17) ? null : reader.GetString(17),
            reader.IsDBNull(18) ? null : reader.GetString(18),
            validationStatus,
            reader.IsDBNull(20) ? null : reader.GetString(20),
            warnings);
    }

    private static async Task<AnimeSeasonAuditProjection> ReadAuditAsync(
        SqliteConnection connection,
        int tmdbSeriesId,
        int seasonNumber,
        CancellationToken cancellationToken)
    {
        var manualOffsets = await ReadManualOffsetsAsync(
            connection,
            tmdbSeriesId,
            seasonNumber,
            cancellationToken).ConfigureAwait(false);
        var (relatedTaskTotal, relatedTasks) = await ReadRelatedTasksAsync(
            connection,
            tmdbSeriesId,
            seasonNumber,
            cancellationToken).ConfigureAwait(false);
        var (resolutionAttemptTotal, resolutionAttempts) = await ReadResolutionAttemptsAsync(
            connection,
            tmdbSeriesId,
            seasonNumber,
            cancellationToken).ConfigureAwait(false);
        return new AnimeSeasonAuditProjection(
            manualOffsets,
            relatedTaskTotal,
            relatedTaskTotal > relatedTasks.Count,
            relatedTasks,
            resolutionAttemptTotal,
            resolutionAttemptTotal > resolutionAttempts.Count,
            resolutionAttempts);
    }

    private static async Task<IReadOnlyList<AnimeSeasonManualOffsetProjection>> ReadManualOffsetsAsync(
        SqliteConnection connection,
        int tmdbSeriesId,
        int seasonNumber,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT rule.mikanid, rule.bangumi_subject_id,
                   rule.tmdb_series_id, rule.tmdb_season_number,
                   rule.episode_offset, rule.enabled, rule.revision,
                   rule.updated_at_utc
            FROM mikan_work_rules AS rule
            WHERE rule.episode_offset IS NOT NULL
              AND (
                    (
                        rule.tmdb_series_id = $tmdb_series_id
                        AND (
                            rule.tmdb_season_number IS NULL
                            OR rule.tmdb_season_number = $season_number
                        )
                    )
                    OR (
                        rule.tmdb_series_id IS NULL
                        AND EXISTS (
                            SELECT 1
                            FROM ingest_tasks AS task
                            WHERE task.mikanid = rule.mikanid
                              AND (
                                    EXISTS (
                                        SELECT 1
                                        FROM task_files AS file
                                        WHERE file.task_id = task.id
                                          AND file.tmdb_series_id = $tmdb_series_id
                                          AND file.tmdb_season_number = $season_number
                                    )
                                    OR EXISTS (
                                        SELECT 1
                                        FROM metadata_resolution_runs AS run
                                        WHERE run.task_id = task.id
                                          AND run.tmdb_series_id = $tmdb_series_id
                                          AND run.tmdb_season_number = $season_number
                                    )
                              )
                        )
                    )
              )
            ORDER BY rule.enabled DESC,
                     rule.tmdb_season_number = $season_number DESC,
                     rule.mikanid ASC;
            """;
        command.Parameters.AddWithValue("$tmdb_series_id", tmdbSeriesId);
        command.Parameters.AddWithValue("$season_number", seasonNumber);
        var values = new List<AnimeSeasonManualOffsetProjection>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            values.Add(new AnimeSeasonManualOffsetProjection(
                reader.GetInt32(0),
                OptionalInt32(reader, 1),
                OptionalInt32(reader, 2),
                OptionalInt32(reader, 3),
                reader.GetInt32(4),
                reader.GetInt64(5) != 0,
                reader.GetInt64(6),
                ParseTimestamp(reader.GetString(7))));
        }

        return values;
    }

    private static async Task<(int Total, IReadOnlyList<AnimeSeasonRelatedTaskProjection> Items)>
        ReadRelatedTasksAsync(
            SqliteConnection connection,
            int tmdbSeriesId,
            int seasonNumber,
            CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            WITH related_tasks AS (
                SELECT task.id, task.title, task.source_id, task.status,
                       task.mikanid, task.bangumi_subject_id, task.updated_at_utc
                FROM ingest_tasks AS task
                WHERE EXISTS (
                        SELECT 1
                        FROM task_files AS file
                        WHERE file.task_id = task.id
                          AND file.tmdb_series_id = $tmdb_series_id
                          AND file.tmdb_season_number = $season_number
                    )
                   OR EXISTS (
                        SELECT 1
                        FROM metadata_resolution_runs AS run
                        WHERE run.task_id = task.id
                          AND run.tmdb_series_id = $tmdb_series_id
                          AND run.tmdb_season_number = $season_number
                    )
            ),
            ranked_runs AS (
                SELECT run.task_id, run.attempt_number, run.status,
                       ROW_NUMBER() OVER (
                           PARTITION BY run.task_id
                           ORDER BY run.attempt_number DESC, run.id DESC
                       ) AS row_number
                FROM metadata_resolution_runs AS run
                JOIN related_tasks AS task ON task.id = run.task_id
            )
            SELECT task.id, task.title, task.source_id, task.status,
                   task.mikanid, task.bangumi_subject_id,
                   run.attempt_number, run.status, task.updated_at_utc,
                   COUNT(*) OVER()
            FROM related_tasks AS task
            LEFT JOIN ranked_runs AS run
              ON run.task_id = task.id AND run.row_number = 1
            ORDER BY task.updated_at_utc DESC, task.id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$tmdb_series_id", tmdbSeriesId);
        command.Parameters.AddWithValue("$season_number", seasonNumber);
        command.Parameters.AddWithValue("$limit", RelatedTaskLimit + 1);
        var values = new List<AnimeSeasonRelatedTaskProjection>(RelatedTaskLimit);
        var total = 0;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            total = checked((int)reader.GetInt64(9));
            if (values.Count == RelatedTaskLimit)
            {
                continue;
            }

            values.Add(new AnimeSeasonRelatedTaskProjection(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                OptionalInt32(reader, 4),
                OptionalInt32(reader, 5),
                OptionalInt32(reader, 6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                ParseTimestamp(reader.GetString(8))));
        }

        return (total, values);
    }

    private static async Task<(int Total, IReadOnlyList<AnimeSeasonResolutionAttemptProjection> Items)>
        ReadResolutionAttemptsAsync(
            SqliteConnection connection,
            int tmdbSeriesId,
            int seasonNumber,
            CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            WITH related_task_ids AS (
                SELECT task.id
                FROM ingest_tasks AS task
                WHERE EXISTS (
                        SELECT 1
                        FROM task_files AS file
                        WHERE file.task_id = task.id
                          AND file.tmdb_series_id = $tmdb_series_id
                          AND file.tmdb_season_number = $season_number
                    )
                   OR EXISTS (
                        SELECT 1
                        FROM metadata_resolution_runs AS related_run
                        WHERE related_run.task_id = task.id
                          AND related_run.tmdb_series_id = $tmdb_series_id
                          AND related_run.tmdb_season_number = $season_number
                    )
            )
            SELECT task.id, task.title,
                   run.attempt_number, run.status,
                   attempt.stage, attempt.strategy, attempt.priority,
                   attempt.result, attempt.error_code, attempt.reason,
                   attempt.retryable, attempt.attempt_number,
                   attempt.duration_ms, attempt.created_at_utc,
                   COUNT(*) OVER()
            FROM related_task_ids AS related
            JOIN ingest_tasks AS task ON task.id = related.id
            JOIN metadata_resolution_runs AS run ON run.task_id = task.id
            JOIN metadata_resolution_attempts AS attempt ON attempt.run_id = run.id
            ORDER BY attempt.created_at_utc DESC, attempt.id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$tmdb_series_id", tmdbSeriesId);
        command.Parameters.AddWithValue("$season_number", seasonNumber);
        command.Parameters.AddWithValue("$limit", ResolutionAttemptLimit + 1);
        var values = new List<AnimeSeasonResolutionAttemptProjection>(ResolutionAttemptLimit);
        var total = 0;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            total = checked((int)reader.GetInt64(14));
            if (values.Count == ResolutionAttemptLimit)
            {
                continue;
            }

            values.Add(new AnimeSeasonResolutionAttemptProjection(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                OptionalInt32(reader, 6),
                reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.GetInt64(10) != 0,
                reader.GetInt32(11),
                reader.GetInt64(12),
                ParseTimestamp(reader.GetString(13))));
        }

        return (total, values);
    }

    private static int? OptionalInt32(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);

    private static DateOnly? ParseDate(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : DateOnly.ParseExact(
                reader.GetString(ordinal),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
}
