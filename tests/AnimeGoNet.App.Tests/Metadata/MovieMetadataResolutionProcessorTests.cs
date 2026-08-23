using AnimeGoNet.App.Metadata;
using AnimeGoNet.Core.Ingest;
using AnimeGoNet.Core.Media;
using AnimeGoNet.Core.Metadata;
using AnimeGoNet.Core.Torrents;
using AnimeGoNet.Data.Ingest;
using AnimeGoNet.Data.Sources;
using AnimeGoNet.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeGoNet.App.Tests.Metadata;

public sealed class MovieMetadataResolutionProcessorTests
{
    [Fact]
    public async Task SingleVideoMovieIsVerifiedAndNeverProjectedAsTvEpisode()
    {
        var tmdb = new FakeTmdbClient();
        await using var app = await RunningApp.StartAsync(
            tmdbClient: tmdb,
            bangumiSubjectClient: new FakeBangumiClient());
        var taskId = await SeedMovieAsync(app, "Spirited Away", includeSubtitle: true);

        Assert.True(await app.App.Services
            .GetRequiredService<AutomaticMetadataResolutionProcessor>()
            .RunOnceAsync());

        var database = app.App.Services.GetRequiredService<AnimeGoSqliteDatabase>();
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT task.status,
                   run.tmdb_movie_id,
                   run.tmdb_series_id,
                   run.tmdb_season_number,
                   (SELECT COUNT(*) FROM anime_movies WHERE tmdb_movie_id = 129),
                   (SELECT COUNT(*) FROM task_files
                    WHERE task_id = $task_id AND tmdb_movie_id = 129
                      AND tmdb_series_id IS NULL
                      AND tmdb_season_number IS NULL
                      AND tmdb_episode_number IS NULL),
                   (SELECT COUNT(*) FROM task_files
                    WHERE task_id = $task_id AND other_reason = 'movie'),
                   (SELECT COUNT(*) FROM task_files
                    WHERE task_id = $task_id AND other_reason = 'movie_subtitle'
                      AND associated_task_file_id IS NOT NULL)
            FROM ingest_tasks AS task
            JOIN metadata_resolution_runs AS run ON run.task_id = task.id
            WHERE task.id = $task_id;
            """;
        command.Parameters.AddWithValue("$task_id", taskId);
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal("metadata_resolved", reader.GetString(0));
        Assert.Equal(129, reader.GetInt32(1));
        Assert.True(reader.IsDBNull(2));
        Assert.True(reader.IsDBNull(3));
        Assert.Equal(1, reader.GetInt32(4));
        Assert.Equal(2, reader.GetInt32(5));
        Assert.Equal(1, reader.GetInt32(6));
        Assert.Equal(1, reader.GetInt32(7));
        Assert.Equal([129], tmdb.MovieDetailRequests);
        Assert.Empty(tmdb.TvSearches);
    }

    [Fact]
    public async Task MultipleMovieVideosFailBeforeTmdbOrAiMatching()
    {
        var tmdb = new FakeTmdbClient();
        await using var app = await RunningApp.StartAsync(
            tmdbClient: tmdb,
            bangumiSubjectClient: new FakeBangumiClient());
        var taskId = await SeedMovieAsync(app, "Movie Collection", secondVideo: true);

        Assert.True(await app.App.Services
            .GetRequiredService<AutomaticMetadataResolutionProcessor>()
            .RunOnceAsync());

        await using var connection = await app.App.Services
            .GetRequiredService<AnimeGoSqliteDatabase>()
            .OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT status, failure_reason FROM ingest_tasks WHERE id = $task_id;";
        command.Parameters.AddWithValue("$task_id", taskId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("metadata_failed", reader.GetString(0));
        Assert.Equal("movie_multiple_videos_unsupported", reader.GetString(1));
        Assert.Empty(tmdb.MovieSearches);
    }

    private static async Task<string> SeedMovieAsync(
        RunningApp app,
        string title,
        bool includeSubtitle = false,
        bool secondVideo = false)
    {
        var database = app.App.Services.GetRequiredService<AnimeGoSqliteDatabase>();
        var profiles = app.App.Services.GetRequiredService<SourceProfileStore>();
        var profile = Assert.IsType<SourceProfileRecord>(await profiles.GetEnabledAsync("mikan"));
        var normalization = IngestCommandNormalizer.Normalize(
            "mikan",
            new IngestItemCommand(
                "https://mikanani.me/Download/movie.torrent",
                new IngestItemInfo(
                    title, null, "movie-item", "3951",
                    "https://mikanani.me/Home/Episode/0123456789abcdef0123456789abcdef01234567",
                    null, 3951, 547888, null, null, 583, MediaTypes.Movie)));
        Assert.True(normalization.IsValid, string.Join(", ", normalization.Errors));
        var normalized = normalization.Item!;
        var files = new List<TorrentFile> { new("movie.mkv", 1_000, false) };
        if (includeSubtitle)
        {
            files.Add(new TorrentFile("movie.zh-CN.ass", 100, false));
        }
        if (secondVideo)
        {
            files.Add(new TorrentFile("bonus.mp4", 500, false));
        }

        var task = await app.App.Services.GetRequiredService<IngestTaskStore>().AddStagedAsync(
            normalized,
            profile,
            new TorrentMetadata(title, new string('a', 40), files.Sum(file => file.Size), files),
            $"movie-{Guid.NewGuid():N}.torrent",
            DateTimeOffset.UtcNow.AddMinutes(10));
        await using var connection = await database.OpenConnectionAsync();
        await using var update = connection.CreateCommand();
        update.CommandText = "UPDATE ingest_tasks SET status = 'download_preparing' WHERE id = $task_id;";
        update.Parameters.AddWithValue("$task_id", task.Id);
        Assert.Equal(1, await update.ExecuteNonQueryAsync());
        return task.Id;
    }

    private sealed class FakeTmdbClient : ITmdbClient, ITmdbMovieClient
    {
        public List<string> TvSearches { get; } = [];

        public List<string> MovieSearches { get; } = [];

        public List<int> MovieDetailRequests { get; } = [];

        public Task<IReadOnlyList<TmdbMovie>> SearchMoviesAsync(
            string title,
            CancellationToken cancellationToken = default)
        {
            MovieSearches.Add(title);
            return Task.FromResult<IReadOnlyList<TmdbMovie>>(
                [new TmdbMovie(129, "千与千寻", "Spirited Away", new DateOnly(2001, 7, 20), "/movie.jpg")]);
        }

        public Task<TmdbMovie?> GetMovieAsync(int movieId, CancellationToken cancellationToken = default)
        {
            MovieDetailRequests.Add(movieId);
            return Task.FromResult<TmdbMovie?>(
                new TmdbMovie(movieId, "千与千寻", "Spirited Away", new DateOnly(2001, 7, 20), "/movie.jpg"));
        }

        public Task<IReadOnlyList<TmdbSeries>> SearchSeriesAsync(
            string title,
            CancellationToken cancellationToken = default)
        {
            TvSearches.Add(title);
            return Task.FromResult<IReadOnlyList<TmdbSeries>>([]);
        }

        public Task<TmdbSeries?> GetSeriesAsync(int seriesId, CancellationToken cancellationToken = default) =>
            Task.FromResult<TmdbSeries?>(null);

        public Task<TmdbSeriesDetails?> GetSeriesDetailsAsync(int seriesId, CancellationToken cancellationToken = default) =>
            Task.FromResult<TmdbSeriesDetails?>(null);

        public Task<TmdbSeason?> GetSeasonAsync(int seriesId, int seasonNumber, CancellationToken cancellationToken = default) =>
            Task.FromResult<TmdbSeason?>(null);

        public Task<TmdbEpisode?> GetEpisodeAsync(
            int seriesId,
            int seasonNumber,
            int episodeNumber,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<TmdbEpisode?>(null);
    }

    private sealed class FakeBangumiClient : IBangumiSubjectClient
    {
        public Task<BangumiSubject?> GetSubjectAsync(
            int subjectId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<BangumiSubject?>(
                new BangumiSubject(subjectId, "Spirited Away", "千与千寻", new DateOnly(2001, 7, 20), 1));

        public Task<IReadOnlyList<BangumiSubjectRelation>> GetRelatedSubjectsAsync(
            int subjectId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BangumiSubjectRelation>>([]);
    }
}
