using System.Globalization;
using AnimeGoNet.Core.Metadata;
using AnimeGoNet.Data.Sqlite;
using Microsoft.Data.Sqlite;

namespace AnimeGoNet.Data.Metadata;

public sealed class PendingTmdbRecoveryStore(AnimeGoSqliteDatabase database)
{
    public async Task<PendingTmdbRecoveryResult> RecoverAsync(
        PendingTmdbRecoveryRequest request,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        Validate(request);
        var now = Format(utcNow);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var fallbackSeries = await FindFallbackSeriesAsync(
            connection,
            transaction,
            request.BangumiSubjectId,
            cancellationToken).ConfigureAwait(false);
        var fallbackRows = new List<FallbackRow>(request.Mappings.Count);
        foreach (var mapping in request.Mappings)
        {
            fallbackRows.Add(await ReadFallbackAsync(
                connection,
                transaction,
                fallbackSeries.Id,
                mapping,
                cancellationToken).ConfigureAwait(false));
        }

        foreach (var episode in request.Mappings.Select(value => value.Episode)
                     .DistinctBy(value => (value.SeriesId, value.SeasonNumber, value.EpisodeNumber)))
        {
            await EnsureNoActiveCanonicalClaimAsync(
                connection,
                transaction,
                episode,
                cancellationToken).ConfigureAwait(false);
        }

        var canonicalSeriesId = await UpsertCanonicalSeriesAsync(
            connection,
            transaction,
            request,
            now,
            cancellationToken).ConfigureAwait(false);
        foreach (var fallback in fallbackRows)
        {
            await EnqueueNfoRewritesAsync(
                connection,
                transaction,
                request,
                fallbackSeries.Name,
                fallback,
                now,
                cancellationToken).ConfigureAwait(false);
        }
        foreach (var season in request.Mappings.Select(value => value.Season)
                     .DistinctBy(value => value.SeasonNumber))
        {
            await UpsertSeasonAsync(
                connection,
                transaction,
                canonicalSeriesId,
                season,
                now,
                cancellationToken).ConfigureAwait(false);
        }

        foreach (var mapping in request.Mappings)
        {
            await UpsertEpisodeAsync(
                connection,
                transaction,
                canonicalSeriesId,
                mapping.Episode,
                now,
                cancellationToken).ConfigureAwait(false);
        }

        var mappingById = request.Mappings.ToDictionary(value => value.FallbackCompletionId, StringComparer.Ordinal);
        var results = new List<PendingTmdbRecoveryItemResult>(fallbackRows.Count);
        foreach (var fallback in fallbackRows
                     .OrderBy(value => value.CompletedAtUtc)
                     .ThenBy(value => value.Id, StringComparer.Ordinal))
        {
            var mapping = mappingById[fallback.Id];
            var completion = await FindCompletionAsync(
                connection,
                transaction,
                request.Series.Id,
                mapping.Episode.SeasonNumber,
                mapping.Episode.EpisodeNumber,
                cancellationToken).ConfigureAwait(false);
            var state = "duplicate_after_resolution";
            if (completion is null)
            {
                completion = Guid.NewGuid().ToString("N");
                await InsertCompletionAsync(
                    connection,
                    transaction,
                    completion,
                    request.Series.Id,
                    mapping,
                    fallback,
                    cancellationToken).ConfigureAwait(false);
                state = "resolved";
            }

            await InsertFallbackAliasAsync(
                connection,
                transaction,
                completion,
                fallback,
                now,
                cancellationToken).ConfigureAwait(false);
            await MarkFallbackResolvedAsync(
                connection,
                transaction,
                canonicalSeriesId,
                fallback,
                mapping,
                completion,
                state,
                request.ResolutionSource,
                now,
                cancellationToken).ConfigureAwait(false);
            results.Add(new PendingTmdbRecoveryItemResult(
                fallback.Id,
                mapping.Episode.SeasonNumber,
                mapping.Episode.EpisodeNumber,
                state,
                completion));
        }

        await ClearRecoveredTaskFailuresAsync(
            connection,
            transaction,
            request.BangumiSubjectId,
            now,
            cancellationToken).ConfigureAwait(false);

        var hasPending = await HasPendingAsync(
            connection,
            transaction,
            fallbackSeries.Id,
            cancellationToken).ConfigureAwait(false);
        if (!hasPending)
        {
            await using var deleteFallback = connection.CreateCommand();
            deleteFallback.Transaction = transaction;
            deleteFallback.CommandText = """
                DELETE FROM anime_series
                WHERE id = $series_id
                  AND tmdb_series_id = 0
                  AND needs_tmdb_completion = 1;
                """;
            deleteFallback.Parameters.AddWithValue("$series_id", fallbackSeries.Id);
            if (await deleteFallback.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            {
                throw new InvalidOperationException("Pending TMDB Series changed concurrently.");
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new PendingTmdbRecoveryResult(
            request.BangumiSubjectId,
            request.Series.Id,
            results,
            hasPending);
    }

    private static void Validate(PendingTmdbRecoveryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Series);
        ArgumentNullException.ThrowIfNull(request.Mappings);
        if (request.BangumiSubjectId <= 0
            || request.Series.Id <= 0
            || string.IsNullOrWhiteSpace(request.Series.Name)
            || request.Mappings.Count == 0
            || request.ResolutionSource is not ("manual" or "automatic"))
        {
            throw new ArgumentException("Pending TMDB recovery identity is invalid.", nameof(request));
        }

        if (request.Mappings.Select(value => value.FallbackCompletionId)
                .Any(string.IsNullOrWhiteSpace)
            || request.Mappings.Select(value => value.FallbackCompletionId)
                .Distinct(StringComparer.Ordinal).Count() != request.Mappings.Count)
        {
            throw new ArgumentException("Fallback completion IDs must be non-empty and unique.", nameof(request));
        }

        foreach (var mapping in request.Mappings)
        {
            ArgumentNullException.ThrowIfNull(mapping.Season);
            ArgumentNullException.ThrowIfNull(mapping.Episode);
            if (mapping.Season.Id <= 0
                || mapping.Season.SeriesId != request.Series.Id
                || mapping.Season.SeasonNumber <= 0
                || mapping.Episode.Id <= 0
                || mapping.Episode.SeriesId != request.Series.Id
                || mapping.Episode.SeasonNumber != mapping.Season.SeasonNumber
                || mapping.Episode.EpisodeNumber <= 0)
            {
                throw new ArgumentException(
                    "Every recovery mapping requires one validated TMDB Series/Season/Episode identity.",
                    nameof(request));
            }
        }
    }

    private static async Task<FallbackSeries> FindFallbackSeriesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int bangumiSubjectId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id, COALESCE(NULLIF(canonical_name, ''), NULLIF(original_name, ''),
                                'Bangumi ' || bangumi_subject_id)
            FROM anime_series
            WHERE tmdb_series_id = 0
              AND needs_tmdb_completion = 1
              AND bangumi_subject_id = $bgmid;
            """;
        command.Parameters.AddWithValue("$bgmid", bangumiSubjectId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new KeyNotFoundException("Pending TMDB Series was not found.");
        }

        return new FallbackSeries(reader.GetString(0), reader.GetString(1));
    }

    private static async Task EnqueueNfoRewritesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        PendingTmdbRecoveryRequest request,
        string fallbackSeriesName,
        FallbackRow fallback,
        string now,
        CancellationToken cancellationToken)
    {
        var saveRoots = new List<string>();
        await using (var targets = connection.CreateCommand())
        {
            targets.Transaction = transaction;
            targets.CommandText = """
                SELECT DISTINCT job.save_root_path
                FROM fallback_claims AS claim
                JOIN task_files AS file ON file.id = claim.task_file_id
                JOIN download_jobs AS job ON job.task_id = file.task_id
                WHERE claim.scope_kind = $scope_kind
                  AND claim.scope_key = $scope_key
                  AND job.save_root_path IS NOT NULL
                  AND trim(job.save_root_path) <> '';
                """;
            targets.Parameters.AddWithValue("$scope_kind", fallback.ScopeKind);
            targets.Parameters.AddWithValue("$scope_key", fallback.ScopeKey);
            await using var reader = await targets.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                saveRoots.Add(reader.GetString(0));
            }
        }

        foreach (var saveRoot in saveRoots)
        {
            await using var enqueue = connection.CreateCommand();
            enqueue.Transaction = transaction;
            enqueue.CommandText = """
                INSERT INTO pending_tmdb_nfo_rewrite_jobs (
                    id, bangumi_subject_id, tmdb_series_id, save_root_path,
                    series_directory_name, canonical_series_name, state,
                    created_at_utc, updated_at_utc)
                VALUES (
                    $id, $bgmid, $tmdb_id, $save_root,
                    $directory_name, $canonical_name, 'pending', $now, $now)
                ON CONFLICT(
                    bangumi_subject_id, tmdb_series_id, save_root_path, series_directory_name)
                DO NOTHING;
                """;
            enqueue.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            enqueue.Parameters.AddWithValue("$bgmid", request.BangumiSubjectId);
            enqueue.Parameters.AddWithValue("$tmdb_id", request.Series.Id);
            enqueue.Parameters.AddWithValue("$save_root", saveRoot);
            enqueue.Parameters.AddWithValue("$directory_name", fallbackSeriesName);
            enqueue.Parameters.AddWithValue("$canonical_name", request.Series.Name);
            enqueue.Parameters.AddWithValue("$now", now);
            await enqueue.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (saveRoots.Count == 0)
        {
            throw new InvalidOperationException(
                "Pending TMDB recovery requires a captured save root for NFO rewrite.");
        }
    }

    private static async Task<FallbackRow> ReadFallbackAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string fallbackSeriesId,
        PendingTmdbRecoveryMapping mapping,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id, scope_kind, scope_key, source_id, source_episode,
                   media_path, completed_at_utc
            FROM fallback_completion_records
            WHERE id = $id
              AND anime_series_id = $series_id
              AND resolution_state = 'pending';
            """;
        command.Parameters.AddWithValue("$id", mapping.FallbackCompletionId);
        command.Parameters.AddWithValue("$series_id", fallbackSeriesId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new KeyNotFoundException(
                $"Pending fallback completion '{mapping.FallbackCompletionId}' was not found.");
        }

        return new FallbackRow(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            DateTimeOffset.Parse(
                reader.GetString(6),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind));
    }

    private static async Task<string> UpsertCanonicalSeriesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        PendingTmdbRecoveryRequest request,
        string now,
        CancellationToken cancellationToken)
    {
        await using (var conflict = connection.CreateCommand())
        {
            conflict.Transaction = transaction;
            conflict.CommandText = """
                SELECT bangumi_subject_id FROM anime_series
                WHERE tmdb_series_id = $tmdb_id;
                """;
            conflict.Parameters.AddWithValue("$tmdb_id", request.Series.Id);
            var existingBangumi = await conflict.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (existingBangumi is not null
                && existingBangumi != DBNull.Value
                && Convert.ToInt32(existingBangumi, CultureInfo.InvariantCulture) != request.BangumiSubjectId)
            {
                throw new InvalidOperationException(
                    "The canonical TMDB Series is already bound to another Bangumi subject.");
            }
        }

        var seriesRowId = Guid.NewGuid().ToString("N");
        await using (var upsert = connection.CreateCommand())
        {
            upsert.Transaction = transaction;
            upsert.CommandText = """
                INSERT INTO anime_series (
                    id, tmdb_series_id, bangumi_subject_id, canonical_name,
                    original_name, poster_path, needs_tmdb_completion,
                    created_at_utc, updated_at_utc)
                VALUES (
                    $id, $tmdb_id, $bgmid, $canonical_name,
                    $original_name, NULL, 0, $now, $now)
                ON CONFLICT(tmdb_series_id) WHERE tmdb_series_id > 0 DO UPDATE SET
                    bangumi_subject_id = COALESCE(anime_series.bangumi_subject_id, excluded.bangumi_subject_id),
                    canonical_name = excluded.canonical_name,
                    original_name = excluded.original_name,
                    updated_at_utc = excluded.updated_at_utc;
                """;
            upsert.Parameters.AddWithValue("$id", seriesRowId);
            upsert.Parameters.AddWithValue("$tmdb_id", request.Series.Id);
            upsert.Parameters.AddWithValue("$bgmid", request.BangumiSubjectId);
            upsert.Parameters.AddWithValue("$canonical_name", request.Series.Name);
            upsert.Parameters.AddWithValue("$original_name", request.Series.OriginalName);
            upsert.Parameters.AddWithValue("$now", now);
            await upsert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var find = connection.CreateCommand();
        find.Transaction = transaction;
        find.CommandText = "SELECT id FROM anime_series WHERE tmdb_series_id = $tmdb_id;";
        find.Parameters.AddWithValue("$tmdb_id", request.Series.Id);
        return (string)(await find.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Canonical TMDB Series upsert did not return a row."));
    }

    private static async Task EnsureNoActiveCanonicalClaimAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        TmdbEpisode episode,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT EXISTS(
                SELECT 1 FROM episode_claims
                WHERE tmdb_series_id = $series_id
                  AND tmdb_season_number = $season_number
                  AND tmdb_episode_number = $episode_number
                  AND state = 'active');
            """;
        command.Parameters.AddWithValue("$series_id", episode.SeriesId);
        command.Parameters.AddWithValue("$season_number", episode.SeasonNumber);
        command.Parameters.AddWithValue("$episode_number", episode.EpisodeNumber);
        if (Convert.ToInt32(
                await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
                CultureInfo.InvariantCulture) == 1)
        {
            throw new InvalidOperationException(
                "The canonical TMDB Episode is currently claimed by another task.");
        }
    }

    private static async Task UpsertSeasonAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string canonicalSeriesId,
        TmdbSeason season,
        string now,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO anime_seasons (
                id, series_id, season_number, canonical_name, poster_path,
                created_at_utc, updated_at_utc)
            VALUES ($id, $series_id, $season_number, $name, NULL, $now, $now)
            ON CONFLICT(series_id, season_number) DO UPDATE SET
                canonical_name = excluded.canonical_name,
                updated_at_utc = excluded.updated_at_utc;
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("$series_id", canonicalSeriesId);
        command.Parameters.AddWithValue("$season_number", season.SeasonNumber);
        command.Parameters.AddWithValue("$name", season.Name);
        command.Parameters.AddWithValue("$now", now);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task UpsertEpisodeAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string canonicalSeriesId,
        TmdbEpisode episode,
        string now,
        CancellationToken cancellationToken)
    {
        await using (var episodeId = connection.CreateCommand())
        {
            episodeId.Transaction = transaction;
            episodeId.CommandText = """
                SELECT series_id, season_number, episode_number
                FROM tmdb_episodes
                WHERE tmdb_episode_id = $episode_id;
                """;
            episodeId.Parameters.AddWithValue("$episode_id", episode.Id);
            await using var reader = await episodeId.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
                && (reader.GetString(0) != canonicalSeriesId
                    || reader.GetInt32(1) != episode.SeasonNumber
                    || reader.GetInt32(2) != episode.EpisodeNumber))
            {
                throw new InvalidOperationException(
                    "The TMDB Episode ID is already bound to another canonical identity.");
            }
        }

        await using (var identity = connection.CreateCommand())
        {
            identity.Transaction = transaction;
            identity.CommandText = """
                SELECT tmdb_episode_id FROM tmdb_episodes
                WHERE series_id = $series_id
                  AND season_number = $season_number
                  AND episode_number = $episode_number;
                """;
            identity.Parameters.AddWithValue("$series_id", canonicalSeriesId);
            identity.Parameters.AddWithValue("$season_number", episode.SeasonNumber);
            identity.Parameters.AddWithValue("$episode_number", episode.EpisodeNumber);
            var existingId = await identity.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (existingId is not null
                && Convert.ToInt32(existingId, CultureInfo.InvariantCulture) != episode.Id)
            {
                throw new InvalidOperationException(
                    "The canonical TMDB Episode identity conflicts with the stored episode ID.");
            }
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO tmdb_episodes (
                tmdb_episode_id, series_id, season_number, episode_number,
                name, air_date, runtime_minutes, fetched_at_utc)
            VALUES (
                $episode_id, $series_id, $season_number, $episode_number,
                $name, $air_date, NULL, $now)
            ON CONFLICT(tmdb_episode_id) DO UPDATE SET
                name = excluded.name,
                air_date = excluded.air_date,
                fetched_at_utc = excluded.fetched_at_utc;
            """;
        command.Parameters.AddWithValue("$episode_id", episode.Id);
        command.Parameters.AddWithValue("$series_id", canonicalSeriesId);
        command.Parameters.AddWithValue("$season_number", episode.SeasonNumber);
        command.Parameters.AddWithValue("$episode_number", episode.EpisodeNumber);
        command.Parameters.AddWithValue("$name", episode.Name);
        command.Parameters.AddWithValue(
            "$air_date",
            episode.AirDate is null
                ? DBNull.Value
                : episode.AirDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$now", now);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string?> FindCompletionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int tmdbSeriesId,
        int seasonNumber,
        int episodeNumber,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id FROM completion_records
            WHERE tmdb_series_id = $series_id
              AND tmdb_season_number = $season_number
              AND tmdb_episode_number = $episode_number;
            """;
        command.Parameters.AddWithValue("$series_id", tmdbSeriesId);
        command.Parameters.AddWithValue("$season_number", seasonNumber);
        command.Parameters.AddWithValue("$episode_number", episodeNumber);
        return (string?)await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertCompletionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string completionId,
        int tmdbSeriesId,
        PendingTmdbRecoveryMapping mapping,
        FallbackRow fallback,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO completion_records (
                id, tmdb_series_id, tmdb_season_number, tmdb_episode_number,
                source_id, source_item_id, media_path, completed_at_utc)
            VALUES (
                $id, $series_id, $season_number, $episode_number,
                $source_id, NULL, $media_path, $completed_at);
            """;
        command.Parameters.AddWithValue("$id", completionId);
        command.Parameters.AddWithValue("$series_id", tmdbSeriesId);
        command.Parameters.AddWithValue("$season_number", mapping.Episode.SeasonNumber);
        command.Parameters.AddWithValue("$episode_number", mapping.Episode.EpisodeNumber);
        command.Parameters.AddWithValue("$source_id", fallback.SourceId);
        command.Parameters.AddWithValue("$media_path", (object?)fallback.MediaPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$completed_at", Format(fallback.CompletedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertFallbackAliasAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string completionId,
        FallbackRow fallback,
        string now,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO completion_aliases (
                id, completion_id, source_id, source_work_id, source_episode,
                info_hash, created_at_utc, fallback_scope_kind, fallback_scope_key)
            VALUES (
                $id, $completion_id, $source_id, NULL, $source_episode,
                NULL, $now, $scope_kind, $scope_key);
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("$completion_id", completionId);
        command.Parameters.AddWithValue("$source_id", fallback.SourceId);
        command.Parameters.AddWithValue("$source_episode", (object?)fallback.SourceEpisode ?? DBNull.Value);
        command.Parameters.AddWithValue("$now", now);
        command.Parameters.AddWithValue("$scope_kind", fallback.ScopeKind);
        command.Parameters.AddWithValue("$scope_key", fallback.ScopeKey);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task MarkFallbackResolvedAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string canonicalSeriesId,
        FallbackRow fallback,
        PendingTmdbRecoveryMapping mapping,
        string completionId,
        string state,
        string resolutionSource,
        string now,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE fallback_completion_records
            SET anime_series_id = $canonical_series_id,
                resolution_state = $state,
                resolved_completion_id = $completion_id,
                resolved_at_utc = $now,
                resolution_source = $resolution_source
            WHERE id = $fallback_id
              AND resolution_state = 'pending';

            UPDATE task_files
            SET tmdb_series_id = $tmdb_series_id,
                tmdb_season_number = $season_number,
                tmdb_episode_number = $episode_number,
                tmdb_episode_id = $tmdb_episode_id,
                disposition = $disposition,
                other_reason = $reason
            WHERE id IN (
                SELECT task_file_id FROM fallback_claims
                WHERE scope_kind = $scope_kind AND scope_key = $scope_key);
            """;
        command.Parameters.AddWithValue("$canonical_series_id", canonicalSeriesId);
        command.Parameters.AddWithValue("$state", state);
        command.Parameters.AddWithValue("$completion_id", completionId);
        command.Parameters.AddWithValue("$now", now);
        command.Parameters.AddWithValue("$resolution_source", resolutionSource);
        command.Parameters.AddWithValue("$fallback_id", fallback.Id);
        command.Parameters.AddWithValue("$tmdb_series_id", mapping.Episode.SeriesId);
        command.Parameters.AddWithValue("$season_number", mapping.Episode.SeasonNumber);
        command.Parameters.AddWithValue("$episode_number", mapping.Episode.EpisodeNumber);
        command.Parameters.AddWithValue("$tmdb_episode_id", mapping.Episode.Id);
        command.Parameters.AddWithValue(
            "$disposition",
            state == "resolved" ? "episode" : "duplicate");
        command.Parameters.AddWithValue(
            "$reason",
            state == "resolved" ? "tmdb_recovered" : "duplicate_after_resolution");
        command.Parameters.AddWithValue("$scope_kind", fallback.ScopeKind);
        command.Parameters.AddWithValue("$scope_key", fallback.ScopeKey);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) < 1)
        {
            throw new InvalidOperationException("Fallback completion changed concurrently.");
        }
    }

    private static async Task ClearRecoveredTaskFailuresAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int bangumiSubjectId,
        string now,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE ingest_tasks
            SET failure_kind = NULL, failure_reason = NULL, updated_at_utc = $now
            WHERE bangumi_subject_id = $bgmid
              AND failure_kind = 'tmdb_completion_pending'
              AND NOT EXISTS (
                  SELECT 1 FROM task_files AS file
                  WHERE file.task_id = ingest_tasks.id
                    AND file.other_reason = 'tmdb_fallback_pending_completion');
            """;
        command.Parameters.AddWithValue("$bgmid", bangumiSubjectId);
        command.Parameters.AddWithValue("$now", now);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> HasPendingAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string fallbackSeriesId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT EXISTS(
                SELECT 1 FROM fallback_completion_records
                WHERE anime_series_id = $series_id
                  AND resolution_state = 'pending');
            """;
        command.Parameters.AddWithValue("$series_id", fallbackSeriesId);
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture) == 1;
    }

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private sealed record FallbackRow(
        string Id,
        string ScopeKind,
        string ScopeKey,
        string SourceId,
        string? SourceEpisode,
        string? MediaPath,
        DateTimeOffset CompletedAtUtc);

    private sealed record FallbackSeries(string Id, string Name);
}
