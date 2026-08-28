using System.Net;
using System.Text;
using System.Text.Json;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Metadata;
using AnimeGoNet.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeGoNet.App.Tests.Api;

public sealed class AnimeLibraryAdminApiTests
{
    [Fact]
    public async Task MovieRefreshUsesTmdbSnapshotAndOptimisticRevision()
    {
        var tmdb = new MutableTmdbClient();
        await using var app = await RunningApp.StartAsync(tmdbClient: tmdb);
        var database = app.App.Services.GetRequiredService<AnimeGoSqliteDatabase>();
        await SeedMovieAsync(database, tmdb.Movie.Id);
        using var listed = await app.Client.GetAsync("/api/v1/library/movies");
        using var listedJson = JsonDocument.Parse(await listed.Content.ReadAsStreamAsync());
        var revision = Assert.Single(listedJson.RootElement.GetProperty("items").EnumerateArray())
            .GetProperty("resource_revision")
            .GetString()!;
        tmdb.Movie = tmdb.Movie with
        {
            Title = "Changed Movie",
            OriginalTitle = "Changed Original Movie",
            ReleaseDate = new DateOnly(2025, 2, 3),
            PosterPath = "/changed-movie.jpg",
        };

        using var refreshed = await app.Client.PutAsync(
            $"/api/v1/library/movies/{tmdb.Movie.Id}",
            Json($$"""{"expected_revision":"{{revision}}"}"""));
        using var refreshedJson = JsonDocument.Parse(await refreshed.Content.ReadAsStreamAsync());
        var newRevision = refreshedJson.RootElement.GetProperty("resource_revision").GetString();
        using var stale = await app.Client.PutAsync(
            $"/api/v1/library/movies/{tmdb.Movie.Id}",
            Json($$"""{"expected_revision":"{{revision}}"}"""));
        using var staleJson = JsonDocument.Parse(await stale.Content.ReadAsStreamAsync());
        using var reloaded = await app.Client.GetAsync("/api/v1/library/movies");
        using var reloadedJson = JsonDocument.Parse(await reloaded.Content.ReadAsStreamAsync());
        var item = Assert.Single(reloadedJson.RootElement.GetProperty("items").EnumerateArray());

        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        Assert.Equal("refreshed", refreshedJson.RootElement.GetProperty("result").GetString());
        Assert.NotEqual(revision, newRevision);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("library_revision_conflict", staleJson.RootElement.GetProperty("code").GetString());
        Assert.Equal("Changed Movie", item.GetProperty("title").GetString());
        Assert.Equal("Changed Original Movie", item.GetProperty("original_title").GetString());
        Assert.Equal("2025-02-03", item.GetProperty("release_date").GetString());
        Assert.Equal(2, tmdb.MovieDetailCallCount);
    }

    [Fact]
    public async Task MovieDeleteRejectsReferencesThenRemovesUnreferencedProjection()
    {
        var tmdb = new MutableTmdbClient();
        await using var app = await RunningApp.StartAsync(tmdbClient: tmdb);
        var database = app.App.Services.GetRequiredService<AnimeGoSqliteDatabase>();
        await SeedMovieAsync(database, tmdb.Movie.Id, includeCompletion: true);
        using var listed = await app.Client.GetAsync("/api/v1/library/movies");
        using var listedJson = JsonDocument.Parse(await listed.Content.ReadAsStreamAsync());
        var revision = Assert.Single(listedJson.RootElement.GetProperty("items").EnumerateArray())
            .GetProperty("resource_revision")
            .GetString()!;

        using var inUse = await app.Client.DeleteAsync(
            $"/api/v1/library/movies/{tmdb.Movie.Id}?expected_revision={revision}");
        var inUseBody = await inUse.Content.ReadAsStringAsync();
        using var inUseJson = JsonDocument.Parse(inUseBody);

        Assert.Equal(HttpStatusCode.Conflict, inUse.StatusCode);
        Assert.Equal("library_movie_in_use", inUseJson.RootElement.GetProperty("code").GetString());
        Assert.Contains("completion records: 1", inUseBody, StringComparison.Ordinal);

        await using (var connection = await database.OpenConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "DELETE FROM movie_completion_records WHERE tmdb_movie_id = $tmdb_movie_id;";
            command.Parameters.AddWithValue("$tmdb_movie_id", tmdb.Movie.Id);
            await command.ExecuteNonQueryAsync();
        }

        using var deleted = await app.Client.DeleteAsync(
            $"/api/v1/library/movies/{tmdb.Movie.Id}?expected_revision={revision}");
        using var deletedJson = JsonDocument.Parse(await deleted.Content.ReadAsStreamAsync());
        using var missing = await app.Client.GetAsync("/api/v1/library/movies");
        using var missingJson = JsonDocument.Parse(await missing.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        Assert.Equal("deleted", deletedJson.RootElement.GetProperty("result").GetString());
        Assert.Equal(0, missingJson.RootElement.GetProperty("total_items").GetInt32());
    }

    [Fact]
    public async Task MovieFilesClassifyMainExtrasAndForceDeleteOrphanMovie()
    {
        var tmdb = new MutableTmdbClient();
        await using var app = await RunningApp.StartAsync(tmdbClient: tmdb);
        var database = app.App.Services.GetRequiredService<AnimeGoSqliteDatabase>();
        var options = app.App.Services.GetRequiredService<AnimeGoOptions>();
        var movieDirectory = Path.Combine(options.Paths.EffectiveMovieSavePath, "Old Movie (2024)");
        var mainPath = Path.Combine(movieDirectory, "Old Movie (2024).mkv");
        var extraPath = Path.Combine(movieDirectory, "Old Movie - NCOP.mkv");
        var nfoPath = Path.Combine(movieDirectory, "movie.nfo");
        Directory.CreateDirectory(movieDirectory);
        await File.WriteAllBytesAsync(mainPath, new byte[] { 1, 2, 3 });
        await File.WriteAllBytesAsync(extraPath, new byte[] { 4, 5 });
        await File.WriteAllTextAsync(nfoPath, "nfo");
        await SeedMovieAsync(
            database,
            tmdb.Movie.Id,
            includeCompletion: true,
            mediaPath: mainPath);

        using var listed = await app.Client.GetAsync("/api/v1/library/movies");
        using var listedJson = JsonDocument.Parse(await listed.Content.ReadAsStreamAsync());
        var revision = Assert.Single(listedJson.RootElement.GetProperty("items").EnumerateArray())
            .GetProperty("resource_revision")
            .GetString()!;
        using var files = await app.Client.GetAsync($"/api/v1/library/movies/{tmdb.Movie.Id}/files");
        using var filesJson = JsonDocument.Parse(await files.Content.ReadAsStreamAsync());
        var fileItems = filesJson.RootElement.GetProperty("files").EnumerateArray().ToArray();

        Assert.Equal(HttpStatusCode.OK, files.StatusCode);
        Assert.True(filesJson.RootElement.GetProperty("can_force_delete").GetBoolean());
        Assert.Contains(fileItems, item => item.GetProperty("role").GetString() == "movie"
            && item.GetProperty("file_name").GetString() == Path.GetFileName(mainPath));
        Assert.Contains(fileItems, item => item.GetProperty("role").GetString() == "extras"
            && item.GetProperty("file_name").GetString() == Path.GetFileName(extraPath));
        Assert.Contains(fileItems, item => item.GetProperty("role").GetString() == "sidecar"
            && item.GetProperty("file_name").GetString() == Path.GetFileName(nfoPath));

        using var deleted = await app.Client.PostAsync(
            $"/api/v1/library/movies/{tmdb.Movie.Id}/force-delete",
            Json($$"""{"expected_revision":"{{revision}}","confirm_tmdb_movie_id":{{tmdb.Movie.Id}}}"""));
        using var deletedJson = JsonDocument.Parse(await deleted.Content.ReadAsStreamAsync());
        using var missing = await app.Client.GetAsync("/api/v1/library/movies");
        using var missingJson = JsonDocument.Parse(await missing.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        Assert.Equal("force_deleted", deletedJson.RootElement.GetProperty("result").GetString());
        Assert.Equal(3, deletedJson.RootElement.GetProperty("deleted_file_count").GetInt32());
        Assert.False(File.Exists(mainPath));
        Assert.False(File.Exists(extraPath));
        Assert.False(File.Exists(nfoPath));
        Assert.Equal(0, missingJson.RootElement.GetProperty("total_items").GetInt32());
    }

    [Fact]
    public async Task MovieForceDeleteRejectsRecordedPathOutsideMovieRoot()
    {
        var tmdb = new MutableTmdbClient();
        await using var app = await RunningApp.StartAsync(tmdbClient: tmdb);
        var database = app.App.Services.GetRequiredService<AnimeGoSqliteDatabase>();
        var outsidePath = Path.Combine(app.RootPath, "outside-movie.mkv");
        await File.WriteAllBytesAsync(outsidePath, new byte[] { 1, 2, 3 });
        await SeedMovieAsync(
            database,
            tmdb.Movie.Id,
            includeCompletion: true,
            mediaPath: outsidePath);
        using var listed = await app.Client.GetAsync("/api/v1/library/movies");
        using var listedJson = JsonDocument.Parse(await listed.Content.ReadAsStreamAsync());
        var revision = Assert.Single(listedJson.RootElement.GetProperty("items").EnumerateArray())
            .GetProperty("resource_revision")
            .GetString()!;

        using var deleted = await app.Client.PostAsync(
            $"/api/v1/library/movies/{tmdb.Movie.Id}/force-delete",
            Json($$"""{"expected_revision":"{{revision}}","confirm_tmdb_movie_id":{{tmdb.Movie.Id}}}"""));
        using var deletedJson = JsonDocument.Parse(await deleted.Content.ReadAsStreamAsync());
        using var stillListed = await app.Client.GetAsync("/api/v1/library/movies");
        using var stillListedJson = JsonDocument.Parse(await stillListed.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.Conflict, deleted.StatusCode);
        Assert.Equal(
            "library_movie_media_outside_root",
            deletedJson.RootElement.GetProperty("code").GetString());
        Assert.True(File.Exists(outsidePath));
        Assert.Equal(1, stillListedJson.RootElement.GetProperty("total_items").GetInt32());
    }

    [Fact]
    public async Task CreateValidatesTmdbAndPublishesCanonicalSeasonSnapshot()
    {
        var tmdb = new MutableTmdbClient();
        await using var app = await RunningApp.StartAsync(tmdbClient: tmdb);

        using var created = await app.Client.PostAsync(
            "/api/v1/library/seasons",
            Json("""{"tmdb_series_id":100,"tmdb_season_number":1}"""));
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStreamAsync());
        var revision = createdJson.RootElement.GetProperty("resource_revision").GetString();
        using var detail = await app.Client.GetAsync("/api/v1/library/seasons/100/1");
        using var detailJson = JsonDocument.Parse(await detail.Content.ReadAsStreamAsync());
        using var duplicate = await app.Client.PostAsync(
            "/api/v1/library/seasons",
            Json("""{"tmdb_series_id":100,"tmdb_season_number":1}"""));
        using var duplicateJson = JsonDocument.Parse(await duplicate.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("created", createdJson.RootElement.GetProperty("status").GetString());
        Assert.Equal(64, revision!.Length);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal("Canonical Series", detailJson.RootElement.GetProperty("display_name").GetString());
        Assert.Equal(revision, detailJson.RootElement.GetProperty("resource_revision").GetString());
        Assert.Equal(2, detailJson.RootElement.GetProperty("episode_total").GetInt32());
        Assert.Equal(2, detailJson.RootElement.GetProperty("episodes").GetArrayLength());
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(
            "library_season_exists",
            duplicateJson.RootElement.GetProperty("code").GetString());
        Assert.Equal(4, tmdb.CallCount);
    }

    [Fact]
    public async Task RefreshUsesOptimisticRevisionAndReplacesTmdbSnapshot()
    {
        var tmdb = new MutableTmdbClient();
        await using var app = await RunningApp.StartAsync(tmdbClient: tmdb);
        using var created = await app.Client.PostAsync(
            "/api/v1/library/seasons",
            Json("""{"tmdb_series_id":100,"tmdb_season_number":1}"""));
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStreamAsync());
        var revision = createdJson.RootElement.GetProperty("resource_revision").GetString()!;
        tmdb.Series = tmdb.Series with { Name = "Changed Series" };
        tmdb.Season = tmdb.Season with
        {
            Name = "Changed Season",
            EpisodeCount = 1,
            Episodes = [tmdb.Season.Episodes![0] with { Name = "Changed Episode" }],
        };

        using var refreshed = await app.Client.PutAsync(
            "/api/v1/library/seasons/100/1",
            Json($$"""{"expected_revision":"{{revision}}"}"""));
        using var refreshedJson = JsonDocument.Parse(await refreshed.Content.ReadAsStreamAsync());
        var newRevision = refreshedJson.RootElement.GetProperty("resource_revision").GetString();
        using var stale = await app.Client.PutAsync(
            "/api/v1/library/seasons/100/1",
            Json($$"""{"expected_revision":"{{revision}}"}"""));
        using var staleJson = JsonDocument.Parse(await stale.Content.ReadAsStreamAsync());
        using var detail = await app.Client.GetAsync("/api/v1/library/seasons/100/1");
        using var detailJson = JsonDocument.Parse(await detail.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        Assert.Equal("refreshed", refreshedJson.RootElement.GetProperty("status").GetString());
        Assert.NotEqual(revision, newRevision);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(
            "library_revision_conflict",
            staleJson.RootElement.GetProperty("code").GetString());
        Assert.Equal("Changed Series", detailJson.RootElement.GetProperty("display_name").GetString());
        Assert.Equal("Changed Season", detailJson.RootElement.GetProperty("season_name").GetString());
        Assert.Equal(1, detailJson.RootElement.GetProperty("episodes").GetArrayLength());
        Assert.Equal(4, tmdb.CallCount);
    }

    [Fact]
    public async Task DeleteRejectsBusinessReferencesThenRemovesUnreferencedProjection()
    {
        var tmdb = new MutableTmdbClient();
        await using var app = await RunningApp.StartAsync(tmdbClient: tmdb);
        using var created = await app.Client.PostAsync(
            "/api/v1/library/seasons",
            Json("""{"tmdb_series_id":100,"tmdb_season_number":1}"""));
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStreamAsync());
        var revision = createdJson.RootElement.GetProperty("resource_revision").GetString()!;
        var database = app.App.Services.GetRequiredService<AnimeGoSqliteDatabase>();
        await using (var connection = await database.OpenConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO completion_records (
                    id, tmdb_series_id, tmdb_season_number, tmdb_episode_number,
                    source_id, media_path, completed_at_utc)
                VALUES ('completion', 100, 1, 1, 'test', NULL, $now);
                """;
            command.Parameters.AddWithValue("$now", "2026-07-30T03:00:00.0000000+00:00");
            await command.ExecuteNonQueryAsync();
        }

        using var inUse = await app.Client.DeleteAsync(
            $"/api/v1/library/seasons/100/1?expected_revision={revision}");
        var inUseBody = await inUse.Content.ReadAsStringAsync();
        using var inUseJson = JsonDocument.Parse(inUseBody);
        Assert.Equal(HttpStatusCode.Conflict, inUse.StatusCode);
        Assert.Equal(
            "library_season_in_use",
            inUseJson.RootElement.GetProperty("code").GetString());
        Assert.Contains("completion records: 1", inUseBody, StringComparison.Ordinal);
        Assert.Contains("four-part deletion workflow", inUseBody, StringComparison.Ordinal);

        await using (var connection = await database.OpenConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "DELETE FROM completion_records WHERE id = 'completion';";
            await command.ExecuteNonQueryAsync();
        }

        using var deleted = await app.Client.DeleteAsync(
            $"/api/v1/library/seasons/100/1?expected_revision={revision}");
        using var deletedJson = JsonDocument.Parse(await deleted.Content.ReadAsStreamAsync());
        using var missing = await app.Client.GetAsync("/api/v1/library/seasons/100/1");

        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        Assert.Equal("deleted", deletedJson.RootElement.GetProperty("status").GetString());
        Assert.True(deletedJson.RootElement.GetProperty("series_removed").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task TmdbFailureReturnsSafeStatusAndCode()
    {
        var tmdb = new MutableTmdbClient
        {
            Failure = new TmdbClientException(
                MetadataFailureKind.Network,
                "tmdb_network_error",
                tmdbAccessConfirmed: false),
        };
        await using var app = await RunningApp.StartAsync(tmdbClient: tmdb);

        using var response = await app.Client.PostAsync(
            "/api/v1/library/seasons",
            Json("""{"tmdb_series_id":100,"tmdb_season_number":1}"""));
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(
            "tmdb_network_error",
            json.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("secret", body, StringComparison.OrdinalIgnoreCase);
    }

    private static StringContent Json(string value) =>
        new(value, Encoding.UTF8, "application/json");

    private static async Task SeedMovieAsync(
        AnimeGoSqliteDatabase database,
        int tmdbMovieId,
        bool includeCompletion = false,
        string? mediaPath = null)
    {
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO anime_movies (
                id, tmdb_movie_id, canonical_title, original_title,
                release_date, poster_path, created_at_utc, updated_at_utc)
            VALUES (
                'movie-admin', $tmdb_movie_id, 'Old Movie', 'Old Original Movie',
                '2024-01-01', '/old-movie.jpg', $now, $now);
            """ + (includeCompletion
                ? """

                  INSERT INTO movie_completion_records (
                      id, tmdb_movie_id, source_id, source_item_id,
                      media_path, completed_at_utc)
                  VALUES (
                      'movie-admin-completion', $tmdb_movie_id, 'test', 'movie-admin-source',
                      $media_path, $now);
                  """
                : string.Empty);
        command.Parameters.AddWithValue("$tmdb_movie_id", tmdbMovieId);
        command.Parameters.AddWithValue("$now", "2026-08-28T00:00:00.0000000+00:00");
        if (includeCompletion)
        {
            command.Parameters.AddWithValue("$media_path", mediaPath ?? "/movies/old.mkv");
        }
        await command.ExecuteNonQueryAsync();
    }

    private sealed class MutableTmdbClient : ITmdbClient, ITmdbMovieClient
    {
        public TmdbSeries Series { get; set; } = new(
            100,
            "Canonical Series",
            "Original Series",
            new DateOnly(2024, 1, 1),
            "/series.jpg");

        public TmdbSeason Season { get; set; } = new(
            1001,
            100,
            1,
            "Season One",
            new DateOnly(2024, 1, 1),
            2,
            "/season.jpg",
            [
                new TmdbEpisode(10001, 100, 1, 1, "Episode One", new DateOnly(2024, 1, 1)),
                new TmdbEpisode(10002, 100, 1, 2, "Episode Two", new DateOnly(2024, 1, 8)),
            ]);

        public TmdbMovie Movie { get; set; } = new(
            129,
            "Canonical Movie",
            "Original Movie",
            new DateOnly(2001, 7, 20),
            "/movie.jpg");

        public TmdbClientException? Failure { get; init; }

        public int CallCount { get; private set; }

        public int MovieDetailCallCount { get; private set; }

        public Task<IReadOnlyList<TmdbMovie>> SearchMoviesAsync(
            string title,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TmdbMovie>>([Movie]);

        public Task<TmdbMovie?> GetMovieAsync(
            int movieId,
            CancellationToken cancellationToken = default)
        {
            MovieDetailCallCount++;
            return Task.FromResult<TmdbMovie?>(movieId == Movie.Id ? Movie : null);
        }

        public Task<IReadOnlyList<TmdbSeries>> SearchSeriesAsync(
            string title,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TmdbSeries?> GetSeriesAsync(
            int seriesId,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (Failure is not null)
            {
                throw Failure;
            }

            return Task.FromResult<TmdbSeries?>(seriesId == Series.Id ? Series : null);
        }

        public Task<TmdbSeriesDetails?> GetSeriesDetailsAsync(
            int seriesId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TmdbSeason?> GetSeasonAsync(
            int seriesId,
            int seasonNumber,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult<TmdbSeason?>(
                seriesId == Season.SeriesId && seasonNumber == Season.SeasonNumber
                    ? Season
                    : null);
        }

        public Task<TmdbEpisode?> GetEpisodeAsync(
            int seriesId,
            int seasonNumber,
            int episodeNumber,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
