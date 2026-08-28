using AnimeGoNet.App.Metadata;
using AnimeGoNet.App.Library;
using AnimeGoNet.App.Downloads;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Downloads;
using AnimeGoNet.Core.Ingest;
using AnimeGoNet.Core.Media;
using AnimeGoNet.Core.Metadata;
using AnimeGoNet.Core.Torrents;
using AnimeGoNet.Data.Ingest;
using AnimeGoNet.Data.Sources;
using AnimeGoNet.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text;
using System.Text.Json;

namespace AnimeGoNet.App.Tests.Metadata;

public sealed class MovieMetadataResolutionProcessorTests
{
    [Fact]
    public async Task OrganizedTvCollectionCanMoveUnhintedVideoThroughValidatedPostprocess()
    {
        var tmdb = new FakeTmdbClient();
        await using var app = await RunningApp.StartAsync(tmdbClient: tmdb);
        var paths = AnimeGoDefaults.CreateNative(app.RootPath).Paths;
        var profile = Assert.IsType<SourceProfileRecord>(await app.App.Services
            .GetRequiredService<SourceProfileStore>().GetEnabledAsync("mikan"));
        const string fileName = "合集 电影正片.mkv";
        const string extraFileName = "Movie 特典.mkv";
        var normalized = Assert.IsType<NormalizedIngestItem>(IngestCommandNormalizer.Normalize(
            "mikan",
            new IngestItemCommand(
                "https://mikanani.me/Download/mixed.torrent",
                new IngestItemInfo(
                    "TV 与 Movie 合集", null, "mixed-item", "3951",
                    "https://mikanani.me/Home/Episode/0123456789abcdef0123456789abcdef01234567",
                    null, 3951, 547888, null, null, 583, MediaTypes.Tv))).Item);
        var tasks = app.App.Services.GetRequiredService<IngestTaskStore>();
        var task = await tasks.AddStagedAsync(
            normalized,
            profile,
            new TorrentMetadata("TV 与 Movie 合集", new string('b', 40), 5, [new TorrentFile(fileName, 5, false)]),
            "mixed.torrent",
            DateTimeOffset.UtcNow.AddMinutes(10));
        var database = app.App.Services.GetRequiredService<AnimeGoSqliteDatabase>();
        await using (var addExtra = await database.OpenConnectionAsync())
        await using (var insertExtra = addExtra.CreateCommand())
        {
            insertExtra.CommandText = """
                INSERT INTO task_files (id, task_id, relative_path, size_bytes, disposition,
                                        download_file_index, download_priority, download_wanted)
                VALUES ('mixed-extra', $task_id, $path, 4, 'other', 1, 1, 1);
                """;
            insertExtra.Parameters.AddWithValue("$task_id", task.Id);
            insertExtra.Parameters.AddWithValue("$path", extraFileName);
            Assert.Equal(1, await insertExtra.ExecuteNonQueryAsync());
        }
        var dispatch = Assert.IsType<ClaimedStagedTorrentRecord>(await tasks.TryClaimNextStagedAsync(
            DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(1)));
        await tasks.CompleteDispatchAsync(
            dispatch,
            new DownloadTaskSnapshot(new string('b', 40), "TV 与 Movie 合集", DownloadTaskState.Complete, 1, 5, 5, 0, null),
            Path.Combine(paths.DownloadPath, "bt"),
            paths.SavePath,
            DateTimeOffset.UtcNow);

        var source = Path.Combine(paths.SavePath, "Series", "S01", "Extras", fileName);
        var extraSource = Path.Combine(paths.SavePath, "Series", "S01", "Extras", extraFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        await File.WriteAllBytesAsync(source, [1, 2, 3, 4, 5]);
        await File.WriteAllBytesAsync(extraSource, [6, 7, 8, 9]);
        await using (var connection = await database.OpenConnectionAsync())
        await using (var setup = connection.CreateCommand())
        {
            setup.CommandText = """
                INSERT INTO anime_series (
                    id, tmdb_series_id, canonical_name, original_name,
                    needs_tmdb_completion, created_at_utc, updated_at_utc)
                VALUES ('mixed-series', 65942, 'Series', 'Series', 0, $now, $now);
                UPDATE task_files
                SET disposition = 'other', other_reason = 'episode_not_parsed',
                    tmdb_series_id = 65942, tmdb_season_number = 1,
                    download_wanted = 1
                WHERE task_id = $task_id;
                UPDATE download_jobs
                SET preparation_state = 'completed', organization_state = 'completed',
                    organization_phase = 'completed', organization_total_units = 1,
                    organization_completed_units = 1, state = 'complete', progress = 1
                WHERE task_id = $task_id;
                UPDATE ingest_tasks SET status = 'organized' WHERE id = $task_id;
                INSERT INTO file_operations (
                    id, task_file_id, strategy, source_path, target_path, state,
                    bytes_verified, failure_reason, created_at_utc, updated_at_utc)
                SELECT 'mixed-operation-' || id, id, 'move',
                       CASE WHEN relative_path = $file_name THEN $source ELSE $extra_source END,
                       CASE WHEN relative_path = $file_name THEN $source ELSE $extra_source END,
                       'completed', CASE WHEN relative_path = $file_name THEN 5 ELSE 4 END,
                       NULL, $now, $now
                FROM task_files WHERE task_id = $task_id;
                """;
            setup.Parameters.AddWithValue("$task_id", task.Id);
            setup.Parameters.AddWithValue("$source", source);
            setup.Parameters.AddWithValue("$extra_source", extraSource);
            setup.Parameters.AddWithValue("$file_name", fileName);
            setup.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            Assert.Equal(7, await setup.ExecuteNonQueryAsync());
        }

        using var preview = await app.Client.GetAsync(
            $"/api/v1/metadata/tasks/{task.Id}/mixed-media-postprocess/preview");
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        using var previewJson = JsonDocument.Parse(await preview.Content.ReadAsStreamAsync());
        Assert.True(previewJson.RootElement.GetProperty("eligible").GetBoolean());
        var files = previewJson.RootElement.GetProperty("files");
        Assert.Equal(2, files.GetArrayLength());
        var file = files.EnumerateArray().Single(item => item.GetProperty("source_name").GetString() == fileName);
        var extra = files.EnumerateArray().Single(item => item.GetProperty("source_name").GetString() == extraFileName);
        Assert.False(file.GetProperty("movie_hint").GetBoolean());
        var taskFileId = file.GetProperty("task_file_id").GetString()!;
        Assert.True(extra.GetProperty("movie_hint").GetBoolean());
        var extraTaskFileId = extra.GetProperty("task_file_id").GetString()!;

        using var search = await app.Client.GetAsync(
            "/api/v1/tmdb/movies/search?query=Spirited%20Away");
        Assert.Equal(HttpStatusCode.OK, search.StatusCode);
        using var searchJson = JsonDocument.Parse(await search.Content.ReadAsStreamAsync());
        Assert.Equal(129, searchJson.RootElement.GetProperty("items")[0]
            .GetProperty("tmdb_movie_id").GetInt32());

        using var invalidRoles = await app.Client.PostAsync(
            $"/api/v1/metadata/tasks/{task.Id}/mixed-media-postprocess",
            new StringContent(
                $$"""{"movie_task_file_id":"{{taskFileId}}","movie_extra_task_file_ids":["{{taskFileId}}"],"tmdb_movie_id":129}""",
                Encoding.UTF8,
                "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, invalidRoles.StatusCode);

        using var start = await app.Client.PostAsync(
            $"/api/v1/metadata/tasks/{task.Id}/mixed-media-postprocess",
            new StringContent(
                $$"""{"movie_task_file_id":"{{taskFileId}}","movie_extra_task_file_ids":["{{extraTaskFileId}}"],"tmdb_movie_id":129}""",
                Encoding.UTF8,
                "application/json"));
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);

        using var pendingPreview = await app.Client.GetAsync(
            $"/api/v1/metadata/tasks/{task.Id}/mixed-media-postprocess/preview");
        Assert.Equal(HttpStatusCode.OK, pendingPreview.StatusCode);
        using var pendingJson = JsonDocument.Parse(await pendingPreview.Content.ReadAsStreamAsync());
        Assert.True(pendingJson.RootElement.GetProperty("eligible").GetBoolean());
        Assert.Equal("edit_pending", pendingJson.RootElement.GetProperty("mode").GetString());
        Assert.Equal(129, pendingJson.RootElement.GetProperty("current_movie")
            .GetProperty("tmdb_movie_id").GetInt32());
        var pendingFiles = pendingJson.RootElement.GetProperty("files");
        Assert.Equal("movie", pendingFiles.EnumerateArray()
            .Single(item => item.GetProperty("task_file_id").GetString() == taskFileId)
            .GetProperty("movie_role").GetString());
        Assert.Equal("extras", pendingFiles.EnumerateArray()
            .Single(item => item.GetProperty("task_file_id").GetString() == extraTaskFileId)
            .GetProperty("movie_role").GetString());

        using var revise = await app.Client.PostAsync(
            $"/api/v1/metadata/tasks/{task.Id}/mixed-media-postprocess",
            new StringContent(
                $$"""{"movie_task_file_id":"{{extraTaskFileId}}","movie_extra_task_file_ids":["{{taskFileId}}"],"tmdb_movie_id":129}""",
                Encoding.UTF8,
                "application/json"));
        Assert.Equal(HttpStatusCode.OK, revise.StatusCode);
        Assert.Equal(
            MediaOrganizationResult.FilesCompleted,
            await app.App.Services.GetRequiredService<MediaOrganizationProcessor>().RunOnceAsync());

        var targetDirectory = Path.Combine(paths.EffectiveMovieSavePath, "千与千寻 (2001)");
        Assert.False(File.Exists(source));
        Assert.False(File.Exists(extraSource));
        Assert.Equal(
            [6, 7, 8, 9],
            await File.ReadAllBytesAsync(Path.Combine(targetDirectory, "千与千寻 (2001).mkv")));
        Assert.Equal(
            [1, 2, 3, 4, 5],
            await File.ReadAllBytesAsync(Path.Combine(targetDirectory, fileName)));
        Assert.True(File.Exists(Path.Combine(targetDirectory, "movie.nfo")));
        await using var verify = await database.OpenConnectionAsync();
        await using var query = verify.CreateCommand();
        query.CommandText = """
            SELECT task.status, file.disposition, file.tmdb_movie_id,
                   (SELECT COUNT(*) FROM movie_completion_records WHERE tmdb_movie_id = 129)
            FROM ingest_tasks AS task
            JOIN task_files AS file ON file.task_id = task.id
            WHERE task.id = $task_id;
            """;
        query.Parameters.AddWithValue("$task_id", task.Id);
        await using var reader = await query.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("organized", reader.GetString(0));
        Assert.Equal("movie", reader.GetString(1));
        Assert.Equal(129, reader.GetInt32(2));
        Assert.Equal(1, reader.GetInt32(3));
    }

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
                    WHERE task_id = $task_id AND disposition = 'movie'
                      AND associated_task_file_id IS NULL
                      AND other_reason IS NULL),
                   (SELECT COUNT(*) FROM task_files
                    WHERE task_id = $task_id AND disposition = 'movie'
                      AND associated_task_file_id IS NOT NULL
                      AND other_reason IS NULL),
                   (SELECT COUNT(*) FROM task_files
                    WHERE task_id = $task_id AND disposition = 'other')
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
        Assert.Equal(0, reader.GetInt32(8));
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

    [Fact]
    public async Task VerifiedMovieMovesToMovieLibraryAndCreatesMovieCompletion()
    {
        var tmdb = new FakeTmdbClient();
        var downloadClient = new FakeDownloadClient();
        await using var app = await RunningApp.StartAsync(
            tmdbClient: tmdb,
            bangumiSubjectClient: new FakeBangumiClient(),
            downloadClientRegistry: new FakeDownloadRegistry(downloadClient));
        var taskId = await SeedStagedMovieAsync(app, "Spirited Away", includeSubtitle: true);
        var store = app.App.Services.GetRequiredService<IngestTaskStore>();
        var dispatch = Assert.IsType<ClaimedStagedTorrentRecord>(await store.TryClaimNextStagedAsync(
            DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(1)));
        var paths = AnimeGoDefaults.CreateNative(app.RootPath).Paths;
        var downloadRoot = Path.Combine(paths.DownloadPath, "bt");
        await store.CompleteDispatchAsync(
            dispatch,
            new DownloadTaskSnapshot(
                new string('a', 40), "Spirited Away", DownloadTaskState.Complete,
                1, 1_100, 1_100, 0, null),
            downloadRoot,
            paths.EffectiveMovieSavePath,
            DateTimeOffset.UtcNow);

        Assert.True(await app.App.Services
            .GetRequiredService<AutomaticMetadataResolutionProcessor>()
            .RunOnceAsync());
        Directory.CreateDirectory(downloadRoot);
        await File.WriteAllBytesAsync(Path.Combine(downloadRoot, "movie.mkv"), new byte[1_000]);
        await File.WriteAllBytesAsync(Path.Combine(downloadRoot, "movie.zh-CN.ass"), new byte[100]);
        var database = app.App.Services.GetRequiredService<AnimeGoSqliteDatabase>();
        await using (var connection = await database.OpenConnectionAsync())
        await using (var update = connection.CreateCommand())
        {
            update.CommandText = """
                UPDATE task_files SET download_wanted = 1 WHERE task_id = $task_id;
                UPDATE download_jobs
                SET preparation_state = 'completed', state = 'complete', progress = 1,
                    organization_state = 'pending', seeding_state = 'not_required'
                WHERE task_id = $task_id;
                UPDATE ingest_tasks SET status = 'downloaded' WHERE id = $task_id;
                """;
            update.Parameters.AddWithValue("$task_id", taskId);
            Assert.Equal(4, await update.ExecuteNonQueryAsync());
        }

        Assert.Equal(
            MediaOrganizationResult.FilesCompleted,
            await app.App.Services.GetRequiredService<MediaOrganizationProcessor>().RunOnceAsync());

        var targetDirectory = Path.Combine(paths.EffectiveMovieSavePath, "千与千寻 (2001)");
        Assert.True(File.Exists(Path.Combine(targetDirectory, "千与千寻 (2001).mkv")));
        Assert.True(File.Exists(Path.Combine(targetDirectory, "千与千寻 (2001).zh-CN.ass")));
        Assert.True(File.Exists(Path.Combine(targetDirectory, "movie.nfo")));
        await using var verify = await database.OpenConnectionAsync();
        await using var query = verify.CreateCommand();
        query.CommandText = """
            SELECT COUNT(*), MIN(tmdb_movie_id), MIN(media_path)
            FROM movie_completion_records;
            """;
        await using var reader = await query.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1, reader.GetInt32(0));
        Assert.Equal(129, reader.GetInt32(1));
        Assert.Equal(Path.Combine(targetDirectory, "千与千寻 (2001).mkv"), reader.GetString(2));
        Assert.Equal(1, downloadClient.PauseCalls);
    }

    private static async Task<string> SeedMovieAsync(
        RunningApp app,
        string title,
        bool includeSubtitle = false,
        bool secondVideo = false)
    {
        var taskId = await SeedStagedMovieAsync(app, title, includeSubtitle, secondVideo);
        var database = app.App.Services.GetRequiredService<AnimeGoSqliteDatabase>();
        await using var connection = await database.OpenConnectionAsync();
        await using var update = connection.CreateCommand();
        update.CommandText = "UPDATE ingest_tasks SET status = 'download_preparing' WHERE id = $task_id;";
        update.Parameters.AddWithValue("$task_id", taskId);
        Assert.Equal(1, await update.ExecuteNonQueryAsync());
        return taskId;
    }

    private static async Task<string> SeedStagedMovieAsync(
        RunningApp app,
        string title,
        bool includeSubtitle = false,
        bool secondVideo = false)
    {
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

    private sealed class FakeDownloadRegistry(IDownloadClient client) : IDownloadClientRegistry
    {
        public IReadOnlyCollection<string> InstanceIds => ["bt"];

        public IDownloadClient GetRequired(string instanceId) =>
            instanceId == "bt" ? client : throw new KeyNotFoundException(instanceId);
    }

    private sealed class FakeDownloadClient : IDownloadClient
    {
        public int PauseCalls { get; private set; }

        public Task ConnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<DownloadTaskSnapshot>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DownloadTaskSnapshot>>([]);

        public Task AddTorrentAsync(AddTorrentCommand command, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<DownloadFileSnapshot>> ListFilesAsync(
            string hash,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DownloadFileSnapshot>>([]);

        public Task SetFilePriorityAsync(
            string hash,
            IReadOnlyList<int> fileIndexes,
            int priority,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task AddTagsAsync(
            IReadOnlyList<string> hashes,
            IReadOnlyList<string> tags,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task PauseAsync(
            IReadOnlyList<string> hashes,
            CancellationToken cancellationToken = default)
        {
            PauseCalls++;
            return Task.CompletedTask;
        }

        public Task ResumeAsync(
            IReadOnlyList<string> hashes,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeleteAsync(
            IReadOnlyList<string> hashes,
            bool deleteFiles,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
