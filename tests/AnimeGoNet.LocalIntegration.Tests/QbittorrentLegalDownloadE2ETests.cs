using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AnimeGoNet.App;
using AnimeGoNet.App.Downloads;
using AnimeGoNet.App.Ingest;
using AnimeGoNet.App.Library;
using AnimeGoNet.App.Torrents;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Downloads;
using AnimeGoNet.Core.Ingest;
using AnimeGoNet.Core.Torrents;
using AnimeGoNet.Data.Sqlite;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeGoNet.LocalIntegration.Tests;

public sealed class QbittorrentLegalDownloadE2ETests
{
    private const int PayloadLength = 128 * 1024;
    private const int PieceLength = 16 * 1024;

    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task LegalLoopbackFileCompletesMoveStateMachineAndExactCleanup()
    {
        Assert.Equal("1", Required("ANIMEGONET_QBIT_INTEGRATION"));
        Assert.Equal("1", Required("ANIMEGONET_QBIT_DOWNLOAD_FIXTURE"));

        string sandbox = Path.GetFullPath(Required("ANIMEGONET_QBIT_SANDBOX"));
        string downloadPath = Path.GetFullPath(Required("ANIMEGONET_QBIT_DOWNLOAD_PATH"));
        string savePath = Path.GetFullPath(Required("ANIMEGONET_QBIT_SAVE_PATH"));
        string integrationDataPath = Path.GetFullPath(Required("ANIMEGONET_QBIT_DATA_PATH"));
        var baseUrl = new Uri(Required("ANIMEGONET_QBIT_BASE_URL"));
        string runId = Guid.NewGuid().ToString("N");
        string shortRunId = runId[..12];
        string fileName = $"animegonet-legal-{runId}.mkv";
        string seriesName = $"AnimeGoNet Legal Fixture {shortRunId}";
        string category = $"animegonet-integration-{runId}";
        string tag = $"animegonet-test-{runId}";
        string dataPath = Path.Combine(
            integrationDataPath,
            "integration",
            $"qbit-legal-download-{runId}");
        string payloadPath = Path.Combine(downloadPath, fileName);
        string incompletePayloadPath = string.Concat(payloadPath, ".!qB");
        string seriesPath = Path.Combine(savePath, seriesName);
        string targetPath = Path.Combine(seriesPath, "S01", "E001.mkv");
        byte[] payload = CreatePayload();

        AssertWithin(sandbox, downloadPath);
        AssertWithin(sandbox, savePath);
        AssertWithin(sandbox, dataPath);
        AssertWithin(downloadPath, payloadPath);
        AssertWithin(savePath, seriesPath);
        Assert.False(File.Exists(payloadPath));
        Assert.False(File.Exists(incompletePayloadPath));
        Assert.False(Directory.Exists(seriesPath));

        await using var fileServer = new LoopbackFileServer(fileName, payload);
        byte[] torrentBytes = BuildTorrent(fileName, payload, fileServer.FileUrl);
        TorrentMetadata metadata = TorrentMetainfoParser.Parse(torrentBytes);
        Assert.Equal(payload.LongLength, metadata.TotalSize);
        Assert.Equal(fileName, Assert.Single(metadata.Files).RelativePath);

        var downloader = new QbittorrentInstanceOptions
        {
            BaseUrl = baseUrl,
            Username = Required("ANIMEGONET_QBIT_USERNAME"),
            Password = Required("ANIMEGONET_QBIT_PASSWORD"),
            DownloadPath = downloadPath,
        };
        using var adminHttp = new HttpClient(new HttpClientHandler { UseCookies = true })
        {
            BaseAddress = baseUrl,
            Timeout = TimeSpan.FromSeconds(15),
        };
        adminHttp.DefaultRequestHeaders.Referrer = baseUrl;
        var admin = new QbittorrentClient(adminHttp, downloader);
        await admin.ConnectAsync();
        Assert.Empty(await admin.ListAsync());

        await PostFormAsync(
            adminHttp,
            "api/v2/torrents/createCategory",
            new Dictionary<string, string>
            {
                ["category"] = category,
                ["savePath"] = string.Empty,
            });
        await PostFormAsync(
            adminHttp,
            "api/v2/torrents/createTags",
            new Dictionary<string, string> { ["tags"] = tag });

        AnimeGoOptions options = CreateOptions(
            dataPath,
            downloadPath,
            savePath,
            downloader,
            category,
            tag);
        DirectoryLayout layout = DirectoryLayout.From(options.Paths);
        var staging = new GeneratedTorrentStagingService(
            layout.StagingPath,
            torrentBytes,
            metadata);
        using var registry = new QbittorrentClientRegistry(options);
        WebApplication? app = null;
        string? taskId = null;
        try
        {
            app = await AnimeGoApplication.BuildAsync(
                [],
                options,
                runningInContainer: false,
                torrentStagingService: staging,
                downloadClientRegistry: registry,
                startBackgroundWorkers: false);

            var processor = app.Services.GetRequiredService<UnifiedIngestProcessor>();
            var result = await processor.ProcessAsync(
                "mikan",
                new IngestItemCommand(
                    "https://fixture.invalid/animegonet-legal-download.torrent?token=local-only",
                    new IngestItemInfo(
                        $"{seriesName} S01E01",
                        null,
                        $"legal-{runId}",
                        "3951",
                        "https://mikanani.me/Home/Bangumi/3951",
                        null,
                        3951,
                        547888,
                        null,
                        null)),
                requireModernMetadata: true);

            Assert.True(result.Accepted, string.Join("; ", result.Errors));
            Assert.Equal("staged", result.Status);
            Assert.Equal(metadata.InfoHash, result.InfoHash);
            Assert.Equal("bt", result.DownloaderId);
            taskId = Assert.IsType<string>(result.IngestId);

            Assert.Equal(
                StagedDispatchResult.Completed,
                await app.Services.GetRequiredService<StagedTorrentDispatcher>()
                    .DispatchNextAsync());
            DownloadTaskSnapshot paused = await WaitForTaskAsync(
                admin,
                metadata.InfoHash,
                static task => task.State == DownloadTaskState.Paused,
                TimeSpan.FromSeconds(10));
            Assert.Equal(PayloadLength, paused.TotalBytes);
            Assert.False(File.Exists(payloadPath));

            var database = app.Services.GetRequiredService<AnimeGoSqliteDatabase>();
            await SeedVerifiedEpisodeAsync(
                database,
                taskId,
                seriesName,
                900001,
                90000101);

            Assert.Equal(
                DownloadPreparationResult.Completed,
                await app.Services.GetRequiredService<DownloadPreparationProcessor>()
                    .RunOnceAsync());
            DownloadFileSnapshot preparedFile = Assert.Single(
                await admin.ListFilesAsync(metadata.InfoHash));
            Assert.Equal(1, preparedFile.Priority);
            Assert.Equal(PayloadLength, preparedFile.SizeBytes);

            DownloadTaskSnapshot completed = await WaitForTaskAsync(
                admin,
                metadata.InfoHash,
                task => task.Progress >= 1
                    && File.Exists(payloadPath)
                    && new FileInfo(payloadPath).Length == PayloadLength,
                TimeSpan.FromSeconds(30),
                () => $"file_exists={File.Exists(payloadPath)}, " +
                    $"web_seed_requests={fileServer.RequestCount}, " +
                    $"file_priority={preparedFile.Priority}, file_progress={preparedFile.Progress}");
            Assert.True(
                completed.State is DownloadTaskState.Seeding or DownloadTaskState.Complete,
                $"Unexpected completed qB state: {completed.State}");
            Assert.True(fileServer.RequestCount > 0);
            Assert.Equal(payload, await File.ReadAllBytesAsync(payloadPath));

            await app.Services.GetRequiredService<DownloadSnapshotSynchronizer>()
                .SyncOnceAsync();
            Assert.Equal(
                ("downloaded", "completed"),
                await ReadDownloadStateAsync(database, taskId));

            var organizer = app.Services.GetRequiredService<MediaOrganizationProcessor>();
            Assert.Equal(
                MediaOrganizationResult.FilesCompleted,
                await RunOrganizationUntilFilesCompletedAsync(
                    organizer,
                    database,
                    taskId));
            Assert.False(File.Exists(payloadPath));
            Assert.Equal(payload, await File.ReadAllBytesAsync(targetPath));
            Assert.True(File.Exists(Path.Combine(seriesPath, "tvshow.nfo")));
            Assert.True(File.Exists(Path.Combine(seriesPath, "anime.a_json")));
            Assert.True(File.Exists(Path.Combine(seriesPath, "S01", "anime.s_json")));
            Assert.True(File.Exists(Path.Combine(seriesPath, "S01", "E001.e_json")));
            Assert.Equal(1, await CountCompletionsAsync(database));

            Assert.Equal(MediaOrganizationResult.CleanupCompleted, await organizer.RunOnceAsync());
            Assert.Equal(
                ("organized", "completed"),
                await ReadOrganizationStateAsync(database, taskId));
            await admin.ConnectAsync();
            Assert.DoesNotContain(
                await admin.ListAsync(),
                task => string.Equals(
                    task.Hash,
                    metadata.InfoHash,
                    StringComparison.OrdinalIgnoreCase));
            Assert.Equal(payload, await File.ReadAllBytesAsync(targetPath));
        }
        finally
        {
            if (app is not null)
            {
                await app.DisposeAsync();
            }

            await BestEffortDeleteTorrentAsync(admin, metadata.InfoHash);
            await BestEffortPostFormAsync(
                adminHttp,
                "api/v2/torrents/removeCategories",
                new Dictionary<string, string> { ["categories"] = category });
            await BestEffortPostFormAsync(
                adminHttp,
                "api/v2/torrents/deleteTags",
                new Dictionary<string, string> { ["tags"] = tag });

            await DeleteExactFileAsync(payloadPath);
            await DeleteExactFileAsync(incompletePayloadPath);
            await DeleteExactDirectoryAsync(seriesPath, savePath);
            await DeleteExactDirectoryAsync(dataPath, integrationDataPath);
        }

        await admin.ConnectAsync();
        Assert.DoesNotContain(
            await admin.ListAsync(),
            task => string.Equals(
                task.Hash,
                metadata.InfoHash,
                StringComparison.OrdinalIgnoreCase));
        using var categories = JsonDocument.Parse(
            await adminHttp.GetStringAsync("api/v2/torrents/categories"));
        Assert.False(categories.RootElement.TryGetProperty(category, out _));
        using var tags = JsonDocument.Parse(
            await adminHttp.GetStringAsync("api/v2/torrents/tags"));
        Assert.DoesNotContain(
            tags.RootElement.EnumerateArray(),
            value => string.Equals(value.GetString(), tag, StringComparison.Ordinal));
        Assert.False(File.Exists(payloadPath));
        Assert.False(File.Exists(incompletePayloadPath));
        Assert.False(Directory.Exists(seriesPath));
        Assert.False(Directory.Exists(dataPath));
    }

    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task LegalMultiFileDownloadAppliesPrioritiesAndMovesAssociatedSubtitle()
    {
        Assert.Equal("1", Required("ANIMEGONET_QBIT_INTEGRATION"));
        Assert.Equal("1", Required("ANIMEGONET_QBIT_DOWNLOAD_FIXTURE"));

        string sandbox = Path.GetFullPath(Required("ANIMEGONET_QBIT_SANDBOX"));
        string downloadPath = Path.GetFullPath(Required("ANIMEGONET_QBIT_DOWNLOAD_PATH"));
        string savePath = Path.GetFullPath(Required("ANIMEGONET_QBIT_SAVE_PATH"));
        string integrationDataPath = Path.GetFullPath(Required("ANIMEGONET_QBIT_DATA_PATH"));
        var baseUrl = new Uri(Required("ANIMEGONET_QBIT_BASE_URL"));
        string runId = Guid.NewGuid().ToString("N");
        string shortRunId = runId[..12];
        string torrentRootName = $"animegonet-legal-multi-{runId}";
        string seriesName = $"AnimeGoNet Legal Multi Fixture {shortRunId}";
        string category = $"animegonet-integration-{runId}";
        string tag = $"animegonet-test-{runId}";
        string dataPath = Path.Combine(
            integrationDataPath,
            "integration",
            $"qbit-legal-multi-download-{runId}");
        string torrentRootPath = Path.Combine(downloadPath, torrentRootName);
        string seriesPath = Path.Combine(savePath, seriesName);
        string seasonPath = Path.Combine(seriesPath, "S01");
        string targetVideoPath = Path.Combine(seasonPath, "E001.mkv");
        string targetSubtitlePath = Path.Combine(seasonPath, "E001.zh-Hans.forced.ass");
        LegalTorrentFile[] files =
        [
            new(["Episode 01.mkv"], CreatePayload(64 * 1024, 11)),
            new(["Episode 01.zh-Hans.forced.ass"], CreatePayload(16 * 1024, 23)),
            new(["Episode 02.mkv"], CreatePayload(32 * 1024, 37)),
            new(["poster.jpg"], CreatePayload(16 * 1024, 53)),
        ];

        AssertWithin(sandbox, downloadPath);
        AssertWithin(sandbox, savePath);
        AssertWithin(sandbox, dataPath);
        AssertWithin(downloadPath, torrentRootPath);
        AssertWithin(savePath, seriesPath);
        Assert.False(Directory.Exists(torrentRootPath));
        Assert.False(Directory.Exists(seriesPath));

        await using var fileServer = new LoopbackMultiFileServer(torrentRootName, files);
        byte[] torrentBytes = BuildMultiFileTorrent(
            torrentRootName,
            files,
            fileServer.BaseUrl);
        TorrentMetadata metadata = TorrentMetainfoParser.Parse(torrentBytes);
        Assert.Equal(files.Sum(file => file.Payload.LongLength), metadata.TotalSize);
        Assert.Equal(
            files.Select(file => $"{torrentRootName}/{string.Join('/', file.PathComponents)}"),
            metadata.Files.Select(file => file.RelativePath));

        var downloader = new QbittorrentInstanceOptions
        {
            BaseUrl = baseUrl,
            Username = Required("ANIMEGONET_QBIT_USERNAME"),
            Password = Required("ANIMEGONET_QBIT_PASSWORD"),
            DownloadPath = downloadPath,
        };
        using var adminHttp = new HttpClient(new HttpClientHandler { UseCookies = true })
        {
            BaseAddress = baseUrl,
            Timeout = TimeSpan.FromSeconds(15),
        };
        adminHttp.DefaultRequestHeaders.Referrer = baseUrl;
        var admin = new QbittorrentClient(adminHttp, downloader);
        await admin.ConnectAsync();
        Assert.Empty(await admin.ListAsync());

        await PostFormAsync(
            adminHttp,
            "api/v2/torrents/createCategory",
            new Dictionary<string, string>
            {
                ["category"] = category,
                ["savePath"] = string.Empty,
            });
        await PostFormAsync(
            adminHttp,
            "api/v2/torrents/createTags",
            new Dictionary<string, string> { ["tags"] = tag });

        AnimeGoOptions options = CreateOptions(
            dataPath,
            downloadPath,
            savePath,
            downloader,
            category,
            tag);
        DirectoryLayout layout = DirectoryLayout.From(options.Paths);
        var staging = new GeneratedTorrentStagingService(
            layout.StagingPath,
            torrentBytes,
            metadata);
        using var registry = new QbittorrentClientRegistry(options);
        WebApplication? app = null;
        string? taskId = null;
        try
        {
            app = await AnimeGoApplication.BuildAsync(
                [],
                options,
                runningInContainer: false,
                torrentStagingService: staging,
                downloadClientRegistry: registry,
                startBackgroundWorkers: false);

            var processor = app.Services.GetRequiredService<UnifiedIngestProcessor>();
            var result = await processor.ProcessAsync(
                "mikan",
                new IngestItemCommand(
                    "https://fixture.invalid/animegonet-legal-multi-download.torrent?token=local-only",
                    new IngestItemInfo(
                        $"{seriesName} S01E01-E02",
                        null,
                        $"legal-multi-{runId}",
                        "3951",
                        "https://mikanani.me/Home/Bangumi/3951",
                        null,
                        3951,
                        547888,
                        null,
                        null)),
                requireModernMetadata: true);

            Assert.True(result.Accepted, string.Join("; ", result.Errors));
            Assert.Equal("staged", result.Status);
            Assert.Equal(metadata.InfoHash, result.InfoHash);
            Assert.Equal("bt", result.DownloaderId);
            taskId = Assert.IsType<string>(result.IngestId);

            Assert.Equal(
                StagedDispatchResult.Completed,
                await app.Services.GetRequiredService<StagedTorrentDispatcher>()
                    .DispatchNextAsync());
            DownloadTaskSnapshot paused = await WaitForTaskAsync(
                admin,
                metadata.InfoHash,
                static task => task.State == DownloadTaskState.Paused,
                TimeSpan.FromSeconds(10));
            Assert.Equal(metadata.TotalSize, paused.TotalBytes);
            Assert.False(Directory.Exists(torrentRootPath));

            var database = app.Services.GetRequiredService<AnimeGoSqliteDatabase>();
            await SeedVerifiedMultiFileEpisodeAsync(
                database,
                taskId,
                seriesName,
                torrentRootName,
                900002,
                90000201);

            Assert.Equal(
                DownloadPreparationResult.Completed,
                await app.Services.GetRequiredService<DownloadPreparationProcessor>()
                    .RunOnceAsync());
            IReadOnlyList<DownloadFileSnapshot> preparedFiles =
                await admin.ListFilesAsync(metadata.InfoHash);
            Assert.Equal(4, preparedFiles.Count);
            Assert.Equal(
                [1, 1, 0, 0],
                preparedFiles.OrderBy(file => file.Index).Select(file => file.Priority).ToArray());
            Assert.Collection(
                await ReadWantedFilesAsync(database, taskId),
                Assert.True,
                Assert.True,
                Assert.False,
                Assert.False);

            string sourceVideoPath = Path.Combine(torrentRootPath, "Episode 01.mkv");
            string sourceSubtitlePath = Path.Combine(
                torrentRootPath,
                "Episode 01.zh-Hans.forced.ass");
            DownloadTaskSnapshot completed = await WaitForTaskAsync(
                admin,
                metadata.InfoHash,
                task => task.Progress >= 1
                    && File.Exists(sourceVideoPath)
                    && File.Exists(sourceSubtitlePath)
                    && new FileInfo(sourceVideoPath).Length == files[0].Payload.Length
                    && new FileInfo(sourceSubtitlePath).Length == files[1].Payload.Length,
                TimeSpan.FromSeconds(30),
                () => $"video_exists={File.Exists(sourceVideoPath)}, " +
                    $"subtitle_exists={File.Exists(sourceSubtitlePath)}, " +
                    $"web_seed_requests={fileServer.RequestCount}");
            Assert.True(
                completed.State is DownloadTaskState.Seeding or DownloadTaskState.Complete,
                $"Unexpected completed qB state: {completed.State}");
            Assert.True(fileServer.RequestCount > 0);
            Assert.Equal(files[0].Payload, await File.ReadAllBytesAsync(sourceVideoPath));
            Assert.Equal(files[1].Payload, await File.ReadAllBytesAsync(sourceSubtitlePath));

            IReadOnlyList<DownloadFileSnapshot> downloadedFiles =
                await admin.ListFilesAsync(metadata.InfoHash);
            Assert.All(
                downloadedFiles.Where(file => file.Priority == 0),
                file => Assert.Equal(0, file.Progress));

            await app.Services.GetRequiredService<DownloadSnapshotSynchronizer>()
                .SyncOnceAsync();
            Assert.Equal(
                ("downloaded", "completed"),
                await ReadDownloadStateAsync(database, taskId));

            var organizer = app.Services.GetRequiredService<MediaOrganizationProcessor>();
            Assert.Equal(
                MediaOrganizationResult.FilesCompleted,
                await RunOrganizationUntilFilesCompletedAsync(
                    organizer,
                    database,
                    taskId));
            Assert.False(File.Exists(sourceVideoPath));
            Assert.False(File.Exists(sourceSubtitlePath));
            Assert.Equal(files[0].Payload, await File.ReadAllBytesAsync(targetVideoPath));
            Assert.Equal(files[1].Payload, await File.ReadAllBytesAsync(targetSubtitlePath));
            Assert.False(File.Exists(Path.Combine(seasonPath, "E002.mkv")));
            Assert.False(File.Exists(Path.Combine(seriesPath, "poster.jpg")));
            Assert.True(File.Exists(Path.Combine(seriesPath, "tvshow.nfo")));
            Assert.True(File.Exists(Path.Combine(seriesPath, "anime.a_json")));
            Assert.True(File.Exists(Path.Combine(seasonPath, "anime.s_json")));
            Assert.True(File.Exists(Path.Combine(seasonPath, "E001.e_json")));
            Assert.Equal(1, await CountCompletionsAsync(database));

            Assert.Equal(MediaOrganizationResult.CleanupCompleted, await organizer.RunOnceAsync());
            Assert.Equal(
                ("organized", "completed"),
                await ReadOrganizationStateAsync(database, taskId));
            await admin.ConnectAsync();
            Assert.DoesNotContain(
                await admin.ListAsync(),
                task => string.Equals(
                    task.Hash,
                    metadata.InfoHash,
                    StringComparison.OrdinalIgnoreCase));
            Assert.Equal(files[0].Payload, await File.ReadAllBytesAsync(targetVideoPath));
            Assert.Equal(files[1].Payload, await File.ReadAllBytesAsync(targetSubtitlePath));
        }
        finally
        {
            if (app is not null)
            {
                await app.DisposeAsync();
            }

            await BestEffortDeleteTorrentAsync(admin, metadata.InfoHash);
            await BestEffortPostFormAsync(
                adminHttp,
                "api/v2/torrents/removeCategories",
                new Dictionary<string, string> { ["categories"] = category });
            await BestEffortPostFormAsync(
                adminHttp,
                "api/v2/torrents/deleteTags",
                new Dictionary<string, string> { ["tags"] = tag });

            await DeleteExactDirectoryAsync(torrentRootPath, downloadPath);
            await DeleteExactDirectoryAsync(seriesPath, savePath);
            await DeleteExactDirectoryAsync(dataPath, integrationDataPath);
        }

        await admin.ConnectAsync();
        Assert.DoesNotContain(
            await admin.ListAsync(),
            task => string.Equals(
                task.Hash,
                metadata.InfoHash,
                StringComparison.OrdinalIgnoreCase));
        using var categories = JsonDocument.Parse(
            await adminHttp.GetStringAsync("api/v2/torrents/categories"));
        Assert.False(categories.RootElement.TryGetProperty(category, out _));
        using var tags = JsonDocument.Parse(
            await adminHttp.GetStringAsync("api/v2/torrents/tags"));
        Assert.DoesNotContain(
            tags.RootElement.EnumerateArray(),
            value => string.Equals(value.GetString(), tag, StringComparison.Ordinal));
        Assert.False(Directory.Exists(torrentRootPath));
        Assert.False(Directory.Exists(seriesPath));
        Assert.False(Directory.Exists(dataPath));
    }

    private static AnimeGoOptions CreateOptions(
        string dataPath,
        string downloadPath,
        string savePath,
        QbittorrentInstanceOptions downloader,
        string category,
        string tag)
    {
        AnimeGoOptions defaults = AnimeGoDefaults.CreateNative(dataPath);
        return defaults with
        {
            Paths = new PathOptions
            {
                DataPath = dataPath,
                DownloadPath = downloadPath,
                SavePath = savePath,
            },
            Downloaders = new Dictionary<string, QbittorrentInstanceOptions>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["bt"] = downloader,
            },
            InitialSourceProfiles =
            [
                new SourceProfileSeed
                {
                    Id = "mikan",
                    Adapter = "mikan",
                    DownloaderId = "bt",
                    FileStrategy = FileStrategy.Move,
                    AllowedTorrentHosts = ["fixture.invalid"],
                    Category = category,
                    Tags = [tag],
                    SeedingTimeMinutes = 0,
                    RssFilterEnabled = true,
                    RssPriorityEnabled = true,
                },
            ],
        };
    }

    private static async Task SeedVerifiedEpisodeAsync(
        AnimeGoSqliteDatabase database,
        string taskId,
        string seriesName,
        int tmdbSeriesId,
        int tmdbEpisodeId)
    {
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO anime_series (
                id, tmdb_series_id, bangumi_subject_id, canonical_name, original_name,
                needs_tmdb_completion, created_at_utc, updated_at_utc)
            VALUES ($series_id, $tmdb_series_id, 547888, $series_name, $series_name, 0, $now, $now);

            UPDATE task_files
            SET disposition = 'episode', source_episode = '1',
                tmdb_series_id = $tmdb_series_id,
                tmdb_season_number = 1, tmdb_episode_number = 1,
                tmdb_episode_id = $tmdb_episode_id,
                download_wanted = 1
            WHERE task_id = $task_id;

            UPDATE ingest_tasks
            SET status = 'metadata_resolved', updated_at_utc = $now
            WHERE id = $task_id AND status = 'download_preparing';
            """;
        command.Parameters.AddWithValue("$series_id", $"series-{taskId}");
        command.Parameters.AddWithValue("$tmdb_series_id", tmdbSeriesId);
        command.Parameters.AddWithValue("$tmdb_episode_id", tmdbEpisodeId);
        command.Parameters.AddWithValue("$series_name", seriesName);
        command.Parameters.AddWithValue("$task_id", taskId);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        Assert.Equal(3, await command.ExecuteNonQueryAsync());
    }

    private static async Task SeedVerifiedMultiFileEpisodeAsync(
        AnimeGoSqliteDatabase database,
        string taskId,
        string seriesName,
        string torrentRootName,
        int tmdbSeriesId,
        int tmdbEpisodeId)
    {
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO anime_series (
                id, tmdb_series_id, bangumi_subject_id, canonical_name, original_name,
                needs_tmdb_completion, created_at_utc, updated_at_utc)
            VALUES ($series_id, $tmdb_series_id, 547888, $series_name, $series_name, 0, $now, $now);

            UPDATE task_files
            SET disposition = 'episode', source_episode = '1', file_episode_candidate = '1',
                tmdb_series_id = $tmdb_series_id,
                tmdb_season_number = 1, tmdb_episode_number = 1,
                tmdb_episode_id = $tmdb_episode_id,
                download_wanted = 1
            WHERE task_id = $task_id
              AND relative_path = $video_path;

            UPDATE task_files
            SET disposition = 'episode', source_episode = '1', file_episode_candidate = '1',
                tmdb_series_id = $tmdb_series_id,
                tmdb_season_number = 1, tmdb_episode_number = 1,
                tmdb_episode_id = $tmdb_episode_id,
                associated_task_file_id = (
                    SELECT id FROM task_files
                    WHERE task_id = $task_id AND relative_path = $video_path),
                rename_suffix = '.zh-Hans.forced.ass',
                download_wanted = 1
            WHERE task_id = $task_id
              AND relative_path = $subtitle_path;

            UPDATE task_files
            SET disposition = 'duplicate', source_episode = '2', file_episode_candidate = '2',
                download_wanted = 0
            WHERE task_id = $task_id
              AND relative_path = $duplicate_path;

            UPDATE task_files
            SET disposition = 'ignored', download_wanted = 0
            WHERE task_id = $task_id
              AND relative_path = $ignored_path;

            UPDATE ingest_tasks
            SET status = 'metadata_resolved', updated_at_utc = $now
            WHERE id = $task_id AND status = 'download_preparing';
            """;
        command.Parameters.AddWithValue("$series_id", $"series-{taskId}");
        command.Parameters.AddWithValue("$tmdb_series_id", tmdbSeriesId);
        command.Parameters.AddWithValue("$tmdb_episode_id", tmdbEpisodeId);
        command.Parameters.AddWithValue("$series_name", seriesName);
        command.Parameters.AddWithValue("$task_id", taskId);
        command.Parameters.AddWithValue("$video_path", $"{torrentRootName}/Episode 01.mkv");
        command.Parameters.AddWithValue(
            "$subtitle_path",
            $"{torrentRootName}/Episode 01.zh-Hans.forced.ass");
        command.Parameters.AddWithValue("$duplicate_path", $"{torrentRootName}/Episode 02.mkv");
        command.Parameters.AddWithValue("$ignored_path", $"{torrentRootName}/poster.jpg");
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        Assert.Equal(6, await command.ExecuteNonQueryAsync());
    }

    private static async Task<bool[]> ReadWantedFilesAsync(
        AnimeGoSqliteDatabase database,
        string taskId)
    {
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT download_wanted
            FROM task_files
            WHERE task_id = $task_id
            ORDER BY download_file_index;
            """;
        command.Parameters.AddWithValue("$task_id", taskId);
        var result = new List<bool>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(!reader.IsDBNull(0) && reader.GetInt64(0) != 0);
        }
        return result.ToArray();
    }

    private static async Task<(string TaskStatus, string PreparationState)> ReadDownloadStateAsync(
        AnimeGoSqliteDatabase database,
        string taskId)
    {
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT task.status, job.preparation_state
            FROM ingest_tasks AS task
            JOIN download_jobs AS job ON job.task_id = task.id
            WHERE task.id = $task_id;
            """;
        command.Parameters.AddWithValue("$task_id", taskId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetString(0), reader.GetString(1));
    }

    private static async Task<(string TaskStatus, string OrganizationState)> ReadOrganizationStateAsync(
        AnimeGoSqliteDatabase database,
        string taskId)
    {
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT task.status, job.organization_state
            FROM ingest_tasks AS task
            JOIN download_jobs AS job ON job.task_id = task.id
            WHERE task.id = $task_id;
            """;
        command.Parameters.AddWithValue("$task_id", taskId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetString(0), reader.GetString(1));
    }

    private static async Task<int> CountCompletionsAsync(
        AnimeGoSqliteDatabase database)
    {
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*) FROM completion_records;
            """;
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<MediaOrganizationResult> RunOrganizationUntilFilesCompletedAsync(
        MediaOrganizationProcessor organizer,
        AnimeGoSqliteDatabase database,
        string taskId)
    {
        for (var attempt = 0; attempt < 25; attempt++)
        {
            MediaOrganizationResult result = await organizer.RunOnceAsync();
            if (result == MediaOrganizationResult.FilesCompleted)
            {
                return result;
            }

            if (result != MediaOrganizationResult.RetryScheduled)
            {
                throw new Xunit.Sdk.XunitException(
                    $"Unexpected organization result before file completion: {result}.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200));
            await using var connection = await database.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE download_jobs
                SET organization_next_attempt_at_utc = $now
                WHERE task_id = $task_id
                  AND organization_state = 'pending';
                """;
            command.Parameters.AddWithValue(
                "$now",
                DateTimeOffset.UtcNow.AddSeconds(-1).ToString("O"));
            command.Parameters.AddWithValue("$task_id", taskId);
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }

        await using (var connection = await database.OpenConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT organization_failure_code
                FROM download_jobs
                WHERE task_id = $task_id;
                """;
            command.Parameters.AddWithValue("$task_id", taskId);
            string? failure = await command.ExecuteScalarAsync() as string;
            throw new Xunit.Sdk.XunitException(
                $"Organization did not release the qB file handle within five seconds; " +
                $"last failure={failure}.");
        }
    }

    private static async Task<DownloadTaskSnapshot> WaitForTaskAsync(
        QbittorrentClient client,
        string infoHash,
        Func<DownloadTaskSnapshot, bool> predicate,
        TimeSpan timeout,
        Func<string>? diagnostic = null)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        DownloadTaskSnapshot? last = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            await client.ConnectAsync();
            last = (await client.ListAsync()).SingleOrDefault(task =>
                string.Equals(task.Hash, infoHash, StringComparison.OrdinalIgnoreCase));
            if (last is not null && predicate(last))
            {
                return last;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        throw new Xunit.Sdk.XunitException(
            $"Timed out waiting for qB task {infoHash}; last state={last?.State}, " +
            $"progress={last?.Progress}, downloaded={last?.DownloadedBytes}/{last?.TotalBytes}, " +
            $"{diagnostic?.Invoke()}.");
    }

    private static byte[] CreatePayload() => CreatePayload(PayloadLength, 17);

    private static byte[] CreatePayload(int length, int seed)
    {
        var payload = new byte[length];
        for (var index = 0; index < payload.Length; index++)
        {
            payload[index] = (byte)((index * 31 + seed) % 251);
        }

        return payload;
    }

    private static byte[] BuildTorrent(
        string fileName,
        byte[] payload,
        Uri webSeed)
    {
        using var output = new MemoryStream();
        WriteAscii(output, "d");
        WriteString(output, "announce");
        WriteString(output, "http://127.0.0.1:9/announce");
        WriteString(output, "info");
        WriteAscii(output, "d");
        WriteString(output, "length");
        WriteInteger(output, payload.LongLength);
        WriteString(output, "name");
        WriteString(output, fileName);
        WriteString(output, "piece length");
        WriteInteger(output, PieceLength);
        WriteString(output, "pieces");
        using (var hashes = new MemoryStream())
        {
            for (var offset = 0; offset < payload.Length; offset += PieceLength)
            {
                int count = Math.Min(PieceLength, payload.Length - offset);
#pragma warning disable CA5350 // BitTorrent v1 mandates SHA-1 piece hashes.
                hashes.Write(SHA1.HashData(payload.AsSpan(offset, count)));
#pragma warning restore CA5350
            }

            WriteBytes(output, hashes.ToArray());
        }
        WriteAscii(output, "e");
        WriteString(output, "url-list");
        WriteString(output, webSeed.AbsoluteUri);
        WriteAscii(output, "e");
        return output.ToArray();
    }

    private static byte[] BuildMultiFileTorrent(
        string rootName,
        IReadOnlyList<LegalTorrentFile> files,
        Uri webSeedBaseUrl)
    {
        using var concatenated = new MemoryStream();
        foreach (LegalTorrentFile file in files)
        {
            concatenated.Write(file.Payload);
        }
        byte[] payload = concatenated.ToArray();

        using var output = new MemoryStream();
        WriteAscii(output, "d");
        WriteString(output, "announce");
        WriteString(output, "http://127.0.0.1:9/announce");
        WriteString(output, "info");
        WriteAscii(output, "d");
        WriteString(output, "files");
        WriteAscii(output, "l");
        foreach (LegalTorrentFile file in files)
        {
            WriteAscii(output, "d");
            WriteString(output, "length");
            WriteInteger(output, file.Payload.LongLength);
            WriteString(output, "path");
            WriteAscii(output, "l");
            foreach (string component in file.PathComponents)
            {
                WriteString(output, component);
            }
            WriteAscii(output, "e");
            WriteAscii(output, "e");
        }
        WriteAscii(output, "e");
        WriteString(output, "name");
        WriteString(output, rootName);
        WriteString(output, "piece length");
        WriteInteger(output, PieceLength);
        WriteString(output, "pieces");
        using (var hashes = new MemoryStream())
        {
            for (var offset = 0; offset < payload.Length; offset += PieceLength)
            {
                int count = Math.Min(PieceLength, payload.Length - offset);
#pragma warning disable CA5350 // BitTorrent v1 mandates SHA-1 piece hashes.
                hashes.Write(SHA1.HashData(payload.AsSpan(offset, count)));
#pragma warning restore CA5350
            }

            WriteBytes(output, hashes.ToArray());
        }
        WriteAscii(output, "e");
        WriteString(output, "url-list");
        WriteString(output, webSeedBaseUrl.AbsoluteUri);
        WriteAscii(output, "e");
        return output.ToArray();
    }

    private static void WriteString(Stream stream, string value) =>
        WriteBytes(stream, Encoding.UTF8.GetBytes(value));

    private static void WriteBytes(Stream stream, byte[] value)
    {
        WriteAscii(stream, value.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        WriteAscii(stream, ":");
        stream.Write(value);
    }

    private static void WriteInteger(Stream stream, long value) =>
        WriteAscii(
            stream,
            $"i{value.ToString(System.Globalization.CultureInfo.InvariantCulture)}e");

    private static void WriteAscii(Stream stream, string value) =>
        stream.Write(Encoding.ASCII.GetBytes(value));

    private static async Task PostFormAsync(
        HttpClient client,
        string path,
        IReadOnlyDictionary<string, string> values)
    {
        using var response = await client.PostAsync(path, new FormUrlEncodedContent(values));
        response.EnsureSuccessStatusCode();
    }

    private static async Task BestEffortPostFormAsync(
        HttpClient client,
        string path,
        IReadOnlyDictionary<string, string> values)
    {
        try
        {
            await PostFormAsync(client, path, values);
        }
        catch (HttpRequestException)
        {
        }
        catch (TaskCanceledException)
        {
        }
    }

    private static async Task BestEffortDeleteTorrentAsync(
        QbittorrentClient client,
        string infoHash)
    {
        try
        {
            await client.ConnectAsync();
            await client.DeleteAsync([infoHash], deleteFiles: false);
        }
        catch (HttpRequestException)
        {
        }
        catch (TaskCanceledException)
        {
        }
    }

    private static async Task DeleteExactFileAsync(string path)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
                return;
            }
            catch (IOException) when (attempt < 49)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100));
            }
        }
    }

    private static async Task DeleteExactDirectoryAsync(
        string path,
        string requiredParent)
    {
        AssertWithin(requiredParent, path);
        for (var attempt = 0; attempt < 50; attempt++)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
                return;
            }
            catch (IOException) when (attempt < 49)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100));
            }
        }
    }

    private static void AssertWithin(string root, string candidate)
    {
        string normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root))
            + Path.DirectorySeparatorChar;
        Assert.StartsWith(
            normalizedRoot,
            Path.GetFullPath(candidate),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string Required(string name) =>
        Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException(
            $"Run eng/qbittorrent-local-integration.ps1; missing {name}.");

    private sealed class GeneratedTorrentStagingService(
        string stagingPath,
        byte[] torrentBytes,
        TorrentMetadata metadata) : ITorrentStagingService
    {
        private string? _stagingFileName;

        public async Task<StagedTorrent> StageAsync(
            Uri secretUrl,
            TorrentSourcePolicy sourcePolicy,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal("fixture.invalid", secretUrl.IdnHost);
            Assert.Equal("mikan", sourcePolicy.SourceProfileId);
            Directory.CreateDirectory(stagingPath);
            _stagingFileName = $"{metadata.InfoHash}-{Guid.NewGuid():N}.torrent";
            string path = Path.Combine(stagingPath, _stagingFileName);
            await File.WriteAllBytesAsync(path, torrentBytes, cancellationToken);
            return new StagedTorrent(path, metadata);
        }

        public Task<bool> DeleteAsync(
            string stagingFileName,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(_stagingFileName, stagingFileName);
            string path = Path.Combine(stagingPath, stagingFileName);
            bool existed = File.Exists(path);
            File.Delete(path);
            return Task.FromResult(existed);
        }

        public FileStream OpenRead(string stagingFileName)
        {
            Assert.Equal(_stagingFileName, stagingFileName);
            return File.OpenRead(Path.Combine(stagingPath, stagingFileName));
        }

        public Task<int> CleanupExpiredAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(0);
        }
    }

    private sealed record LegalTorrentFile(
        string[] PathComponents,
        byte[] Payload);

    private sealed class LoopbackMultiFileServer : IAsyncDisposable
    {
        private readonly Dictionary<string, byte[]> _files;
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _stopping = new();
        private readonly Task _serveTask;
        private int _requestCount;

        public LoopbackMultiFileServer(
            string rootName,
            IReadOnlyList<LegalTorrentFile> files)
        {
            _files = files.ToDictionary(
                file => "/" + rootName + "/" + string.Join('/', file.PathComponents),
                file => file.Payload,
                StringComparer.Ordinal);
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            int port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            BaseUrl = new Uri($"http://127.0.0.1:{port}/");
            _serveTask = ServeAsync(_stopping.Token);
        }

        public Uri BaseUrl { get; }

        public int RequestCount => Volatile.Read(ref _requestCount);

        public async ValueTask DisposeAsync()
        {
            await _stopping.CancelAsync();
            _listener.Stop();
            try
            {
                await _serveTask;
            }
            catch (OperationCanceledException)
            {
            }
            catch (SocketException) when (_stopping.IsCancellationRequested)
            {
            }
            _stopping.Dispose();
        }

        private async Task ServeAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client = await _listener.AcceptTcpClientAsync(cancellationToken);
                using (client)
                {
                    await ServeClientAsync(client, cancellationToken);
                }
            }
        }

        private async Task ServeClientAsync(
            TcpClient client,
            CancellationToken cancellationToken)
        {
            client.ReceiveTimeout = 5000;
            client.SendTimeout = 5000;
            await using NetworkStream stream = client.GetStream();
            using var reader = new StreamReader(
                stream,
                Encoding.ASCII,
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 4096,
                leaveOpen: true);
            string? requestLine = await reader.ReadLineAsync(cancellationToken);
            if (requestLine is null)
            {
                return;
            }

            string? range = null;
            while (await reader.ReadLineAsync(cancellationToken) is { Length: > 0 } line)
            {
                if (line.StartsWith("Range:", StringComparison.OrdinalIgnoreCase))
                {
                    range = line[6..].Trim();
                }
            }

            string[] requestParts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string requestPath = requestParts.Length == 3
                ? Uri.UnescapeDataString(requestParts[1].Split('?', 2)[0])
                : string.Empty;
            if (requestParts.Length != 3
                || requestParts[0] is not ("GET" or "HEAD")
                || !_files.TryGetValue(requestPath, out byte[]? payload))
            {
                await WriteStatusAsync(stream, "404 Not Found", 0, cancellationToken);
                return;
            }

            Interlocked.Increment(ref _requestCount);
            (int start, int end, bool partial) = ParseRange(range, payload.Length);
            int count = end - start + 1;
            var header = new StringBuilder()
                .Append("HTTP/1.1 ")
                .Append(partial ? "206 Partial Content" : "200 OK")
                .Append("\r\nContent-Type: application/octet-stream\r\nAccept-Ranges: bytes\r\n")
                .Append("Content-Length: ")
                .Append(count)
                .Append("\r\n");
            if (partial)
            {
                header.Append("Content-Range: bytes ")
                    .Append(start)
                    .Append('-')
                    .Append(end)
                    .Append('/')
                    .Append(payload.Length)
                    .Append("\r\n");
            }
            header.Append("Connection: close\r\n\r\n");
            await stream.WriteAsync(
                Encoding.ASCII.GetBytes(header.ToString()),
                cancellationToken);
            if (requestParts[0] == "GET")
            {
                await stream.WriteAsync(payload.AsMemory(start, count), cancellationToken);
            }
        }

        private static (int Start, int End, bool Partial) ParseRange(
            string? value,
            int length)
        {
            if (value is null
                || !value.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
            {
                return (0, length - 1, false);
            }

            string[] bounds = value[6..].Split('-', 2);
            if (bounds.Length != 2
                || !int.TryParse(bounds[0], out int start)
                || start < 0
                || start >= length)
            {
                return (0, length - 1, false);
            }

            int end = int.TryParse(bounds[1], out int parsedEnd)
                ? Math.Min(parsedEnd, length - 1)
                : length - 1;
            return end >= start
                ? (start, end, true)
                : (0, length - 1, false);
        }

        private static async Task WriteStatusAsync(
            Stream stream,
            string status,
            int contentLength,
            CancellationToken cancellationToken)
        {
            byte[] response = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {status}\r\nContent-Length: {contentLength}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(response, cancellationToken);
        }
    }

    private sealed class LoopbackFileServer : IAsyncDisposable
    {
        private readonly string _fileName;
        private readonly byte[] _payload;
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _stopping = new();
        private readonly Task _serveTask;
        private int _requestCount;

        public LoopbackFileServer(string fileName, byte[] payload)
        {
            _fileName = fileName;
            _payload = payload;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            int port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            FileUrl = new Uri($"http://127.0.0.1:{port}/{Uri.EscapeDataString(fileName)}");
            _serveTask = ServeAsync(_stopping.Token);
        }

        public Uri FileUrl { get; }

        public int RequestCount => Volatile.Read(ref _requestCount);

        public async ValueTask DisposeAsync()
        {
            await _stopping.CancelAsync();
            _listener.Stop();
            try
            {
                await _serveTask;
            }
            catch (OperationCanceledException)
            {
            }
            catch (SocketException) when (_stopping.IsCancellationRequested)
            {
            }
            _stopping.Dispose();
        }

        private async Task ServeAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client = await _listener.AcceptTcpClientAsync(cancellationToken);
                using (client)
                {
                    await ServeClientAsync(client, cancellationToken);
                }
            }
        }

        private async Task ServeClientAsync(
            TcpClient client,
            CancellationToken cancellationToken)
        {
            client.ReceiveTimeout = 5000;
            client.SendTimeout = 5000;
            await using NetworkStream stream = client.GetStream();
            using var reader = new StreamReader(
                stream,
                Encoding.ASCII,
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 4096,
                leaveOpen: true);
            string? requestLine = await reader.ReadLineAsync(cancellationToken);
            if (requestLine is null)
            {
                return;
            }

            string? range = null;
            while (await reader.ReadLineAsync(cancellationToken) is { Length: > 0 } line)
            {
                if (line.StartsWith("Range:", StringComparison.OrdinalIgnoreCase))
                {
                    range = line[6..].Trim();
                }
            }

            string[] requestParts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (requestParts.Length != 3
                || requestParts[0] is not ("GET" or "HEAD")
                || !Uri.UnescapeDataString(requestParts[1]).EndsWith(
                    _fileName,
                    StringComparison.Ordinal))
            {
                await WriteStatusAsync(stream, "404 Not Found", 0, cancellationToken);
                return;
            }

            Interlocked.Increment(ref _requestCount);
            (int start, int end, bool partial) = ParseRange(range, _payload.Length);
            int count = end - start + 1;
            var header = new StringBuilder()
                .Append("HTTP/1.1 ")
                .Append(partial ? "206 Partial Content" : "200 OK")
                .Append("\r\nContent-Type: application/octet-stream\r\nAccept-Ranges: bytes\r\n")
                .Append("Content-Length: ")
                .Append(count)
                .Append("\r\n");
            if (partial)
            {
                header.Append("Content-Range: bytes ")
                    .Append(start)
                    .Append('-')
                    .Append(end)
                    .Append('/')
                    .Append(_payload.Length)
                    .Append("\r\n");
            }
            header.Append("Connection: close\r\n\r\n");
            await stream.WriteAsync(
                Encoding.ASCII.GetBytes(header.ToString()),
                cancellationToken);
            if (requestParts[0] == "GET")
            {
                await stream.WriteAsync(
                    _payload.AsMemory(start, count),
                    cancellationToken);
            }
        }

        private static (int Start, int End, bool Partial) ParseRange(
            string? value,
            int length)
        {
            if (value is null
                || !value.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
            {
                return (0, length - 1, false);
            }

            string[] bounds = value[6..].Split('-', 2);
            if (bounds.Length != 2
                || !int.TryParse(bounds[0], out int start)
                || start < 0
                || start >= length)
            {
                return (0, length - 1, false);
            }

            int end = int.TryParse(bounds[1], out int parsedEnd)
                ? Math.Min(parsedEnd, length - 1)
                : length - 1;
            return end >= start
                ? (start, end, true)
                : (0, length - 1, false);
        }

        private static async Task WriteStatusAsync(
            Stream stream,
            string status,
            int contentLength,
            CancellationToken cancellationToken)
        {
            byte[] response = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {status}\r\nContent-Length: {contentLength}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(response, cancellationToken);
        }
    }
}
