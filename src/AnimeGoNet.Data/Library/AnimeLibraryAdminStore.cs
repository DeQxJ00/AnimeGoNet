using System.Globalization;
using AnimeGoNet.Core.Metadata;
using AnimeGoNet.Data.Metadata;
using AnimeGoNet.Data.Sqlite;
using Microsoft.Data.Sqlite;

namespace AnimeGoNet.Data.Library;

public sealed class AnimeLibraryAdminStore(AnimeGoSqliteDatabase database)
{
    public async Task<AnimeLibraryMutationResult> CreateAsync(
        TmdbSeries series,
        TmdbSeason season,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default) =>
        await WriteAsync(
            series,
            season,
            expectedRevision: null,
            create: true,
            utcNow,
            cancellationToken).ConfigureAwait(false);

    public async Task<AnimeLibraryMutationResult> RefreshAsync(
        TmdbSeries series,
        TmdbSeason season,
        string expectedRevision,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default) =>
        await WriteAsync(
            series,
            season,
            NormalizeRevision(expectedRevision),
            create: false,
            utcNow,
            cancellationToken).ConfigureAwait(false);

    public async Task<AnimeLibraryMutationResult> DeleteAsync(
        int tmdbSeriesId,
        int seasonNumber,
        string expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(tmdbSeriesId, seasonNumber);
        var revision = NormalizeRevision(expectedRevision);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        await using var transaction = connection.BeginTransaction(deferred: false);
        var current = await FindAsync(
            connection,
            transaction,
            tmdbSeriesId,
            seasonNumber,
            cancellationToken).ConfigureAwait(false);
        if (current is null)
        {
            return new AnimeLibraryMutationResult(
                AnimeLibraryMutationStatus.NotFound,
                tmdbSeriesId,
                seasonNumber,
                null);
        }

        var currentRevision = Revision(current);
        if (!string.Equals(currentRevision, revision, StringComparison.Ordinal))
        {
            return new AnimeLibraryMutationResult(
                AnimeLibraryMutationStatus.RevisionConflict,
                tmdbSeriesId,
                seasonNumber,
                currentRevision);
        }

        var removeSeries = current.SeasonCount == 1;
        var references = await ReadReferencesAsync(
            connection,
            transaction,
            current.SeriesRowId,
            tmdbSeriesId,
            seasonNumber,
            removeSeries,
            cancellationToken).ConfigureAwait(false);
        if (references.Total > 0)
        {
            return new AnimeLibraryMutationResult(
                AnimeLibraryMutationStatus.InUse,
                tmdbSeriesId,
                seasonNumber,
                currentRevision,
                References: references);
        }

        await DeleteSeasonEpisodesAsync(
            connection,
            transaction,
            current.SeriesRowId,
            seasonNumber,
            cancellationToken).ConfigureAwait(false);
        await using (var deleteSeason = connection.CreateCommand())
        {
            deleteSeason.Transaction = transaction;
            deleteSeason.CommandText = "DELETE FROM anime_seasons WHERE id = $id;";
            deleteSeason.Parameters.AddWithValue("$id", current.SeasonRowId);
            await deleteSeason.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (removeSeries)
        {
            await using var deleteSeries = connection.CreateCommand();
            deleteSeries.Transaction = transaction;
            deleteSeries.CommandText = "DELETE FROM anime_series WHERE id = $id;";
            deleteSeries.Parameters.AddWithValue("$id", current.SeriesRowId);
            await deleteSeries.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new AnimeLibraryMutationResult(
            AnimeLibraryMutationStatus.Deleted,
            tmdbSeriesId,
            seasonNumber,
            null,
            removeSeries,
            references);
    }

    public async Task<AnimeMovieMutationResult> RefreshMovieAsync(
        TmdbMovie movie,
        string expectedRevision,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        ValidateMovie(movie);
        var revision = NormalizeRevision(expectedRevision);
        var now = utcNow.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction(deferred: false);
        var current = await FindMovieAsync(connection, transaction, movie.Id, cancellationToken)
            .ConfigureAwait(false);
        if (current is null)
        {
            return new AnimeMovieMutationResult(
                AnimeLibraryMutationStatus.NotFound,
                movie.Id,
                null);
        }

        var currentRevision = MovieRevision(current);
        if (!string.Equals(currentRevision, revision, StringComparison.Ordinal))
        {
            return new AnimeMovieMutationResult(
                AnimeLibraryMutationStatus.RevisionConflict,
                movie.Id,
                currentRevision);
        }

        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE anime_movies
            SET canonical_title = $title,
                original_title = $original_title,
                release_date = $release_date,
                poster_path = $poster_path,
                updated_at_utc = $updated_at
            WHERE tmdb_movie_id = $tmdb_movie_id;
            """;
        update.Parameters.AddWithValue("$tmdb_movie_id", movie.Id);
        update.Parameters.AddWithValue("$title", CanonicalMovieTitle(movie));
        update.Parameters.AddWithValue(
            "$original_title",
            string.IsNullOrWhiteSpace(movie.OriginalTitle)
                ? CanonicalMovieTitle(movie)
                : movie.OriginalTitle.Trim());
        update.Parameters.AddWithValue(
            "$release_date",
            movie.ReleaseDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? (object)DBNull.Value);
        update.Parameters.AddWithValue("$poster_path", movie.PosterPath ?? (object)DBNull.Value);
        update.Parameters.AddWithValue("$updated_at", now);
        await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new AnimeMovieMutationResult(
            AnimeLibraryMutationStatus.Updated,
            movie.Id,
            AnimeLibraryResourceRevision.CreateMovie(current.MovieRowId, movie.Id, now));
    }

    public async Task<AnimeMovieMutationResult> DeleteMovieAsync(
        int tmdbMovieId,
        string expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tmdbMovieId, 1);
        var revision = NormalizeRevision(expectedRevision);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction(deferred: false);
        var current = await FindMovieAsync(connection, transaction, tmdbMovieId, cancellationToken)
            .ConfigureAwait(false);
        if (current is null)
        {
            return new AnimeMovieMutationResult(
                AnimeLibraryMutationStatus.NotFound,
                tmdbMovieId,
                null);
        }

        var currentRevision = MovieRevision(current);
        if (!string.Equals(currentRevision, revision, StringComparison.Ordinal))
        {
            return new AnimeMovieMutationResult(
                AnimeLibraryMutationStatus.RevisionConflict,
                tmdbMovieId,
                currentRevision);
        }

        var references = await ReadMovieReferencesAsync(
            connection,
            transaction,
            tmdbMovieId,
            cancellationToken).ConfigureAwait(false);
        if (references.Total > 0)
        {
            return new AnimeMovieMutationResult(
                AnimeLibraryMutationStatus.InUse,
                tmdbMovieId,
                currentRevision,
                references);
        }

        await using (var releasedClaims = connection.CreateCommand())
        {
            releasedClaims.Transaction = transaction;
            releasedClaims.CommandText = "DELETE FROM movie_claims WHERE tmdb_movie_id = $tmdb_movie_id;";
            releasedClaims.Parameters.AddWithValue("$tmdb_movie_id", tmdbMovieId);
            await releasedClaims.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM anime_movies WHERE tmdb_movie_id = $tmdb_movie_id;";
            delete.Parameters.AddWithValue("$tmdb_movie_id", tmdbMovieId);
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new AnimeMovieMutationResult(
            AnimeLibraryMutationStatus.Deleted,
            tmdbMovieId,
            null,
            references);
    }

    public async Task<AnimeMovieFileContext?> GetMovieFileContextAsync(
        int tmdbMovieId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tmdbMovieId, 1);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction(deferred: true);
        var current = await FindMovieAsync(connection, transaction, tmdbMovieId, cancellationToken)
            .ConfigureAwait(false);
        if (current is null)
        {
            return null;
        }

        string? mainMediaPath;
        await using (var media = connection.CreateCommand())
        {
            media.Transaction = transaction;
            media.CommandText = """
                SELECT media_path
                FROM movie_completion_records
                WHERE tmdb_movie_id = $tmdb_movie_id
                LIMIT 1;
                """;
            media.Parameters.AddWithValue("$tmdb_movie_id", tmdbMovieId);
            var value = await media.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            mainMediaPath = value is string path && !string.IsNullOrWhiteSpace(path)
                ? path
                : null;
        }

        var references = await ReadMovieReferencesAsync(
            connection,
            transaction,
            tmdbMovieId,
            cancellationToken).ConfigureAwait(false);
        return new AnimeMovieFileContext(
            tmdbMovieId,
            MovieRevision(current),
            mainMediaPath,
            references);
    }

    public async Task<AnimeMovieMutationResult> ForceDeleteOrphanMovieAsync(
        int tmdbMovieId,
        string expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tmdbMovieId, 1);
        var revision = NormalizeRevision(expectedRevision);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction(deferred: false);
        var current = await FindMovieAsync(connection, transaction, tmdbMovieId, cancellationToken)
            .ConfigureAwait(false);
        if (current is null)
        {
            return new AnimeMovieMutationResult(
                AnimeLibraryMutationStatus.NotFound,
                tmdbMovieId,
                null);
        }

        var currentRevision = MovieRevision(current);
        if (!string.Equals(currentRevision, revision, StringComparison.Ordinal))
        {
            return new AnimeMovieMutationResult(
                AnimeLibraryMutationStatus.RevisionConflict,
                tmdbMovieId,
                currentRevision);
        }

        var references = await ReadMovieReferencesAsync(
            connection,
            transaction,
            tmdbMovieId,
            cancellationToken).ConfigureAwait(false);
        if (references.TaskFiles > 0 || references.ActiveClaims > 0)
        {
            return new AnimeMovieMutationResult(
                AnimeLibraryMutationStatus.InUse,
                tmdbMovieId,
                currentRevision,
                references);
        }

        await using (var deleteCompletion = connection.CreateCommand())
        {
            deleteCompletion.Transaction = transaction;
            deleteCompletion.CommandText =
                "DELETE FROM movie_completion_records WHERE tmdb_movie_id = $tmdb_movie_id;";
            deleteCompletion.Parameters.AddWithValue("$tmdb_movie_id", tmdbMovieId);
            await deleteCompletion.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var deleteClaims = connection.CreateCommand())
        {
            deleteClaims.Transaction = transaction;
            deleteClaims.CommandText = "DELETE FROM movie_claims WHERE tmdb_movie_id = $tmdb_movie_id;";
            deleteClaims.Parameters.AddWithValue("$tmdb_movie_id", tmdbMovieId);
            await deleteClaims.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var deleteMovie = connection.CreateCommand())
        {
            deleteMovie.Transaction = transaction;
            deleteMovie.CommandText = "DELETE FROM anime_movies WHERE tmdb_movie_id = $tmdb_movie_id;";
            deleteMovie.Parameters.AddWithValue("$tmdb_movie_id", tmdbMovieId);
            await deleteMovie.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new AnimeMovieMutationResult(
            AnimeLibraryMutationStatus.Deleted,
            tmdbMovieId,
            null,
            references);
    }

    private async Task<AnimeLibraryMutationResult> WriteAsync(
        TmdbSeries series,
        TmdbSeason season,
        string? expectedRevision,
        bool create,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        Validate(series, season);
        var now = utcNow.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        await using var transaction = connection.BeginTransaction(deferred: false);
        var current = await FindAsync(
            connection,
            transaction,
            series.Id,
            season.SeasonNumber,
            cancellationToken).ConfigureAwait(false);
        if (create && current is not null)
        {
            return new AnimeLibraryMutationResult(
                AnimeLibraryMutationStatus.AlreadyExists,
                series.Id,
                season.SeasonNumber,
                Revision(current));
        }

        if (!create && current is null)
        {
            return new AnimeLibraryMutationResult(
                AnimeLibraryMutationStatus.NotFound,
                series.Id,
                season.SeasonNumber,
                null);
        }

        if (!create
            && !string.Equals(
                Revision(current!),
                expectedRevision,
                StringComparison.Ordinal))
        {
            return new AnimeLibraryMutationResult(
                AnimeLibraryMutationStatus.RevisionConflict,
                series.Id,
                season.SeasonNumber,
                Revision(current!));
        }

        var seriesRowId = current?.SeriesRowId
            ?? await FindSeriesRowIdAsync(
                connection,
                transaction,
                series.Id,
                cancellationToken).ConfigureAwait(false)
            ?? Guid.NewGuid().ToString("N");
        if (current is null
            && !await SeriesExistsAsync(
                connection,
                transaction,
                seriesRowId,
                cancellationToken).ConfigureAwait(false))
        {
            await InsertSeriesAsync(
                connection,
                transaction,
                seriesRowId,
                series,
                now,
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await UpdateSeriesAsync(
                connection,
                transaction,
                seriesRowId,
                series,
                now,
                cancellationToken).ConfigureAwait(false);
        }

        var seasonRowId = current?.SeasonRowId ?? Guid.NewGuid().ToString("N");
        if (create)
        {
            await InsertSeasonAsync(
                connection,
                transaction,
                seasonRowId,
                seriesRowId,
                season,
                now,
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await UpdateSeasonAsync(
                connection,
                transaction,
                seasonRowId,
                season,
                now,
                cancellationToken).ConfigureAwait(false);
        }

        await TmdbEpisodeProjectionWriter.UpsertAsync(
            connection,
            transaction,
            seriesRowId,
            series.Id,
            season.SeasonNumber,
            season.EpisodeCount,
            season.Episodes,
            now,
            cancellationToken).ConfigureAwait(false);
        await DeleteStaleEpisodesAsync(
            connection,
            transaction,
            seriesRowId,
            season,
            cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new AnimeLibraryMutationResult(
            create
                ? AnimeLibraryMutationStatus.Created
                : AnimeLibraryMutationStatus.Updated,
            series.Id,
            season.SeasonNumber,
            AnimeLibraryResourceRevision.Create(
                seriesRowId,
                now,
                seasonRowId,
                now));
    }

    private static async Task<CurrentSeason?> FindAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int tmdbSeriesId,
        int seasonNumber,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT series.id, series.updated_at_utc,
                   season.id, season.updated_at_utc,
                   (SELECT COUNT(*) FROM anime_seasons WHERE series_id = series.id)
            FROM anime_series AS series
            JOIN anime_seasons AS season ON season.series_id = series.id
            WHERE series.tmdb_series_id = $tmdb_series_id
              AND series.needs_tmdb_completion = 0
              AND season.season_number = $season_number
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$tmdb_series_id", tmdbSeriesId);
        command.Parameters.AddWithValue("$season_number", seasonNumber);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new CurrentSeason(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt32(4))
            : null;
    }

    private static async Task<string?> FindSeriesRowIdAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int tmdbSeriesId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id
            FROM anime_series
            WHERE tmdb_series_id = $tmdb_series_id
              AND needs_tmdb_completion = 0;
            """;
        command.Parameters.AddWithValue("$tmdb_series_id", tmdbSeriesId);
        return (string?)await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> SeriesExistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string seriesRowId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM anime_series WHERE id = $id);";
        command.Parameters.AddWithValue("$id", seriesRowId);
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture) == 1;
    }

    private static async Task InsertSeriesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string seriesRowId,
        TmdbSeries series,
        string now,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO anime_series (
                id, tmdb_series_id, bangumi_subject_id,
                canonical_name, original_name, poster_path,
                needs_tmdb_completion, created_at_utc, updated_at_utc,
                first_air_date)
            VALUES (
                $id, $tmdb_series_id, NULL,
                $canonical_name, $original_name, $poster_path,
                0, $now, $now, $first_air_date);
            """;
        AddSeriesParameters(command, seriesRowId, series, now);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task UpdateSeriesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string seriesRowId,
        TmdbSeries series,
        string now,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE anime_series
            SET canonical_name = $canonical_name,
                original_name = $original_name,
                poster_path = COALESCE($poster_path, poster_path),
                first_air_date = COALESCE($first_air_date, first_air_date),
                updated_at_utc = $now
            WHERE id = $id;
            """;
        AddSeriesParameters(command, seriesRowId, series, now);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AddSeriesParameters(
        SqliteCommand command,
        string seriesRowId,
        TmdbSeries series,
        string now)
    {
        command.Parameters.AddWithValue("$id", seriesRowId);
        command.Parameters.AddWithValue("$tmdb_series_id", series.Id);
        command.Parameters.AddWithValue("$canonical_name", CanonicalSeriesName(series));
        command.Parameters.AddWithValue("$original_name", series.OriginalName);
        command.Parameters.AddWithValue("$poster_path", (object?)series.PosterPath ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$first_air_date",
            series.FirstAirDate is null
                ? DBNull.Value
                : series.FirstAirDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$now", now);
    }

    private static async Task InsertSeasonAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string seasonRowId,
        string seriesRowId,
        TmdbSeason season,
        string now,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO anime_seasons (
                id, series_id, season_number, canonical_name, poster_path,
                created_at_utc, updated_at_utc, air_date, episode_count)
            VALUES (
                $id, $series_id, $season_number, $canonical_name, $poster_path,
                $now, $now, $air_date, $episode_count);
            """;
        AddSeasonParameters(command, seasonRowId, seriesRowId, season, now);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task UpdateSeasonAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string seasonRowId,
        TmdbSeason season,
        string now,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE anime_seasons
            SET canonical_name = $canonical_name,
                poster_path = COALESCE($poster_path, poster_path),
                air_date = COALESCE($air_date, air_date),
                episode_count = $episode_count,
                updated_at_utc = $now
            WHERE id = $id;
            """;
        AddSeasonParameters(command, seasonRowId, string.Empty, season, now);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AddSeasonParameters(
        SqliteCommand command,
        string seasonRowId,
        string seriesRowId,
        TmdbSeason season,
        string now)
    {
        command.Parameters.AddWithValue("$id", seasonRowId);
        command.Parameters.AddWithValue("$series_id", seriesRowId);
        command.Parameters.AddWithValue("$season_number", season.SeasonNumber);
        command.Parameters.AddWithValue("$canonical_name", CanonicalSeasonName(season));
        command.Parameters.AddWithValue("$poster_path", (object?)season.PosterPath ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$air_date",
            season.AirDate is null
                ? DBNull.Value
                : season.AirDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$episode_count", season.EpisodeCount);
        command.Parameters.AddWithValue("$now", now);
    }

    private static async Task DeleteStaleEpisodesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string seriesRowId,
        TmdbSeason season,
        CancellationToken cancellationToken)
    {
        if (season.Episodes is null)
        {
            return;
        }

        var authoritativeNumbers = season.Episodes
            .Select(value => value.EpisodeNumber)
            .ToHashSet();
        var storedNumbers = new List<int>();
        await using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = """
                SELECT episode_number
                FROM tmdb_episodes
                WHERE series_id = $series_id
                  AND season_number = $season_number;
                """;
            select.Parameters.AddWithValue("$series_id", seriesRowId);
            select.Parameters.AddWithValue("$season_number", season.SeasonNumber);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                storedNumbers.Add(reader.GetInt32(0));
            }
        }

        foreach (var episodeNumber in storedNumbers.Where(value => !authoritativeNumbers.Contains(value)))
        {
            await using var delete = connection.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText = """
                DELETE FROM tmdb_episodes
                WHERE series_id = $series_id
                  AND season_number = $season_number
                  AND episode_number = $episode_number;
                """;
            delete.Parameters.AddWithValue("$series_id", seriesRowId);
            delete.Parameters.AddWithValue("$season_number", season.SeasonNumber);
            delete.Parameters.AddWithValue("$episode_number", episodeNumber);
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task DeleteSeasonEpisodesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string seriesRowId,
        int seasonNumber,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM tmdb_episodes
            WHERE series_id = $series_id
              AND season_number = $season_number;
            """;
        command.Parameters.AddWithValue("$series_id", seriesRowId);
        command.Parameters.AddWithValue("$season_number", seasonNumber);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<AnimeLibraryReferenceSummary> ReadReferencesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string seriesRowId,
        int tmdbSeriesId,
        int seasonNumber,
        bool seriesScope,
        CancellationToken cancellationToken)
    {
        var seasonPredicate = seriesScope ? string.Empty : " AND tmdb_season_number = $season_number";
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $$"""
            SELECT
                (SELECT COUNT(*) FROM task_files
                 WHERE tmdb_series_id = $tmdb_series_id{{seasonPredicate}}),
                (SELECT COUNT(*) FROM completion_records
                 WHERE tmdb_series_id = $tmdb_series_id{{seasonPredicate}}),
                (SELECT COUNT(*) FROM episode_claims
                 WHERE tmdb_series_id = $tmdb_series_id{{seasonPredicate}}),
                (SELECT COUNT(*) FROM mikan_work_rules
                 WHERE tmdb_series_id = $tmdb_series_id{{seasonPredicate}}),
                (SELECT COUNT(*) FROM fallback_completion_records
                 WHERE anime_series_id = $series_row_id),
                (SELECT COUNT(*) FROM pending_tmdb_nfo_rewrite_jobs
                 WHERE tmdb_series_id = $tmdb_series_id
                   AND state IN ('pending', 'writing', 'failed'));
            """;
        command.Parameters.AddWithValue("$tmdb_series_id", tmdbSeriesId);
        command.Parameters.AddWithValue("$season_number", seasonNumber);
        command.Parameters.AddWithValue("$series_row_id", seriesRowId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Library reference summary was not returned.");
        }

        return new AnimeLibraryReferenceSummary(
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetInt32(3),
            reader.GetInt32(4),
            reader.GetInt32(5));
    }

    private static async Task<CurrentMovie?> FindMovieAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int tmdbMovieId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id, updated_at_utc
            FROM anime_movies
            WHERE tmdb_movie_id = $tmdb_movie_id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$tmdb_movie_id", tmdbMovieId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new CurrentMovie(reader.GetString(0), tmdbMovieId, reader.GetString(1))
            : null;
    }

    private static async Task<AnimeMovieReferenceSummary> ReadMovieReferencesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int tmdbMovieId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM task_files WHERE tmdb_movie_id = $tmdb_movie_id),
                (SELECT COUNT(*) FROM movie_completion_records WHERE tmdb_movie_id = $tmdb_movie_id),
                (SELECT COUNT(*) FROM movie_claims
                  WHERE tmdb_movie_id = $tmdb_movie_id AND state <> 'released');
            """;
        command.Parameters.AddWithValue("$tmdb_movie_id", tmdbMovieId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Movie library reference summary was not returned.");
        }

        return new AnimeMovieReferenceSummary(
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.GetInt32(2));
    }

    private static string Revision(CurrentSeason value) =>
        AnimeLibraryResourceRevision.Create(
            value.SeriesRowId,
            value.SeriesUpdatedAtUtc,
            value.SeasonRowId,
            value.SeasonUpdatedAtUtc);

    private static string MovieRevision(CurrentMovie value) =>
        AnimeLibraryResourceRevision.CreateMovie(
            value.MovieRowId,
            value.TmdbMovieId,
            value.UpdatedAtUtc);

    private static string NormalizeRevision(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length != 64 || normalized.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("Library resource revision is invalid.", nameof(value));
        }

        return normalized;
    }

    private static void Validate(TmdbSeries series, TmdbSeason season)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(season);
        ValidateIdentity(series.Id, season.SeasonNumber);
        if (season.Id <= 0
            || season.SeriesId != series.Id
            || season.EpisodeCount < 0
            || series.Name.Length > 512
            || series.OriginalName.Length > 512
            || season.Name.Length > 512)
        {
            throw new ArgumentException("TMDB Series/Season identity is invalid.");
        }
    }

    private static void ValidateIdentity(int tmdbSeriesId, int seasonNumber)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tmdbSeriesId, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(seasonNumber, 1);
    }

    private static void ValidateMovie(TmdbMovie movie)
    {
        ArgumentNullException.ThrowIfNull(movie);
        ArgumentOutOfRangeException.ThrowIfLessThan(movie.Id, 1);
        if (movie.Title.Length > 512
            || movie.OriginalTitle.Length > 512
            || movie.PosterPath is { Length: > 1024 })
        {
            throw new ArgumentException("TMDB Movie identity is invalid.", nameof(movie));
        }
    }

    private static string CanonicalMovieTitle(TmdbMovie movie) =>
        !string.IsNullOrWhiteSpace(movie.Title)
            ? movie.Title.Trim()
            : !string.IsNullOrWhiteSpace(movie.OriginalTitle)
                ? movie.OriginalTitle.Trim()
                : $"TMDB Movie {movie.Id}";

    private static string CanonicalSeriesName(TmdbSeries series) =>
        !string.IsNullOrWhiteSpace(series.Name)
            ? series.Name.Trim()
            : !string.IsNullOrWhiteSpace(series.OriginalName)
                ? series.OriginalName.Trim()
                : $"TMDB {series.Id}";

    private static string CanonicalSeasonName(TmdbSeason season) =>
        !string.IsNullOrWhiteSpace(season.Name)
            ? season.Name.Trim()
            : $"Season {season.SeasonNumber}";

    private sealed record CurrentSeason(
        string SeriesRowId,
        string SeriesUpdatedAtUtc,
        string SeasonRowId,
        string SeasonUpdatedAtUtc,
        int SeasonCount);

    private sealed record CurrentMovie(
        string MovieRowId,
        int TmdbMovieId,
        string UpdatedAtUtc);
}
