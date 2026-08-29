using System.Globalization;
using AnimeGoNet.Core.Library;
using AnimeGoNet.Core.Metadata;
using AnimeGoNet.Data.Sqlite;
using Microsoft.Data.Sqlite;

namespace AnimeGoNet.Data.Metadata;

public sealed record MixedMediaPostprocessFile(
    string TaskFileId,
    string SourceName,
    long SizeBytes,
    string Disposition,
    string? OtherReason,
    int? TmdbSeriesId,
    int? TmdbSeasonNumber,
    int? TmdbEpisodeNumber,
    int? TmdbMovieId,
    string? MovieRole,
    string SourceMediaPath,
    bool MovieHint,
    bool PendingPostprocess,
    int SharedPathReferenceCount);

public sealed record MixedMediaPostprocessPreview(
    string TaskId,
    string Title,
    string TaskStatus,
    string MediaType,
    bool HasActivePostprocess,
    string PostprocessMode,
    string? OrganizationState,
    TmdbMovie? CurrentMovie,
    IReadOnlyList<MixedMediaPostprocessFile> Files);

public enum MixedMediaPostprocessResult
{
    Started,
    NotFound,
    NotEligible,
    FileNotEligible,
    MovieAlreadyCompleted,
    MovieClaimed,
}

public sealed class MixedMediaPostprocessStore(AnimeGoSqliteDatabase database)
{
    public async Task<MixedMediaPostprocessPreview?> PreviewAsync(
        string taskId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        await using var connection = await database.OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        string? title;
        string? status;
        string? mediaType;
        var active = false;
        string? organizationState;
        await using (var task = connection.CreateCommand())
        {
            task.CommandText = """
                SELECT title, status, media_type, EXISTS (
                    SELECT 1 FROM other_file_readaptation_jobs
                    WHERE task_id = $task_id AND state = 'pending'),
                    (SELECT organization_state FROM download_jobs
                     WHERE task_id = $task_id LIMIT 1)
                FROM ingest_tasks WHERE id = $task_id;
                """;
            task.Parameters.AddWithValue("$task_id", taskId);
            await using var reader = await task.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            title = reader.GetString(0);
            status = reader.GetString(1);
            mediaType = reader.GetString(2);
            active = reader.GetInt64(3) == 1;
            organizationState = reader.IsDBNull(4) ? null : reader.GetString(4);
        }

        var files = new List<MixedMediaPostprocessFile>();
        await using (var query = connection.CreateCommand())
        {
            query.CommandText = """
                SELECT file.id, file.relative_path, file.size_bytes,
                       file.disposition, file.other_reason,
                       file.tmdb_series_id, file.tmdb_season_number,
                       file.tmdb_episode_number, file.tmdb_movie_id,
                       CASE WHEN file.tmdb_movie_id IS NULL THEN NULL
                            WHEN file.associated_task_file_id IS NULL THEN 'movie'
                            ELSE 'extras' END,
                       COALESCE(active.source_media_path, operation.target_path),
                       active.id IS NOT NULL,
                       (SELECT COUNT(*) FROM file_operations AS shared
                        WHERE shared.target_path = COALESCE(active.source_media_path, operation.target_path)
                          AND shared.state = 'completed')
                FROM task_files AS file
                LEFT JOIN other_file_readaptation_jobs AS active
                  ON active.task_file_id = file.id AND active.state = 'pending'
                LEFT JOIN file_operations AS operation
                  ON operation.task_file_id = file.id AND operation.state = 'completed'
                WHERE file.task_id = $task_id
                  AND (file.associated_task_file_id IS NULL OR file.tmdb_movie_id IS NOT NULL)
                  AND file.disposition IN ('episode', 'movie', 'other', 'extras', 'ignored')
                  AND COALESCE(active.source_media_path, operation.target_path) IS NOT NULL
                  AND (
                    lower(file.relative_path) GLOB '*.mkv'
                    OR lower(file.relative_path) GLOB '*.mp4'
                    OR lower(file.relative_path) GLOB '*.avi'
                    OR lower(file.relative_path) GLOB '*.mov'
                    OR lower(file.relative_path) GLOB '*.m2ts'
                    OR lower(file.relative_path) GLOB '*.ts'
                    OR lower(file.relative_path) GLOB '*.webm')
                ORDER BY file.relative_path COLLATE NOCASE, file.id;
                """;
            query.Parameters.AddWithValue("$task_id", taskId);
            await using var reader = await query.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var path = reader.GetString(1);
                files.Add(new MixedMediaPostprocessFile(
                    reader.GetString(0),
                    path,
                    reader.GetInt64(2),
                    reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetInt32(5),
                    reader.IsDBNull(6) ? null : reader.GetInt32(6),
                    reader.IsDBNull(7) ? null : reader.GetInt32(7),
                    reader.IsDBNull(8) ? null : reader.GetInt32(8),
                    reader.IsDBNull(9) ? null : reader.GetString(9),
                    reader.GetString(10),
                    ContainsMovieHint(path),
                    reader.GetInt64(11) == 1,
                    reader.GetInt32(12)));
            }
        }

        TmdbMovie? currentMovie = null;
        var currentMovieId = files
            .OrderByDescending(file => file.PendingPostprocess)
            .Select(file => file.TmdbMovieId)
            .FirstOrDefault(id => id is > 0);
        if (currentMovieId is > 0)
        {
            await using var movie = connection.CreateCommand();
            movie.CommandText = """
                SELECT tmdb_movie_id, canonical_title, original_title, release_date, poster_path
                FROM anime_movies WHERE tmdb_movie_id = $tmdb_id LIMIT 1;
                """;
            movie.Parameters.AddWithValue("$tmdb_id", currentMovieId.Value);
            await using var reader = await movie.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                currentMovie = new TmdbMovie(
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.IsDBNull(3)
                        ? null
                        : DateOnly.ParseExact(reader.GetString(3), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                    reader.IsDBNull(4) ? null : reader.GetString(4));
            }
        }

        var mode = status == "organized" && !active
            ? "create"
            : status == "downloaded" && active && organizationState == "pending"
                ? "edit_pending"
                : "readonly";

        return new MixedMediaPostprocessPreview(
            taskId, title!, status!, mediaType!, active, mode, organizationState,
            currentMovie, files);
    }

    public Task<MixedMediaPostprocessResult> StartAsync(
        string taskId,
        string taskFileId,
        TmdbMovie movie,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default) =>
        StartAsync(taskId, new[] { taskFileId }, movie, utcNow, cancellationToken);

    public async Task<MixedMediaPostprocessResult> StartAsync(
        string taskId,
        IReadOnlyList<string> taskFileIds,
        TmdbMovie movie,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default) =>
        await StartAsync(
            taskId,
            taskFileIds.Count == 0 ? string.Empty : taskFileIds[0],
            taskFileIds.Skip(1).ToArray(),
            movie,
            utcNow,
            cancellationToken).ConfigureAwait(false);

    public async Task<MixedMediaPostprocessResult> StartAsync(
        string taskId,
        string movieTaskFileId,
        IReadOnlyList<string> movieExtraTaskFileIds,
        TmdbMovie movie,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        ArgumentException.ThrowIfNullOrWhiteSpace(movieTaskFileId);
        ArgumentNullException.ThrowIfNull(movieExtraTaskFileIds);
        ArgumentNullException.ThrowIfNull(movie);
        var mainFileId = movieTaskFileId.Trim();
        var extraFileIds = movieExtraTaskFileIds
            .Where(fileId => !string.IsNullOrWhiteSpace(fileId))
            .Select(fileId => fileId.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (extraFileIds.Contains(mainFileId, StringComparer.Ordinal))
        {
            throw new ArgumentException("The Movie main file cannot also be a Movie Extra.", nameof(movieExtraTaskFileIds));
        }
        var normalizedFileIds = new[] { mainFileId }.Concat(extraFileIds).ToArray();
        if (movie.Id <= 0 || string.IsNullOrWhiteSpace(movie.Title))
        {
            throw new ArgumentException("A validated TMDB Movie is required.", nameof(movie));
        }

        var preview = await PreviewAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (preview is null)
        {
            return MixedMediaPostprocessResult.NotFound;
        }
        if (preview.MediaType is not ("tv" or "movie") || preview.PostprocessMode == "readonly")
        {
            return MixedMediaPostprocessResult.NotEligible;
        }

        if (preview.PostprocessMode == "edit_pending")
        {
            return await UpdatePendingAsync(
                preview, mainFileId, extraFileIds, movie, utcNow, cancellationToken)
                .ConfigureAwait(false);
        }
        var filesById = preview.Files.ToDictionary(file => file.TaskFileId, StringComparer.Ordinal);
        var selected = normalizedFileIds
            .Where(filesById.ContainsKey)
            .Select(fileId => filesById[fileId])
            .ToArray();
        if (selected.Length != normalizedFileIds.Length
            || selected.Any(file => !FilePathInspector.HasExpectedFileLength(
                file.SourceMediaPath, file.SizeBytes)))
        {
            return MixedMediaPostprocessResult.FileNotEligible;
        }
        if (preview.MediaType == "movie"
            && (selected.Any(file => file.MovieRole == "movie")
                || selected.Any(file => file.TmdbMovieId == movie.Id)))
        {
            return MixedMediaPostprocessResult.FileNotEligible;
        }

        var now = utcNow.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        await using var connection = await database.OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        foreach (var candidate in selected)
        {
            await using var eligibility = connection.CreateCommand();
            eligibility.Transaction = transaction;
            eligibility.CommandText = """
                SELECT COUNT(*)
                FROM ingest_tasks AS task
                JOIN task_files AS file ON file.task_id = task.id
                JOIN file_operations AS operation
                  ON operation.task_file_id = file.id AND operation.state = 'completed'
                WHERE task.id = $task_id
                  AND task.status = 'organized'
                  AND task.media_type IN ('tv', 'movie')
                  AND file.id = $file_id
                  AND (
                    (task.media_type = 'tv'
                      AND file.disposition IN ('episode', 'other', 'extras', 'ignored'))
                    OR (task.media_type = 'movie'
                      AND file.disposition IN ('movie', 'extras')
                      AND file.associated_task_file_id IS NOT NULL)
                  )
                  AND operation.target_path = $source_path
                  AND file.size_bytes = $size_bytes
                  AND NOT EXISTS (
                    SELECT 1 FROM other_file_readaptation_jobs AS active
                    WHERE active.task_id = task.id AND active.state = 'pending');
                """;
            eligibility.Parameters.AddWithValue("$task_id", taskId);
            eligibility.Parameters.AddWithValue("$file_id", candidate.TaskFileId);
            eligibility.Parameters.AddWithValue("$source_path", candidate.SourceMediaPath);
            eligibility.Parameters.AddWithValue("$size_bytes", candidate.SizeBytes);
            if (Convert.ToInt64(
                    await eligibility.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
                    CultureInfo.InvariantCulture) != 1)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return MixedMediaPostprocessResult.FileNotEligible;
            }
        }

        await using (var completed = connection.CreateCommand())
        {
            completed.Transaction = transaction;
            completed.CommandText = "SELECT EXISTS (SELECT 1 FROM movie_completion_records WHERE tmdb_movie_id = $tmdb_id);";
            completed.Parameters.AddWithValue("$tmdb_id", movie.Id);
            if (Convert.ToInt64(
                    await completed.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
                    CultureInfo.InvariantCulture) == 1)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return MixedMediaPostprocessResult.MovieAlreadyCompleted;
            }
        }

        await using (var upsert = connection.CreateCommand())
        {
            upsert.Transaction = transaction;
            upsert.CommandText = """
                INSERT INTO anime_movies (
                    id, tmdb_movie_id, canonical_title, original_title,
                    release_date, poster_path, created_at_utc, updated_at_utc)
                VALUES ($id, $tmdb_id, $title, $original_title,
                        $release_date, $poster_path, $now, $now)
                ON CONFLICT(tmdb_movie_id) DO UPDATE SET
                    canonical_title = excluded.canonical_title,
                    original_title = excluded.original_title,
                    release_date = COALESCE(excluded.release_date, anime_movies.release_date),
                    poster_path = COALESCE(excluded.poster_path, anime_movies.poster_path),
                    updated_at_utc = excluded.updated_at_utc;
                """;
            upsert.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            upsert.Parameters.AddWithValue("$tmdb_id", movie.Id);
            upsert.Parameters.AddWithValue("$title", movie.Title.Trim());
            upsert.Parameters.AddWithValue("$original_title", movie.OriginalTitle.Trim());
            upsert.Parameters.AddWithValue(
                "$release_date",
                movie.ReleaseDate is null
                    ? DBNull.Value
                    : movie.ReleaseDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            upsert.Parameters.AddWithValue("$poster_path", (object?)movie.PosterPath ?? DBNull.Value);
            upsert.Parameters.AddWithValue("$now", now);
            await upsert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var claim = connection.CreateCommand())
        {
            claim.Transaction = transaction;
            claim.CommandText = """
                INSERT INTO movie_claims (
                    id, tmdb_movie_id, task_file_id, state, claimed_at_utc, expires_at_utc)
                VALUES ($id, $tmdb_id, $file_id, 'active', $now, $expires)
                ON CONFLICT(tmdb_movie_id) DO UPDATE SET
                    id = excluded.id,
                    task_file_id = excluded.task_file_id,
                    state = 'active',
                    claimed_at_utc = excluded.claimed_at_utc,
                    expires_at_utc = excluded.expires_at_utc
                WHERE movie_claims.state = 'released';
                """;
            claim.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            claim.Parameters.AddWithValue("$tmdb_id", movie.Id);
            claim.Parameters.AddWithValue("$file_id", mainFileId);
            claim.Parameters.AddWithValue("$now", now);
            claim.Parameters.AddWithValue(
                "$expires",
                utcNow.AddDays(7).ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            if (await claim.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return MixedMediaPostprocessResult.MovieClaimed;
            }
        }

        foreach (var candidate in selected)
        {
            await using var convert = connection.CreateCommand();
            convert.Transaction = transaction;
            convert.CommandText = """
                DELETE FROM completion_records
                WHERE tmdb_series_id = $series
                  AND tmdb_season_number = $season
                  AND tmdb_episode_number = $episode
                  AND $was_episode = 1;
                DELETE FROM episode_claims WHERE task_file_id = $file_id;
                INSERT INTO other_file_readaptation_jobs (
                    id, task_id, task_file_id, source_media_path,
                    original_other_reason, state, requested_at_utc, completed_at_utc,
                    preserve_source, original_disposition,
                    original_tmdb_series_id, original_tmdb_season_number,
                    original_tmdb_episode_number)
                VALUES (
                    $job_id, $task_id, $file_id, $source_path,
                    $original_reason, 'pending', $now, NULL, $preserve_source,
                    $original_disposition, $series, $season, $episode);
                DELETE FROM file_operations WHERE task_file_id = $file_id;
                UPDATE task_files
                SET disposition = 'movie', other_reason = NULL,
                    tmdb_movie_id = $tmdb_id,
                    tmdb_series_id = NULL, tmdb_season_number = NULL,
                    tmdb_episode_number = NULL, tmdb_episode_id = NULL,
                    associated_task_file_id = $associated_file_id, rename_suffix = NULL,
                    episode_resolution_source = NULL,
                    episode_resolution_run_id = NULL,
                    episode_resolution_attempt_id = NULL
                WHERE id = $file_id AND task_id = $task_id;
                UPDATE download_jobs
                SET organization_state = 'pending',
                    organization_lease_token = NULL,
                    organization_lease_expires_at_utc = NULL,
                    organization_next_attempt_at_utc = NULL,
                    organization_failure_code = NULL,
                    organization_phase = 'not_started',
                    organization_total_units = 0,
                    organization_completed_units = 0,
                    updated_at_utc = $now,
                    revision = revision + 1
                WHERE task_id = $task_id
                  AND organization_state IN ('completed', 'cleanup');
                UPDATE ingest_tasks
                SET status = 'downloaded', failure_kind = NULL, failure_reason = NULL,
                    updated_at_utc = $now
                WHERE id = $task_id AND status = 'organized';
                """;
            convert.Parameters.AddWithValue("$task_id", taskId);
            convert.Parameters.AddWithValue("$file_id", candidate.TaskFileId);
            convert.Parameters.AddWithValue("$tmdb_id", movie.Id);
            convert.Parameters.AddWithValue("$job_id", Guid.NewGuid().ToString("N"));
            convert.Parameters.AddWithValue(
                "$associated_file_id",
                candidate.TaskFileId == mainFileId
                    ? DBNull.Value
                    : mainFileId);
            convert.Parameters.AddWithValue("$source_path", candidate.SourceMediaPath);
            convert.Parameters.AddWithValue(
                "$original_reason",
                (object?)candidate.OtherReason ?? "mixed_media_manual_postprocess");
            convert.Parameters.AddWithValue("$preserve_source", candidate.SharedPathReferenceCount > 1 ? 1 : 0);
            convert.Parameters.AddWithValue("$original_disposition", candidate.Disposition);
            convert.Parameters.AddWithValue("$series", (object?)candidate.TmdbSeriesId ?? DBNull.Value);
            convert.Parameters.AddWithValue("$season", (object?)candidate.TmdbSeasonNumber ?? DBNull.Value);
            convert.Parameters.AddWithValue("$episode", (object?)candidate.TmdbEpisodeNumber ?? DBNull.Value);
            convert.Parameters.AddWithValue("$was_episode", candidate.Disposition == "episode" ? 1 : 0);
            convert.Parameters.AddWithValue("$now", now);
            await convert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return MixedMediaPostprocessResult.Started;
    }

    private async Task<MixedMediaPostprocessResult> UpdatePendingAsync(
        MixedMediaPostprocessPreview preview,
        string mainFileId,
        IReadOnlyList<string> extraFileIds,
        TmdbMovie movie,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        var selectedIds = new[] { mainFileId }.Concat(extraFileIds).ToHashSet(StringComparer.Ordinal);
        var pendingMovieFiles = preview.Files
            .Where(file => file.PendingPostprocess)
            .ToArray();
        if (pendingMovieFiles.Length == 0
            || !selectedIds.SetEquals(pendingMovieFiles.Select(file => file.TaskFileId))
            || pendingMovieFiles.Any(file => !FilePathInspector.HasExpectedFileLength(
                file.SourceMediaPath, file.SizeBytes)))
        {
            return MixedMediaPostprocessResult.FileNotEligible;
        }

        var now = utcNow.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using (var guard = connection.CreateCommand())
        {
            guard.Transaction = transaction;
            guard.CommandText = """
                SELECT COUNT(*)
                FROM ingest_tasks AS task
                JOIN download_jobs AS download ON download.task_id = task.id
                WHERE task.id = $task_id
                  AND task.status = 'downloaded'
                  AND task.media_type IN ('tv', 'movie')
                  AND download.organization_state = 'pending'
                  AND download.organization_lease_token IS NULL
                  AND (SELECT COUNT(*) FROM other_file_readaptation_jobs AS job
                       WHERE job.task_id = task.id AND job.state = 'pending') = $file_count;
                """;
            guard.Parameters.AddWithValue("$task_id", preview.TaskId);
            guard.Parameters.AddWithValue("$file_count", selectedIds.Count);
            if (Convert.ToInt64(
                    await guard.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
                    CultureInfo.InvariantCulture) != 1)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return MixedMediaPostprocessResult.NotEligible;
            }
        }

        await using (var completed = connection.CreateCommand())
        {
            completed.Transaction = transaction;
            completed.CommandText = "SELECT EXISTS (SELECT 1 FROM movie_completion_records WHERE tmdb_movie_id = $tmdb_id);";
            completed.Parameters.AddWithValue("$tmdb_id", movie.Id);
            if (Convert.ToInt64(
                    await completed.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
                    CultureInfo.InvariantCulture) == 1)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return MixedMediaPostprocessResult.MovieAlreadyCompleted;
            }
        }

        await UpsertMovieAsync(connection, transaction, movie, now, cancellationToken)
            .ConfigureAwait(false);

        await using (var release = connection.CreateCommand())
        {
            release.Transaction = transaction;
            release.CommandText = """
                UPDATE movie_claims
                SET state = 'released', expires_at_utc = NULL
                WHERE state = 'active'
                  AND task_file_id IN (
                    SELECT task_file_id FROM other_file_readaptation_jobs
                    WHERE task_id = $task_id AND state = 'pending');
                """;
            release.Parameters.AddWithValue("$task_id", preview.TaskId);
            await release.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var claim = connection.CreateCommand())
        {
            claim.Transaction = transaction;
            claim.CommandText = """
                INSERT INTO movie_claims (
                    id, tmdb_movie_id, task_file_id, state, claimed_at_utc, expires_at_utc)
                VALUES ($id, $tmdb_id, $file_id, 'active', $now, $expires)
                ON CONFLICT(tmdb_movie_id) DO UPDATE SET
                    id = excluded.id,
                    task_file_id = excluded.task_file_id,
                    state = 'active',
                    claimed_at_utc = excluded.claimed_at_utc,
                    expires_at_utc = excluded.expires_at_utc
                WHERE movie_claims.state = 'released';
                """;
            claim.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            claim.Parameters.AddWithValue("$tmdb_id", movie.Id);
            claim.Parameters.AddWithValue("$file_id", mainFileId);
            claim.Parameters.AddWithValue("$now", now);
            claim.Parameters.AddWithValue(
                "$expires",
                utcNow.AddDays(7).ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            if (await claim.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return MixedMediaPostprocessResult.MovieClaimed;
            }
        }

        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE task_files
                SET tmdb_movie_id = $tmdb_id,
                    associated_task_file_id = CASE WHEN id = $main_file_id THEN NULL ELSE $main_file_id END
                WHERE task_id = $task_id
                  AND id IN (
                    SELECT task_file_id FROM other_file_readaptation_jobs
                    WHERE task_id = $task_id AND state = 'pending');
                UPDATE ingest_tasks SET updated_at_utc = $now WHERE id = $task_id;
                UPDATE download_jobs SET updated_at_utc = $now, revision = revision + 1
                WHERE task_id = $task_id AND organization_state = 'pending';
                """;
            update.Parameters.AddWithValue("$tmdb_id", movie.Id);
            update.Parameters.AddWithValue("$main_file_id", mainFileId);
            update.Parameters.AddWithValue("$task_id", preview.TaskId);
            update.Parameters.AddWithValue("$now", now);
            await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (preview.CurrentMovie is { Id: var previousMovieId }
            && previousMovieId != movie.Id)
        {
            await using var cleanup = connection.CreateCommand();
            cleanup.Transaction = transaction;
            cleanup.CommandText = """
                DELETE FROM movie_claims
                WHERE tmdb_movie_id = $previous_tmdb_id AND state = 'released';
                DELETE FROM anime_movies
                WHERE tmdb_movie_id = $previous_tmdb_id
                  AND NOT EXISTS (
                    SELECT 1 FROM task_files WHERE tmdb_movie_id = $previous_tmdb_id)
                  AND NOT EXISTS (
                    SELECT 1 FROM movie_completion_records WHERE tmdb_movie_id = $previous_tmdb_id)
                  AND NOT EXISTS (
                    SELECT 1 FROM movie_claims
                    WHERE tmdb_movie_id = $previous_tmdb_id AND state <> 'released');
                """;
            cleanup.Parameters.AddWithValue("$previous_tmdb_id", previousMovieId);
            await cleanup.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return MixedMediaPostprocessResult.Started;
    }

    private static async Task UpsertMovieAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        TmdbMovie movie,
        string now,
        CancellationToken cancellationToken)
    {
        await using var upsert = connection.CreateCommand();
        upsert.Transaction = transaction;
        upsert.CommandText = """
            INSERT INTO anime_movies (
                id, tmdb_movie_id, canonical_title, original_title,
                release_date, poster_path, created_at_utc, updated_at_utc)
            VALUES ($id, $tmdb_id, $title, $original_title,
                    $release_date, $poster_path, $now, $now)
            ON CONFLICT(tmdb_movie_id) DO UPDATE SET
                canonical_title = excluded.canonical_title,
                original_title = excluded.original_title,
                release_date = COALESCE(excluded.release_date, anime_movies.release_date),
                poster_path = COALESCE(excluded.poster_path, anime_movies.poster_path),
                updated_at_utc = excluded.updated_at_utc;
            """;
        upsert.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        upsert.Parameters.AddWithValue("$tmdb_id", movie.Id);
        upsert.Parameters.AddWithValue("$title", movie.Title.Trim());
        upsert.Parameters.AddWithValue("$original_title", movie.OriginalTitle.Trim());
        upsert.Parameters.AddWithValue(
            "$release_date",
            movie.ReleaseDate is null
                ? DBNull.Value
                : movie.ReleaseDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        upsert.Parameters.AddWithValue("$poster_path", (object?)movie.PosterPath ?? DBNull.Value);
        upsert.Parameters.AddWithValue("$now", now);
        await upsert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static bool ContainsMovieHint(string relativePath) =>
        relativePath.Contains("劇場版", StringComparison.OrdinalIgnoreCase)
        || relativePath.Contains("剧场版", StringComparison.OrdinalIgnoreCase)
        || relativePath.Contains("Gekijouban", StringComparison.OrdinalIgnoreCase)
        || relativePath.Contains("movie", StringComparison.OrdinalIgnoreCase);
}
