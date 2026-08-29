using System.Globalization;
using AnimeGoNet.Core.Library;
using AnimeGoNet.Core.Metadata;
using AnimeGoNet.Data.Sqlite;
using Microsoft.Data.Sqlite;

namespace AnimeGoNet.Data.Metadata;

public sealed record ManualMetadataAssignmentFile(
    string TaskFileId,
    string RelativePath,
    long SizeBytes,
    bool IsVideo,
    string Disposition,
    int? TmdbSeriesId,
    int? TmdbSeasonNumber,
    int? TmdbEpisodeNumber,
    int? TmdbMovieId,
    string? FileEpisodeCandidate);

public sealed record ManualMetadataAssignmentPreview(
    string TaskId,
    string Title,
    string Status,
    string MediaType,
    bool Eligible,
    string? Reason,
    IReadOnlyList<ManualMetadataAssignmentFile> Files);

public sealed record ManualTvFileAssignment(string TaskFileId, int? EpisodeNumber);

public sealed class ManualMetadataAssignmentException(string code, string message)
    : InvalidOperationException(message)
{
    public string Code { get; } = code;
}

public sealed class ManualMetadataAssignmentStore(AnimeGoSqliteDatabase database)
{
    public async Task<ManualMetadataAssignmentPreview?> PreviewAsync(
        string taskId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        await using var connection = await database.OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        string title;
        string status;
        string mediaType;
        bool hasActiveRun;
        bool hasCompletedOrganization;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT task.title, task.status, task.media_type,
                       EXISTS (SELECT 1 FROM metadata_resolution_runs AS run
                               WHERE run.task_id = task.id AND run.status = 'running'),
                       EXISTS (SELECT 1 FROM download_jobs AS job
                               WHERE job.task_id = task.id
                                 AND job.organization_state IN ('organizing', 'cleanup', 'completed'))
                FROM ingest_tasks AS task
                WHERE task.id = $task_id;
                """;
            command.Parameters.AddWithValue("$task_id", taskId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            title = reader.GetString(0);
            status = reader.GetString(1);
            mediaType = reader.GetString(2);
            hasActiveRun = reader.GetInt64(3) == 1;
            hasCompletedOrganization = reader.GetInt64(4) == 1;
        }

        var files = new List<ManualMetadataAssignmentFile>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT id, relative_path, size_bytes, disposition,
                       tmdb_series_id, tmdb_season_number, tmdb_episode_number,
                       tmdb_movie_id, file_episode_candidate
                FROM task_files
                WHERE task_id = $task_id
                ORDER BY relative_path COLLATE NOCASE, id;
                """;
            command.Parameters.AddWithValue("$task_id", taskId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var relativePath = reader.GetString(1);
                files.Add(new ManualMetadataAssignmentFile(
                    reader.GetString(0),
                    relativePath,
                    reader.GetInt64(2),
                    SubtitleAssociationResolver.IsVideo(relativePath),
                    reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    reader.IsDBNull(5) ? null : reader.GetInt32(5),
                    reader.IsDBNull(6) ? null : reader.GetInt32(6),
                    reader.IsDBNull(7) ? null : reader.GetInt32(7),
                    reader.IsDBNull(8) ? null : reader.GetString(8)));
            }
        }

        var reason = hasActiveRun
            ? "后台匹配正在执行，请等待本轮结束后再手动指定。"
            : hasCompletedOrganization || status is "organizing_cleanup" or "organized"
                ? "任务已经开始或完成整理；请使用动画库、电影库或 TV+Movie 后处理功能修改已入库文件。"
                : files.Count == 0
                    ? "任务没有可指定的文件。"
                    : null;
        return new ManualMetadataAssignmentPreview(
            taskId, title, status, mediaType, reason is null, reason, files);
    }

    public async Task ApplyTvAsync(
        string taskId,
        TmdbSeries series,
        TmdbSeason season,
        IReadOnlyList<ManualTvFileAssignment> assignments,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(season);
        if (series.Id <= 0 || season.SeriesId != series.Id || season.SeasonNumber <= 0
            || season.Episodes is null || season.Episodes.Count != season.EpisodeCount)
        {
            throw new ArgumentException("A complete validated TMDB TV Season is required.", nameof(season));
        }

        var preview = await RequireEligiblePreviewAsync(taskId, cancellationToken).ConfigureAwait(false);
        ValidateExactFiles(preview, assignments.Select(value => value.TaskFileId));
        var episodes = season.Episodes.ToDictionary(value => value.EpisodeNumber);
        if (!assignments.Any(value => value.EpisodeNumber is > 0))
        {
            throw new ManualMetadataAssignmentException("manual_tv_episode_required", "TV 至少需要指定一个 Episode 文件。");
        }
        if (assignments.Where(value => value.EpisodeNumber is > 0)
            .Select(value => value.EpisodeNumber!.Value).Distinct().Count()
            != assignments.Count(value => value.EpisodeNumber is > 0))
        {
            throw new ManualMetadataAssignmentException("manual_tv_episode_duplicate", "同一任务不能把多个文件指定到相同 Episode。");
        }
        foreach (var assignment in assignments.Where(value => value.EpisodeNumber is > 0))
        {
            var file = preview.Files.Single(value => value.TaskFileId == assignment.TaskFileId);
            if (!file.IsVideo)
            {
                throw new ManualMetadataAssignmentException("manual_tv_episode_not_video", $"非视频文件不能指定为 Episode：{file.RelativePath}");
            }
            if (!episodes.ContainsKey(assignment.EpisodeNumber!.Value))
            {
                throw new ManualMetadataAssignmentException("manual_tv_episode_not_found", $"TMDB S{season.SeasonNumber:00} 中不存在 E{assignment.EpisodeNumber:000}。");
            }
        }

        var now = utcNow.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await EnsureTaskStillEligibleAsync(connection, transaction, taskId, cancellationToken).ConfigureAwait(false);
        var seriesRowId = await UpsertSeriesAndSeasonAsync(
            connection, transaction, series, season, now, cancellationToken).ConfigureAwait(false);
        await TmdbEpisodeProjectionWriter.UpsertAsync(
            connection, transaction, seriesRowId, series.Id, season.SeasonNumber,
            season.EpisodeCount, season.Episodes, now, cancellationToken).ConfigureAwait(false);
        await ReleaseClaimsAsync(connection, transaction, taskId, now, cancellationToken).ConfigureAwait(false);

        foreach (var assignment in assignments)
        {
            if (assignment.EpisodeNumber is > 0)
            {
                await ClaimEpisodeAsync(
                    connection, transaction, taskId, assignment.TaskFileId,
                    series.Id, season.SeasonNumber, assignment.EpisodeNumber.Value,
                    now, cancellationToken).ConfigureAwait(false);
            }

            var episode = assignment.EpisodeNumber is > 0
                ? episodes[assignment.EpisodeNumber.Value]
                : null;
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE task_files
                SET tmdb_series_id = $series_id,
                    tmdb_season_number = $season_number,
                    tmdb_episode_number = $episode_number,
                    tmdb_episode_id = $episode_id,
                    tmdb_movie_id = NULL,
                    disposition = $disposition,
                    other_reason = $reason,
                    associated_task_file_id = NULL,
                    rename_suffix = NULL,
                    episode_resolution_source = NULL,
                    episode_resolution_run_id = NULL,
                    episode_resolution_attempt_id = NULL
                WHERE id = $file_id AND task_id = $task_id;
                """;
            update.Parameters.AddWithValue("$series_id", series.Id);
            update.Parameters.AddWithValue("$season_number", season.SeasonNumber);
            update.Parameters.AddWithValue("$episode_number", (object?)episode?.EpisodeNumber ?? DBNull.Value);
            update.Parameters.AddWithValue("$episode_id", (object?)episode?.Id ?? DBNull.Value);
            update.Parameters.AddWithValue("$disposition", episode is null ? "extras" : "episode");
            update.Parameters.AddWithValue("$reason", episode is null ? "manual_tv_extra" : DBNull.Value);
            update.Parameters.AddWithValue("$file_id", assignment.TaskFileId);
            update.Parameters.AddWithValue("$task_id", taskId);
            if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            {
                throw new ManualMetadataAssignmentException("manual_assignment_concurrent_change", "任务文件在提交期间发生变化，请刷新后重试。");
            }
        }

        await FinishAsync(connection, transaction, taskId, "tv", series.Id, season.SeasonNumber, null, now, cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ApplyMovieAsync(
        string taskId,
        TmdbMovie movie,
        string mainFileId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(movie);
        ArgumentException.ThrowIfNullOrWhiteSpace(mainFileId);
        if (movie.Id <= 0 || string.IsNullOrWhiteSpace(movie.Title))
        {
            throw new ArgumentException("A validated TMDB Movie is required.", nameof(movie));
        }

        var preview = await RequireEligiblePreviewAsync(taskId, cancellationToken).ConfigureAwait(false);
        var main = preview.Files.SingleOrDefault(value => value.TaskFileId == mainFileId);
        if (main is null || !main.IsVideo)
        {
            throw new ManualMetadataAssignmentException("manual_movie_main_invalid", "Movie 主文件必须是任务中的一个视频文件。");
        }

        var now = utcNow.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await EnsureTaskStillEligibleAsync(connection, transaction, taskId, cancellationToken).ConfigureAwait(false);
        await ReleaseClaimsAsync(connection, transaction, taskId, now, cancellationToken).ConfigureAwait(false);
        await UpsertMovieAsync(connection, transaction, movie, now, cancellationToken).ConfigureAwait(false);
        await ClaimMovieAsync(connection, transaction, taskId, mainFileId, movie.Id, now, cancellationToken).ConfigureAwait(false);

        foreach (var file in preview.Files)
        {
            var isMain = file.TaskFileId == mainFileId;
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE task_files
                SET tmdb_series_id = NULL,
                    tmdb_season_number = NULL,
                    tmdb_episode_number = NULL,
                    tmdb_episode_id = NULL,
                    tmdb_movie_id = $movie_id,
                    disposition = $disposition,
                    other_reason = $reason,
                    associated_task_file_id = $associated_file_id,
                    rename_suffix = NULL,
                    episode_resolution_source = NULL,
                    episode_resolution_run_id = NULL,
                    episode_resolution_attempt_id = NULL
                WHERE id = $file_id AND task_id = $task_id;
                """;
            update.Parameters.AddWithValue("$movie_id", movie.Id);
            update.Parameters.AddWithValue("$disposition", isMain ? "movie" : "extras");
            update.Parameters.AddWithValue("$reason", isMain ? DBNull.Value : "manual_movie_extra");
            update.Parameters.AddWithValue("$associated_file_id", isMain ? DBNull.Value : mainFileId);
            update.Parameters.AddWithValue("$file_id", file.TaskFileId);
            update.Parameters.AddWithValue("$task_id", taskId);
            if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            {
                throw new ManualMetadataAssignmentException("manual_assignment_concurrent_change", "任务文件在提交期间发生变化，请刷新后重试。");
            }
        }

        await FinishAsync(connection, transaction, taskId, "movie", null, null, movie.Id, now, cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<ManualMetadataAssignmentPreview> RequireEligiblePreviewAsync(
        string taskId,
        CancellationToken cancellationToken)
    {
        var preview = await PreviewAsync(taskId, cancellationToken).ConfigureAwait(false)
            ?? throw new ManualMetadataAssignmentException("metadata_task_not_found", "Metadata task was not found.");
        if (!preview.Eligible)
        {
            throw new ManualMetadataAssignmentException("manual_assignment_not_eligible", preview.Reason ?? "任务当前不可手动指定。");
        }
        return preview;
    }

    private static void ValidateExactFiles(ManualMetadataAssignmentPreview preview, IEnumerable<string> fileIds)
    {
        var requested = fileIds.ToArray();
        if (requested.Length != requested.Distinct(StringComparer.Ordinal).Count()
            || !requested.ToHashSet(StringComparer.Ordinal)
                .SetEquals(preview.Files.Select(value => value.TaskFileId)))
        {
            throw new ManualMetadataAssignmentException("manual_assignment_files_incomplete", "必须为任务中的每个文件指定一次归类。");
        }
    }

    private static async Task EnsureTaskStillEligibleAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string taskId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT status,
                   EXISTS (SELECT 1 FROM metadata_resolution_runs
                           WHERE task_id = $task_id AND status = 'running'),
                   EXISTS (SELECT 1 FROM download_jobs
                           WHERE task_id = $task_id
                             AND organization_state IN ('organizing', 'cleanup', 'completed'))
            FROM ingest_tasks WHERE id = $task_id;
            """;
        command.Parameters.AddWithValue("$task_id", taskId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new ManualMetadataAssignmentException("metadata_task_not_found", "Metadata task was not found.");
        }
        if (reader.GetInt64(1) == 1 || reader.GetInt64(2) == 1
            || reader.GetString(0) is "organizing_cleanup" or "organized")
        {
            throw new ManualMetadataAssignmentException("manual_assignment_not_eligible", "任务状态已变化，不能继续手动指定。");
        }
    }

    private static async Task<string> UpsertSeriesAndSeasonAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        TmdbSeries series,
        TmdbSeason season,
        string now,
        CancellationToken cancellationToken)
    {
        var seriesRowId = Guid.NewGuid().ToString("N");
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO anime_series (
                    id, tmdb_series_id, canonical_name, original_name, poster_path,
                    needs_tmdb_completion, created_at_utc, updated_at_utc)
                VALUES ($id, $tmdb_id, $name, $original_name, $poster_path, 0, $now, $now)
                ON CONFLICT(tmdb_series_id) WHERE tmdb_series_id > 0 DO UPDATE SET
                    canonical_name = excluded.canonical_name,
                    original_name = excluded.original_name,
                    poster_path = COALESCE(excluded.poster_path, anime_series.poster_path),
                    updated_at_utc = excluded.updated_at_utc
                RETURNING id;
                """;
            command.Parameters.AddWithValue("$id", seriesRowId);
            command.Parameters.AddWithValue("$tmdb_id", series.Id);
            command.Parameters.AddWithValue("$name", series.Name.Trim());
            command.Parameters.AddWithValue("$original_name", series.OriginalName.Trim());
            command.Parameters.AddWithValue("$poster_path", (object?)series.PosterPath ?? DBNull.Value);
            command.Parameters.AddWithValue("$now", now);
            seriesRowId = (string)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("TMDB Series projection could not be written."));
        }
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO anime_seasons (
                    id, series_id, season_number, canonical_name, poster_path,
                    created_at_utc, updated_at_utc)
                VALUES ($id, $series_id, $season_number, $name, $poster_path, $now, $now)
                ON CONFLICT(series_id, season_number) DO UPDATE SET
                    canonical_name = excluded.canonical_name,
                    poster_path = COALESCE(excluded.poster_path, anime_seasons.poster_path),
                    updated_at_utc = excluded.updated_at_utc;
                """;
            command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            command.Parameters.AddWithValue("$series_id", seriesRowId);
            command.Parameters.AddWithValue("$season_number", season.SeasonNumber);
            command.Parameters.AddWithValue("$name", season.Name.Trim());
            command.Parameters.AddWithValue("$poster_path", (object?)season.PosterPath ?? DBNull.Value);
            command.Parameters.AddWithValue("$now", now);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        return seriesRowId;
    }

    private static async Task UpsertMovieAsync(SqliteConnection connection, SqliteTransaction transaction, TmdbMovie movie, string now, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO anime_movies (
                id, tmdb_movie_id, canonical_title, original_title,
                release_date, poster_path, created_at_utc, updated_at_utc)
            VALUES ($id, $movie_id, $title, $original_title, $release_date, $poster_path, $now, $now)
            ON CONFLICT(tmdb_movie_id) DO UPDATE SET
                canonical_title = excluded.canonical_title,
                original_title = excluded.original_title,
                release_date = COALESCE(excluded.release_date, anime_movies.release_date),
                poster_path = COALESCE(excluded.poster_path, anime_movies.poster_path),
                updated_at_utc = excluded.updated_at_utc;
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("$movie_id", movie.Id);
        command.Parameters.AddWithValue("$title", movie.Title.Trim());
        command.Parameters.AddWithValue("$original_title", movie.OriginalTitle.Trim());
        command.Parameters.AddWithValue("$release_date", movie.ReleaseDate is null ? DBNull.Value : movie.ReleaseDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$poster_path", (object?)movie.PosterPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$now", now);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ReleaseClaimsAsync(SqliteConnection connection, SqliteTransaction transaction, string taskId, string now, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE episode_claims SET state = 'released', expires_at_utc = NULL
            WHERE state = 'active' AND task_file_id IN (SELECT id FROM task_files WHERE task_id = $task_id);
            UPDATE movie_claims SET state = 'released', expires_at_utc = NULL
            WHERE state = 'active' AND task_file_id IN (SELECT id FROM task_files WHERE task_id = $task_id);
            UPDATE fallback_claims SET state = 'released', expires_at_utc = NULL
            WHERE state = 'active' AND task_file_id IN (SELECT id FROM task_files WHERE task_id = $task_id);
            """;
        command.Parameters.AddWithValue("$task_id", taskId);
        command.Parameters.AddWithValue("$now", now);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ClaimEpisodeAsync(SqliteConnection connection, SqliteTransaction transaction, string taskId, string fileId, int seriesId, int seasonNumber, int episodeNumber, string now, CancellationToken cancellationToken)
    {
        await using (var check = connection.CreateCommand())
        {
            check.Transaction = transaction;
            check.CommandText = """
                SELECT CASE
                    WHEN EXISTS (SELECT 1 FROM completion_records WHERE tmdb_series_id = $series_id AND tmdb_season_number = $season_number AND tmdb_episode_number = $episode_number) THEN 'completed'
                    WHEN EXISTS (SELECT 1 FROM episode_claims AS claim JOIN task_files AS file ON file.id = claim.task_file_id
                                 WHERE claim.tmdb_series_id = $series_id AND claim.tmdb_season_number = $season_number AND claim.tmdb_episode_number = $episode_number
                                   AND claim.state <> 'released' AND file.task_id <> $task_id) THEN 'claimed'
                    ELSE NULL END;
                """;
            check.Parameters.AddWithValue("$series_id", seriesId);
            check.Parameters.AddWithValue("$season_number", seasonNumber);
            check.Parameters.AddWithValue("$episode_number", episodeNumber);
            check.Parameters.AddWithValue("$task_id", taskId);
            var conflict = await check.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
            if (conflict is not null)
            {
                throw new ManualMetadataAssignmentException("manual_episode_target_conflict", $"TMDB S{seasonNumber:00}E{episodeNumber:000} 已完成或已被其他任务占用。");
            }
        }
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO episode_claims (id, tmdb_series_id, tmdb_season_number, tmdb_episode_number, task_file_id, state, claimed_at_utc, expires_at_utc)
            VALUES ($id, $series_id, $season_number, $episode_number, $file_id, 'active', $now, NULL)
            ON CONFLICT(tmdb_series_id, tmdb_season_number, tmdb_episode_number) DO UPDATE SET
                id = excluded.id, task_file_id = excluded.task_file_id, state = 'active', claimed_at_utc = excluded.claimed_at_utc, expires_at_utc = NULL
            WHERE episode_claims.state = 'released';
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("$series_id", seriesId);
        command.Parameters.AddWithValue("$season_number", seasonNumber);
        command.Parameters.AddWithValue("$episode_number", episodeNumber);
        command.Parameters.AddWithValue("$file_id", fileId);
        command.Parameters.AddWithValue("$now", now);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            throw new ManualMetadataAssignmentException("manual_episode_target_conflict", $"TMDB S{seasonNumber:00}E{episodeNumber:000} 已被占用。");
        }
    }

    private static async Task ClaimMovieAsync(SqliteConnection connection, SqliteTransaction transaction, string taskId, string fileId, int movieId, string now, CancellationToken cancellationToken)
    {
        await using (var check = connection.CreateCommand())
        {
            check.Transaction = transaction;
            check.CommandText = """
                SELECT CASE
                    WHEN EXISTS (SELECT 1 FROM movie_completion_records WHERE tmdb_movie_id = $movie_id) THEN 'completed'
                    WHEN EXISTS (SELECT 1 FROM movie_claims AS claim JOIN task_files AS file ON file.id = claim.task_file_id
                                 WHERE claim.tmdb_movie_id = $movie_id AND claim.state <> 'released' AND file.task_id <> $task_id) THEN 'claimed'
                    ELSE NULL END;
                """;
            check.Parameters.AddWithValue("$movie_id", movieId);
            check.Parameters.AddWithValue("$task_id", taskId);
            var conflict = await check.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
            if (conflict is not null)
            {
                throw new ManualMetadataAssignmentException("manual_movie_target_conflict", "该 TMDB Movie 已完成或已被其他任务占用。");
            }
        }
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO movie_claims (id, tmdb_movie_id, task_file_id, state, claimed_at_utc, expires_at_utc)
            VALUES ($id, $movie_id, $file_id, 'active', $now, NULL)
            ON CONFLICT(tmdb_movie_id) DO UPDATE SET id = excluded.id, task_file_id = excluded.task_file_id,
                state = 'active', claimed_at_utc = excluded.claimed_at_utc, expires_at_utc = NULL
            WHERE movie_claims.state = 'released';
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("$movie_id", movieId);
        command.Parameters.AddWithValue("$file_id", fileId);
        command.Parameters.AddWithValue("$now", now);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            throw new ManualMetadataAssignmentException("manual_movie_target_conflict", "该 TMDB Movie 已被占用。");
        }
    }

    private static async Task FinishAsync(SqliteConnection connection, SqliteTransaction transaction, string taskId, string mediaType, int? seriesId, int? seasonNumber, int? movieId, string now, CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid().ToString("N");
        var attemptNumber = 1;
        await using (var count = connection.CreateCommand())
        {
            count.Transaction = transaction;
            count.CommandText = "SELECT COALESCE(MAX(attempt_number), 0) + 1 FROM metadata_resolution_runs WHERE task_id = $task_id;";
            count.Parameters.AddWithValue("$task_id", taskId);
            attemptNumber = Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
        }
        await using (var run = connection.CreateCommand())
        {
            run.Transaction = transaction;
            run.CommandText = """
                INSERT INTO metadata_resolution_runs (
                    id, task_id, status, tmdb_access_confirmed, fallback_eligible,
                    started_at_utc, completed_at_utc, attempt_number,
                    tmdb_series_id, tmdb_season_number, tmdb_movie_id)
                VALUES ($id, $task_id, 'resolved', 1, 0, $now, $now, $attempt_number,
                        $series_id, $season_number, $movie_id);
                """;
            run.Parameters.AddWithValue("$id", runId);
            run.Parameters.AddWithValue("$task_id", taskId);
            run.Parameters.AddWithValue("$now", now);
            run.Parameters.AddWithValue("$attempt_number", attemptNumber);
            run.Parameters.AddWithValue("$series_id", (object?)seriesId ?? DBNull.Value);
            run.Parameters.AddWithValue("$season_number", (object?)seasonNumber ?? DBNull.Value);
            run.Parameters.AddWithValue("$movie_id", (object?)movieId ?? DBNull.Value);
            await run.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        foreach (var stage in mediaType == "tv" ? new[] { "series", "season", "episode" } : new[] { "series" })
        {
            await using var attempt = connection.CreateCommand();
            attempt.Transaction = transaction;
            attempt.CommandText = """
                INSERT INTO metadata_resolution_attempts (
                    id, run_id, stage, strategy, result, retryable,
                    attempt_number, duration_ms, created_at_utc, reason)
                VALUES ($id, $run_id, $stage, 'manual_assignment', 'matched', 0, 1, 0, $now, 'webui_manual_assignment');
                """;
            attempt.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            attempt.Parameters.AddWithValue("$run_id", runId);
            attempt.Parameters.AddWithValue("$stage", stage);
            attempt.Parameters.AddWithValue("$now", now);
            await attempt.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await using var finish = connection.CreateCommand();
        finish.Transaction = transaction;
        finish.CommandText = """
            UPDATE ingest_tasks
            SET media_type = $media_type,
                status = CASE WHEN status IN ('download_preparing', 'download_queued', 'downloading', 'downloaded', 'download_error')
                              THEN status ELSE 'metadata_resolved' END,
                failure_kind = CASE WHEN status = 'download_error' THEN failure_kind ELSE NULL END,
                failure_reason = CASE WHEN status = 'download_error' THEN failure_reason ELSE NULL END,
                updated_at_utc = $now
            WHERE id = $task_id;
            UPDATE download_jobs
            SET organization_state = CASE WHEN state = 'complete' AND organization_state = 'not_required' THEN 'pending' ELSE organization_state END,
                updated_at_utc = $now
            WHERE task_id = $task_id;
            """;
        finish.Parameters.AddWithValue("$media_type", mediaType);
        finish.Parameters.AddWithValue("$now", now);
        finish.Parameters.AddWithValue("$task_id", taskId);
        await finish.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
