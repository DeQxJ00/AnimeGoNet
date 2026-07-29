using System.Net;
using System.Text.Json;
using AnimeGoNet.App;
using AnimeGoNet.App.Downloads;
using AnimeGoNet.App.Ingest;
using AnimeGoNet.App.Torrents;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Downloads;
using AnimeGoNet.Core.Ingest;
using AnimeGoNet.Core.Torrents;
using AnimeGoNet.Data.Sqlite;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeGoNet.LocalIntegration.Tests;

public sealed class QbittorrentDispatchFixtureTests
{
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task UnifiedIngestDispatchesPausedIdentifiedFixtureAndCleansUp()
    {
        Assert.Equal("1", Required("ANIMEGONET_QBIT_INTEGRATION"));
        Assert.Equal("1", Required("ANIMEGONET_QBIT_DISPATCH_FIXTURE"));

        var sandbox = Path.GetFullPath(Required("ANIMEGONET_QBIT_SANDBOX"));
        var downloadPath = Path.GetFullPath(Required("ANIMEGONET_QBIT_DOWNLOAD_PATH"));
        var savePath = Path.GetFullPath(Required("ANIMEGONET_QBIT_SAVE_PATH"));
        var integrationDataPath = Path.GetFullPath(Required("ANIMEGONET_QBIT_DATA_PATH"));
        var fixturePath = Path.GetFullPath(Required("ANIMEGONET_QBIT_TORRENT_FIXTURE"));
        var baseUrl = new Uri(Required("ANIMEGONET_QBIT_BASE_URL"));
        var runId = Guid.NewGuid().ToString("N");
        var category = $"animegonet-integration-{runId}";
        var tag = $"animegonet-test-{runId}";
        var dataPath = Path.Combine(integrationDataPath, "integration", $"qbit-dispatch-{runId}");

        AssertWithin(sandbox, downloadPath);
        AssertWithin(sandbox, savePath);
        AssertWithin(sandbox, dataPath);
        Assert.True(File.Exists(fixturePath));

        var fixtureBytes = Convert.FromBase64String(
            (await File.ReadAllTextAsync(fixturePath)).Trim());
        var metadata = TorrentMetainfoParser.Parse(fixtureBytes);
        var payloadPaths = metadata.Files
            .Where(file => !file.IsPadding)
            .Select(file => ResolvePayloadPath(downloadPath, file.RelativePath))
            .ToArray();
        Assert.All(payloadPaths, path => Assert.False(File.Exists(path)));

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
            Timeout = TimeSpan.FromSeconds(10),
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

        var options = CreateOptions(
            dataPath,
            downloadPath,
            savePath,
            downloader,
            category,
            tag);
        var layout = DirectoryLayout.From(options.Paths);
        var staging = new FixtureTorrentStagingService(
            layout.StagingPath,
            fixtureBytes,
            metadata);
        using var registry = new QbittorrentClientRegistry(options);
        WebApplication? app = null;
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
                    "https://fixture.invalid/animegonet-ci.torrent?token=local-only",
                    new IngestItemInfo(
                        "AnimeGoNet isolated qB fixture",
                        null,
                        $"fixture-{runId}",
                        "3951",
                        "https://mikanani.me/Home/Bangumi/3951",
                        null,
                        3951,
                        547888,
                        null,
                        null)),
                requireModernMetadata: true);

            Assert.True(result.Accepted);
            Assert.Equal("staged", result.Status);
            Assert.Equal(metadata.InfoHash, result.InfoHash);
            Assert.Equal("bt", result.DownloaderId);
            Assert.NotNull(staging.LastStagingFileName);
            Assert.True(File.Exists(Path.Combine(layout.StagingPath, staging.LastStagingFileName)));

            var dispatch = app.Services.GetRequiredService<StagedTorrentDispatcher>();
            Assert.Equal(StagedDispatchResult.Completed, await dispatch.DispatchNextAsync());

            await admin.ConnectAsync();
            var task = await WaitForPausedTaskAsync(admin, metadata.InfoHash);
            Assert.Equal(DownloadTaskState.Paused, task.State);
            Assert.Equal(metadata.TotalSize, task.TotalBytes);

            using var qbitInfo = await GetTorrentInfoAsync(adminHttp, metadata.InfoHash);
            var qbitTask = Assert.Single(qbitInfo.RootElement.EnumerateArray());
            Assert.Equal(category, qbitTask.GetProperty("category").GetString());
            Assert.Contains(
                tag,
                (qbitTask.GetProperty("tags").GetString() ?? string.Empty)
                    .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));

            var database = app.Services.GetRequiredService<AnimeGoSqliteDatabase>();
            var lifecycle = await ReadLifecycleAsync(database, result.IngestId!);
            Assert.Equal("download_preparing", lifecycle.Status);
            Assert.Equal(1, lifecycle.DownloadJobs);
            Assert.Equal(downloadPath, lifecycle.DownloadRoot);
            Assert.Equal(savePath, lifecycle.SaveRoot);
            Assert.False(File.Exists(Path.Combine(layout.StagingPath, staging.LastStagingFileName)));
            Assert.All(payloadPaths, path => Assert.False(File.Exists(path)));
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

            foreach (var payloadPath in payloadPaths)
            {
                if (File.Exists(payloadPath))
                {
                    File.Delete(payloadPath);
                }
            }

            if (Directory.Exists(dataPath))
            {
                Directory.Delete(dataPath, recursive: true);
            }
        }

        await admin.ConnectAsync();
        Assert.DoesNotContain(
            await admin.ListAsync(),
            item => string.Equals(item.Hash, metadata.InfoHash, StringComparison.OrdinalIgnoreCase));
        using var categories = JsonDocument.Parse(
            await adminHttp.GetStringAsync("api/v2/torrents/categories"));
        Assert.False(categories.RootElement.TryGetProperty(category, out _));
        using var tags = JsonDocument.Parse(
            await adminHttp.GetStringAsync("api/v2/torrents/tags"));
        Assert.DoesNotContain(
            tags.RootElement.EnumerateArray(),
            value => string.Equals(value.GetString(), tag, StringComparison.Ordinal));
        Assert.All(payloadPaths, path => Assert.False(File.Exists(path)));
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
        var defaults = AnimeGoDefaults.CreateNative(dataPath);
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

    private static async Task<LifecycleState> ReadLifecycleAsync(
        AnimeGoSqliteDatabase database,
        string taskId)
    {
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT task.status,
                   (SELECT COUNT(*) FROM download_jobs WHERE task_id = task.id),
                   (SELECT download_root_path FROM download_jobs WHERE task_id = task.id),
                   (SELECT save_root_path FROM download_jobs WHERE task_id = task.id)
            FROM ingest_tasks AS task
            WHERE task.id = $task_id;
            """;
        command.Parameters.AddWithValue("$task_id", taskId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new LifecycleState(
            reader.GetString(0),
            reader.GetInt32(1),
            reader.GetString(2),
            reader.GetString(3));
    }

    private static async Task<JsonDocument> GetTorrentInfoAsync(
        HttpClient client,
        string infoHash)
    {
        using var response = await client.GetAsync(
            $"api/v2/torrents/info?hashes={Uri.EscapeDataString(infoHash)}");
        response.EnsureSuccessStatusCode();
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
    }

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

    private static async Task<DownloadTaskSnapshot> WaitForPausedTaskAsync(
        QbittorrentClient client,
        string infoHash)
    {
        DownloadTaskSnapshot? last = null;
        for (var attempt = 0; attempt < 50; attempt++)
        {
            last = (await client.ListAsync()).SingleOrDefault(
                item => string.Equals(item.Hash, infoHash, StringComparison.OrdinalIgnoreCase));
            if (last?.State == DownloadTaskState.Paused)
            {
                return last;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        return Assert.IsType<DownloadTaskSnapshot>(last);
    }

    private static string ResolvePayloadPath(string downloadRoot, string relativePath)
    {
        var result = Path.GetFullPath(
            Path.Combine(
                downloadRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
        AssertWithin(downloadRoot, result);
        return result;
    }

    private static void AssertWithin(string root, string candidate)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root))
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

    private sealed record LifecycleState(
        string Status,
        int DownloadJobs,
        string DownloadRoot,
        string SaveRoot);

    private sealed class FixtureTorrentStagingService(
        string stagingPath,
        byte[] fixtureBytes,
        TorrentMetadata metadata) : ITorrentStagingService
    {
        public string? LastStagingFileName { get; private set; }

        public async Task<StagedTorrent> StageAsync(
            Uri secretUrl,
            TorrentSourcePolicy sourcePolicy,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal("fixture.invalid", secretUrl.IdnHost);
            Assert.Equal("mikan", sourcePolicy.SourceProfileId);
            Assert.Contains("fixture.invalid", sourcePolicy.AllowedHosts);
            Directory.CreateDirectory(stagingPath);
            LastStagingFileName = $"{metadata.InfoHash}-{Guid.NewGuid():N}.torrent";
            var path = Path.Combine(stagingPath, LastStagingFileName);
            await File.WriteAllBytesAsync(path, fixtureBytes, cancellationToken);
            return new StagedTorrent(path, metadata);
        }

        public Task<bool> DeleteAsync(
            string stagingFileName,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.Combine(stagingPath, stagingFileName);
            var existed = File.Exists(path);
            File.Delete(path);
            return Task.FromResult(existed);
        }

        public FileStream OpenRead(string stagingFileName) =>
            File.OpenRead(Path.Combine(stagingPath, stagingFileName));

        public Task<int> CleanupExpiredAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
    }
}
