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
        var tmdb = new FakeTmdbClient
        {
            TvSearchResults =
            [
                new TmdbSeries(
                    35544,
                    "机动战舰抚子号",
                    "機動戦艦ナデシコ",
                    new DateOnly(1996, 10, 1),
                    "/nadesico.jpg"),
            ],
        };
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

        using var anitomy = await app.Client.PostAsync(
            "/api/v1/metadata/anitomy/parse-title",
            new StringContent(
                """{"source_text":"[Group] Kidou Senkan Nadesico The Movie [1080p].mkv"}""",
                Encoding.UTF8,
                "application/json"));
        Assert.Equal(HttpStatusCode.OK, anitomy.StatusCode);
        using var anitomyJson = JsonDocument.Parse(await anitomy.Content.ReadAsStreamAsync());
        Assert.True(anitomyJson.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(
            "Kidou Senkan Nadesico The Movie",
            anitomyJson.RootElement.GetProperty("anime_title").GetString());
        Assert.True(anitomyJson.RootElement.GetProperty("match_start").GetInt32() >= 0);

        using var search = await app.Client.GetAsync(
            "/api/v1/tmdb/movies/search?query=Spirited%20Away");
        Assert.Equal(HttpStatusCode.OK, search.StatusCode);
        using var searchJson = JsonDocument.Parse(await search.Content.ReadAsStreamAsync());
        Assert.Equal(129, searchJson.RootElement.GetProperty("items")[0]
            .GetProperty("tmdb_movie_id").GetInt32());

        using var tvSearch = await app.Client.GetAsync(
            "/api/v1/tmdb/tv/search?query=Nadesico");
        Assert.Equal(HttpStatusCode.OK, tvSearch.StatusCode);
        using var tvSearchJson = JsonDocument.Parse(await tvSearch.Content.ReadAsStreamAsync());
        Assert.Equal(35544, tvSearchJson.RootElement.GetProperty("items")[0]
            .GetProperty("tmdb_series_id").GetInt32());

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
            await File.ReadAllBytesAsync(Path.Combine(targetDirectory, "Extras", fileName)));
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
    public async Task OrganizedMovieCollectionCanSplitExtraVideoIntoSecondMovie()
    {
        var tmdb = new FakeTmdbClient();
        await using var app = await RunningApp.StartAsync(tmdbClient: tmdb);
        var taskId = await SeedStagedMovieAsync(app, "Movie x2 Collection", secondVideo: true);
        var tasks = app.App.Services.GetRequiredService<IngestTaskStore>();
        var dispatch = Assert.IsType<ClaimedStagedTorrentRecord>(await tasks.TryClaimNextStagedAsync(
            DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(1)));
        var paths = AnimeGoDefaults.CreateNative(app.RootPath).Paths;
        await tasks.CompleteDispatchAsync(
            dispatch,
            new DownloadTaskSnapshot(
                new string('a', 40), "Movie x2 Collection", DownloadTaskState.Complete,
                1, 1_500, 1_500, 0, null),
            Path.Combine(paths.DownloadPath, "bt"),
            paths.EffectiveMovieSavePath,
            DateTimeOffset.UtcNow);

        var originalDirectory = Path.Combine(paths.EffectiveMovieSavePath, "Original Movie (2001)");
        var originalMain = Path.Combine(originalDirectory, "Original Movie (2001).mkv");
        var secondMovieSource = Path.Combine(originalDirectory, "Extras", "bonus.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(secondMovieSource)!);
        await File.WriteAllBytesAsync(originalMain, new byte[1_000]);
        await File.WriteAllBytesAsync(secondMovieSource, new byte[500]);

        var database = app.App.Services.GetRequiredService<AnimeGoSqliteDatabase>();
        await using (var connection = await database.OpenConnectionAsync())
        await using (var setup = connection.CreateCommand())
        {
            setup.CommandText = """
                INSERT INTO anime_movies (
                    id, tmdb_movie_id, canonical_title, original_title,
                    release_date, poster_path, created_at_utc, updated_at_utc)
                VALUES ('original-movie', 129, 'Original Movie', 'Original Movie',
                        '2001-07-20', NULL, $now, $now);
                UPDATE task_files
                SET disposition = 'movie', tmdb_movie_id = 129,
                    associated_task_file_id = NULL, other_reason = NULL,
                    download_wanted = 1
                WHERE task_id = $task_id AND relative_path = 'movie.mkv';
                UPDATE task_files
                SET disposition = 'extras', tmdb_movie_id = 129,
                    associated_task_file_id = (
                        SELECT id FROM task_files
                        WHERE task_id = $task_id AND relative_path = 'movie.mkv'),
                    other_reason = 'movie_video_extra', download_wanted = 1
                WHERE task_id = $task_id AND relative_path = 'bonus.mp4';
                INSERT INTO movie_claims (
                    id, tmdb_movie_id, task_file_id, state, claimed_at_utc, expires_at_utc)
                SELECT 'original-claim', 129, id, 'completed', $now, NULL
                FROM task_files WHERE task_id = $task_id AND relative_path = 'movie.mkv';
                INSERT INTO movie_completion_records (
                    id, tmdb_movie_id, source_id, source_item_id, media_path, completed_at_utc)
                VALUES ('original-completion', 129, 'mikan', 'movie-item', $main_path, $now);
                INSERT INTO file_operations (
                    id, task_file_id, strategy, source_path, target_path, state,
                    bytes_verified, failure_reason, created_at_utc, updated_at_utc)
                SELECT 'movie-operation-' || id, id, 'move',
                       CASE relative_path WHEN 'movie.mkv' THEN $main_path ELSE $extra_path END,
                       CASE relative_path WHEN 'movie.mkv' THEN $main_path ELSE $extra_path END,
                       'completed', size_bytes, NULL, $now, $now
                FROM task_files WHERE task_id = $task_id;
                UPDATE download_jobs
                SET preparation_state = 'completed', state = 'complete', progress = 1,
                    organization_state = 'completed', organization_phase = 'completed',
                    organization_total_units = 1, organization_completed_units = 1,
                    seeding_state = 'not_required'
                WHERE task_id = $task_id;
                UPDATE ingest_tasks SET status = 'organized' WHERE id = $task_id;
                """;
            setup.Parameters.AddWithValue("$task_id", taskId);
            setup.Parameters.AddWithValue("$main_path", originalMain);
            setup.Parameters.AddWithValue("$extra_path", secondMovieSource);
            setup.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            Assert.Equal(9, await setup.ExecuteNonQueryAsync());
        }

        using var preview = await app.Client.GetAsync(
            $"/api/v1/metadata/tasks/{taskId}/mixed-media-postprocess/preview");
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        using var previewJson = JsonDocument.Parse(await preview.Content.ReadAsStreamAsync());
        Assert.True(previewJson.RootElement.GetProperty("eligible").GetBoolean());
        Assert.Equal("movie", previewJson.RootElement.GetProperty("media_type").GetString());
        var files = previewJson.RootElement.GetProperty("files").EnumerateArray().ToArray();
        var main = Assert.Single(files, file => file.GetProperty("movie_role").GetString() == "movie");
        var extra = Assert.Single(files, file => file.GetProperty("movie_role").GetString() == "extras");
        Assert.Equal("movie.mkv", main.GetProperty("source_name").GetString());

        using var moveCurrentMain = await app.Client.PostAsync(
            $"/api/v1/metadata/tasks/{taskId}/mixed-media-postprocess",
            new StringContent(
                $$"""{"movie_task_file_id":"{{main.GetProperty("task_file_id").GetString()}}","movie_extra_task_file_ids":[],"tmdb_movie_id":200}""",
                Encoding.UTF8,
                "application/json"));
        Assert.Equal(HttpStatusCode.Conflict, moveCurrentMain.StatusCode);

        using var reuseCurrentMovie = await app.Client.PostAsync(
            $"/api/v1/metadata/tasks/{taskId}/mixed-media-postprocess",
            new StringContent(
                $$"""{"movie_task_file_id":"{{extra.GetProperty("task_file_id").GetString()}}","movie_extra_task_file_ids":[],"tmdb_movie_id":129}""",
                Encoding.UTF8,
                "application/json"));
        Assert.Equal(HttpStatusCode.Conflict, reuseCurrentMovie.StatusCode);

        using var start = await app.Client.PostAsync(
            $"/api/v1/metadata/tasks/{taskId}/mixed-media-postprocess",
            new StringContent(
                $$"""{"movie_task_file_id":"{{extra.GetProperty("task_file_id").GetString()}}","movie_extra_task_file_ids":[],"tmdb_movie_id":200}""",
                Encoding.UTF8,
                "application/json"));
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);

        await using (var verify = await database.OpenConnectionAsync())
        await using (var query = verify.CreateCommand())
        {
            query.CommandText = """
                SELECT relative_path, tmdb_movie_id, associated_task_file_id
                FROM task_files WHERE task_id = $task_id ORDER BY relative_path;
                """;
            query.Parameters.AddWithValue("$task_id", taskId);
            await using var reader = await query.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("bonus.mp4", reader.GetString(0));
            Assert.Equal(200, reader.GetInt32(1));
            Assert.True(reader.IsDBNull(2));
            Assert.True(await reader.ReadAsync());
            Assert.Equal("movie.mkv", reader.GetString(0));
            Assert.Equal(129, reader.GetInt32(1));
            Assert.True(reader.IsDBNull(2));
            Assert.False(await reader.ReadAsync());
        }
        Assert.True(File.Exists(originalMain));
        Assert.True(File.Exists(secondMovieSource));
        Assert.Contains(200, tmdb.MovieDetailRequests);

        Assert.Equal(
            MediaOrganizationResult.FilesCompleted,
            await app.App.Services.GetRequiredService<MediaOrganizationProcessor>().RunOnceAsync());
        var secondMovieTarget = Path.Combine(
            paths.EffectiveMovieSavePath,
            "第二部电影 (2002)",
            "第二部电影 (2002).mp4");
        Assert.True(File.Exists(originalMain));
        Assert.False(File.Exists(secondMovieSource));
        Assert.True(File.Exists(secondMovieTarget));
        Assert.Equal(new byte[500], await File.ReadAllBytesAsync(secondMovieTarget));

        await using var completed = await database.OpenConnectionAsync();
        await using var completedQuery = completed.CreateCommand();
        completedQuery.CommandText = """
            SELECT COUNT(*), COUNT(DISTINCT tmdb_movie_id)
            FROM movie_completion_records WHERE tmdb_movie_id IN (129, 200);
            """;
        await using var completedReader = await completedQuery.ExecuteReaderAsync();
        Assert.True(await completedReader.ReadAsync());
        Assert.Equal(2, completedReader.GetInt32(0));
        Assert.Equal(2, completedReader.GetInt32(1));
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
                     WHERE task_id = $task_id AND disposition = 'extras'
                       AND associated_task_file_id IS NOT NULL
                       AND other_reason = 'movie_subtitle_extra'),
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
    public async Task U2MovieWithClearlyLargestVideoCompletesMainAndExtras()
    {
        var tmdb = new FakeTmdbClient();
        await using var app = await RunningApp.StartAsync(
            configure: options => options with
            {
                InitialSourceProfiles =
                [
                    .. options.InitialSourceProfiles,
                    new SourceProfileSeed
                    {
                        Id = "u2",
                        DisplayName = "U2",
                        Adapter = "u2",
                        MediaType = MediaTypes.Movie,
                        DownloaderId = "bt",
                        FileStrategy = FileStrategy.Link,
                        AllowedTorrentHosts = ["u2.dmhy.org"],
                    },
                ],
            },
            tmdbClient: tmdb,
            bangumiSubjectClient: new FakeBangumiClient());
        var taskId = await SeedU2MovieAsync(
            app,
            "[Group] Spirited Away [BDRip 1080p]",
            [
                new TorrentFile("Spirited Away.mkv", 8L * 1024 * 1024 * 1024, false),
                new TorrentFile("Trailer.mkv", 200L * 1024 * 1024, false),
                new TorrentFile("Booklet.pdf", 50L * 1024 * 1024, false),
            ]);

        Assert.True(await app.App.Services
            .GetRequiredService<AutomaticMetadataResolutionProcessor>()
            .RunOnceAsync());

        await using var connection = await app.App.Services
            .GetRequiredService<AnimeGoSqliteDatabase>()
            .OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT task.status, file.relative_path, file.disposition,
                   file.tmdb_movie_id, file.associated_task_file_id,
                   main.relative_path
            FROM ingest_tasks AS task
            JOIN task_files AS file ON file.task_id = task.id
            LEFT JOIN task_files AS main ON main.id = file.associated_task_file_id
            WHERE task.id = $task_id
            ORDER BY file.relative_path;
            """;
        command.Parameters.AddWithValue("$task_id", taskId);
        var rows = new List<(string Status, string Path, string Disposition, int MovieId, string? MainPath)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add((
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        Assert.Equal(3, rows.Count);
        Assert.All(rows, row =>
        {
            Assert.Equal("metadata_resolved", row.Status);
            Assert.Equal(129, row.MovieId);
        });
        var main = Assert.Single(rows, row => row.Disposition == "movie");
        Assert.Equal("Spirited Away.mkv", main.Path);
        Assert.Null(main.MainPath);
        var extras = rows.Where(row => row.Disposition == "extras").ToArray();
        Assert.Equal(2, extras.Length);
        Assert.All(extras, extra => Assert.Equal("Spirited Away.mkv", extra.MainPath));
        Assert.Equal(["Spirited Away"], tmdb.MovieSearches);
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
        Assert.True(File.Exists(Path.Combine(targetDirectory, "Extras", "movie.zh-CN.ass")));
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

    private static async Task<string> SeedU2MovieAsync(
        RunningApp app,
        string title,
        IReadOnlyList<TorrentFile> files)
    {
        var profiles = app.App.Services.GetRequiredService<SourceProfileStore>();
        var profile = Assert.IsType<SourceProfileRecord>(await profiles.GetEnabledAsync("u2"));
        var normalization = IngestCommandNormalizer.Normalize(
            "u2",
            new IngestItemCommand(
                "https://u2.dmhy.org/download.php?id=65893&passkey=test&https=1",
                new IngestItemInfo(
                    title,
                    null,
                    "65893",
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    MediaTypes.Movie)));
        Assert.True(normalization.IsValid, string.Join(", ", normalization.Errors));
        var task = await app.App.Services.GetRequiredService<IngestTaskStore>().AddStagedAsync(
            normalization.Item!,
            profile,
            new TorrentMetadata(title, new string('c', 40), files.Sum(file => file.Size), files),
            $"u2-movie-{Guid.NewGuid():N}.torrent",
            DateTimeOffset.UtcNow.AddMinutes(10));
        var database = app.App.Services.GetRequiredService<AnimeGoSqliteDatabase>();
        await using var connection = await database.OpenConnectionAsync();
        await using var update = connection.CreateCommand();
        update.CommandText = "UPDATE ingest_tasks SET status = 'download_preparing' WHERE id = $task_id;";
        update.Parameters.AddWithValue("$task_id", task.Id);
        Assert.Equal(1, await update.ExecuteNonQueryAsync());
        return task.Id;
    }

    private sealed class FakeTmdbClient : ITmdbClient, ITmdbMovieClient
    {
        public IReadOnlyList<TmdbSeries> TvSearchResults { get; init; } = [];

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
                movieId == 200
                    ? new TmdbMovie(200, "第二部电影", "Second Movie", new DateOnly(2002, 1, 1), "/second.jpg")
                    : new TmdbMovie(movieId, "千与千寻", "Spirited Away", new DateOnly(2001, 7, 20), "/movie.jpg"));
        }

        public Task<IReadOnlyList<TmdbSeries>> SearchSeriesAsync(
            string title,
            CancellationToken cancellationToken = default)
        {
            TvSearches.Add(title);
            return Task.FromResult(TvSearchResults);
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
