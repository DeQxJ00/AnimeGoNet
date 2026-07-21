using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Downloads;
using AnimeGoNet.App.Torrents;
using AnimeGoNet.Data.Downloads;
using AnimeGoNet.Data.Ingest;
using AnimeGoNet.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeGoNet.App.Tests.Api;

public sealed class MinimalApiTests
{
    [Fact]
    public async Task DockerModeRequiresAccessKey()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "animegonet-app-tests", Guid.NewGuid().ToString("N"));
        var options = AnimeGoNet.Core.Configuration.AnimeGoDefaults.CreateNative(rootPath);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => AnimeGoApplication.BuildAsync([], options, accessKey: null, runningInContainer: true));

        Assert.Contains("requires a non-empty access_key", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PingPreservesLegacyEnvelope()
    {
        await using var app = await RunningApp.StartAsync();

        using var response = await app.Client.GetAsync("/ping");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(200, json.RootElement.GetProperty("code").GetInt32());
        Assert.Equal("pong", json.RootElement.GetProperty("msg").GetString());
        Assert.True(json.RootElement.GetProperty("data").GetProperty("time").GetInt64() > 0);
    }

    [Fact]
    public async Task StatusReportsDatabaseAndEffectivePaths()
    {
        await using var app = await RunningApp.StartAsync();

        using var response = await app.Client.GetAsync("/api/v1/status");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.True(
            json.RootElement.TryGetProperty("database_schema_version", out var schemaVersion),
            json.RootElement.GetRawText());
        Assert.Equal(DatabaseSchema.CurrentVersion, schemaVersion.GetInt32());
        Assert.Equal(Path.Combine(app.RootPath, "data"), json.RootElement.GetProperty("paths").GetProperty("data_path").GetString());
        Assert.True(File.Exists(Path.Combine(app.RootPath, "data", "animegonet.db")));
    }

    [Fact]
    public async Task StatusReportsConfiguredTmdbWithoutEchoingCredential()
    {
        await using var app = await RunningApp.StartAsync(configure: options => options with
        {
            Metadata = options.Metadata with
            {
                Tmdb = options.Metadata.Tmdb with { ReadAccessToken = "private-tmdb-token" },
            },
        });

        using var response = await app.Client.GetAsync("/api/v1/status");
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);

        Assert.True(json.RootElement.GetProperty("capabilities").GetProperty("tmdb").GetBoolean());
        Assert.DoesNotContain("private-tmdb-token", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MetadataTaskListShowsPipelineStateWithoutSecretTorrentUrl()
    {
        await using var app = await RunningApp.StartAsync();
        const string payload = """
            {
              "source": "mikan",
              "data": [{
                "torrent": "https://mikanani.me/private-passkey/metadata-list.torrent",
                "info": { "title": "Metadata list", "mikanid": 3951, "bgmid": 547888 }
              }]
            }
            """;
        using var ingest = await app.Client.PostAsync(
            "/api/v1/ingest",
            new StringContent(payload, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, ingest.StatusCode);

        using var response = await app.Client.GetAsync("/api/v1/metadata/tasks");
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        var item = Assert.Single(json.RootElement.GetProperty("items").EnumerateArray());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Metadata list", item.GetProperty("title").GetString());
        Assert.Equal("staged", item.GetProperty("status").GetString());
        Assert.Equal(3951, item.GetProperty("mikanid").GetInt32());
        Assert.Equal(0, item.GetProperty("duplicate_file_count").GetInt32());
        Assert.Equal(1, item.GetProperty("pending_file_count").GetInt32());
        Assert.DoesNotContain("private-passkey", body, StringComparison.Ordinal);
        Assert.DoesNotContain("metadata-list.torrent", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProtectedApiAcceptsDirectAndLegacyHashedAccessKeys()
    {
        const string accessKey = "test-secret";
        await using var app = await RunningApp.StartAsync(accessKey);

        using var denied = await app.Client.GetAsync("/api/v1/status");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);

        using var directRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/status");
        directRequest.Headers.Add("X-AnimeGo-Access-Key", accessKey);
        using var direct = await app.Client.SendAsync(directRequest);
        Assert.Equal(HttpStatusCode.OK, direct.StatusCode);

        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(accessKey)));
        using var legacyRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/status");
        legacyRequest.Headers.Add("Access-Key", hash);
        using var legacy = await app.Client.SendAsync(legacyRequest);
        Assert.Equal(HttpStatusCode.OK, legacy.StatusCode);
    }

    [Fact]
    public async Task UnifiedIngestRoutesSourcesAndReportsEveryRejectedItem()
    {
        await using var app = await RunningApp.StartAsync(configure: options => options with
        {
            InitialSourceProfiles =
            [
                .. options.InitialSourceProfiles,
                new SourceProfileSeed
                {
                    Id = "u2",
                    Adapter = "u2",
                    DownloaderId = "pt",
                    FileStrategy = FileStrategy.Link,
                    AllowedTorrentHosts = ["u2.invalid"],
                },
            ],
        });
        const string payload = """
            {
              "source": "mikan",
              "data": [
                {
                  "torrent": "https://tracker.invalid/personal-passkey/one.torrent",
                  "info": { "title": "Episode 1", "mikanid": 3951, "bgmid": 547888 }
                },
                {
                  "torrent": "https://tracker.invalid/personal-passkey/two.torrent",
                  "info": { "title": "Episode 2", "mikanid": 3951 }
                },
                {
                  "torrent": "https://tracker.invalid/personal-passkey/three.torrent",
                  "info": null
                }
              ]
            }
            """;

        using var response = await app.Client.PostAsync(
            "/api/v1/ingest",
            new StringContent(payload, Encoding.UTF8, "application/json"));
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, json.RootElement.GetProperty("accepted_count").GetInt32());
        Assert.Equal(2, json.RootElement.GetProperty("rejected_count").GetInt32());
        Assert.Equal("bt", json.RootElement.GetProperty("items")[0].GetProperty("downloader_id").GetString());
        Assert.Equal("staged", json.RootElement.GetProperty("items")[0].GetProperty("status").GetString());
        Assert.Equal(40, json.RootElement.GetProperty("items")[0].GetProperty("info_hash").GetString()!.Length);
        Assert.Equal(1, json.RootElement.GetProperty("items")[0].GetProperty("file_count").GetInt32());
        Assert.Equal("rejected", json.RootElement.GetProperty("items")[1].GetProperty("status").GetString());
        Assert.Equal("info is required", json.RootElement.GetProperty("items")[2].GetProperty("errors")[0].GetString());
        Assert.DoesNotContain("personal-passkey", body, StringComparison.Ordinal);

        const string u2Payload = """
            {
              "source": "u2",
              "data": [
                {
                  "torrent": "https://u2.invalid/passkey/item.torrent",
                  "info": { "title": "U2 item", "source_work_id": "u2-100" }
                }
              ]
            }
            """;
        using var u2Response = await app.Client.PostAsync(
            "/api/v1/ingest",
            new StringContent(u2Payload, Encoding.UTF8, "application/json"));
        using var u2Json = JsonDocument.Parse(await u2Response.Content.ReadAsStreamAsync());
        Assert.Equal("pt", u2Json.RootElement.GetProperty("items")[0].GetProperty("downloader_id").GetString());
    }

    [Fact]
    public async Task StagingFailureIsPerItemAndDoesNotEchoSecretUrl()
    {
        await using var app = await RunningApp.StartAsync(stagingService: new RejectingStagingService());
        const string payload = """
            {
              "source": "mikan",
              "data": [
                {
                  "torrent": "https://mikanani.me/private-passkey/file.torrent?token=secret",
                  "info": { "title": "Episode 1", "mikanid": 3951, "bgmid": 547888 }
                }
              ]
            }
            """;

        using var response = await app.Client.PostAsync(
            "/api/v1/ingest",
            new StringContent(payload, Encoding.UTF8, "application/json"));
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);

        Assert.Equal(0, json.RootElement.GetProperty("accepted_count").GetInt32());
        Assert.Equal("rejected", json.RootElement.GetProperty("items")[0].GetProperty("status").GetString());
        Assert.Contains("HostNotAllowed", json.RootElement.GetProperty("items")[0].GetProperty("errors")[0].GetString());
        Assert.DoesNotContain("private-passkey", body, StringComparison.Ordinal);
        Assert.DoesNotContain("token=secret", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DownloadListReturnsCanonicalSnapshotWithoutSecretPaths()
    {
        await using var app = await RunningApp.StartAsync();
        const string payload = """
            {
              "source": "mikan",
              "data": [{
                "torrent": "https://mikanani.me/private-passkey/file.torrent?token=secret",
                "info": { "title": "Episode 1", "mikanid": 3951, "bgmid": 547888 }
              }]
            }
            """;
        using var ingestResponse = await app.Client.PostAsync(
            "/api/v1/ingest",
            new StringContent(payload, Encoding.UTF8, "application/json"));
        using var ingestJson = JsonDocument.Parse(await ingestResponse.Content.ReadAsStreamAsync());
        var hash = ingestJson.RootElement.GetProperty("items")[0].GetProperty("info_hash").GetString()!;
        var tasks = app.App.Services.GetRequiredService<IngestTaskStore>();
        var claim = Assert.IsType<ClaimedStagedTorrentRecord>(await tasks.TryClaimNextStagedAsync(
            DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(1)));
        await tasks.CompleteDispatchAsync(
            claim,
            new DownloadTaskSnapshot(hash, "Episode", DownloadTaskState.Waiting, 0, 0, 100, 0, null),
            Path.Combine(app.RootPath, "download", "bt"),
            Path.Combine(app.RootPath, "save"),
            DateTimeOffset.UtcNow);
        var database = app.App.Services.GetRequiredService<AnimeGoNet.Data.Sqlite.AnimeGoSqliteDatabase>();
        await using (var connection = await database.OpenConnectionAsync())
        await using (var ready = connection.CreateCommand())
        {
            ready.CommandText = """
                UPDATE download_jobs SET preparation_state = 'completed' WHERE task_id = $task_id;
                UPDATE ingest_tasks SET status = 'download_queued' WHERE id = $task_id;
                """;
            ready.Parameters.AddWithValue("$task_id", claim.TaskId);
            Assert.Equal(2, await ready.ExecuteNonQueryAsync());
        }
        await app.App.Services.GetRequiredService<DownloadJobStore>().ApplyInstanceSnapshotAsync(
            "bt",
            [new DownloadTaskSnapshot(hash, "Episode", DownloadTaskState.Downloading, 0.4, 40, 100, 8, 7, 2, 4)],
            DateTimeOffset.UtcNow);

        using var response = await app.Client.GetAsync("/api/v1/downloads");
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);

        var item = json.RootElement.GetProperty("items")[0];
        Assert.Equal("Episode 1", item.GetProperty("title").GetString());
        Assert.Equal("bt", item.GetProperty("downloader_id").GetString());
        Assert.Equal("downloading", item.GetProperty("state").GetString());
        Assert.Equal(0.4, item.GetProperty("progress").GetDouble());
        Assert.Equal(2, item.GetProperty("seeds").GetInt32());
        Assert.Equal(4, item.GetProperty("peers").GetInt32());
        Assert.False(item.GetProperty("is_stale").GetBoolean());
        Assert.DoesNotContain("private-passkey", body, StringComparison.Ordinal);
        Assert.DoesNotContain("token=secret", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LegacyDownloadManagerUsesSameMikanRouteAndEnvelope()
    {
        await using var app = await RunningApp.StartAsync();
        const string payload = """
            {
              "source": "mikan",
              "data": [
                {
                  "torrent": "https://tracker.invalid/passkey/legacy.torrent",
                  "info": { "name": "Legacy episode", "url": "https://mikanani.me/Home/Bangumi/3951" }
                }
              ]
            }
            """;

        using var response = await app.Client.PostAsync(
            "/api/download/manager",
            new StringContent(payload, Encoding.UTF8, "application/json"));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(200, json.RootElement.GetProperty("code").GetInt32());
        Assert.Equal("开始处理1个下载项", json.RootElement.GetProperty("msg").GetString());
        var data = json.RootElement.GetProperty("data");
        Assert.Equal("bt", data.GetProperty("items")[0].GetProperty("downloader_id").GetString());
        Assert.Equal(1, data.GetProperty("accepted_count").GetInt32());
    }

    private sealed class RejectingStagingService : ITorrentStagingService
    {
        public Task<StagedTorrent> StageAsync(
            Uri secretUrl,
            TorrentSourcePolicy sourcePolicy,
            CancellationToken cancellationToken = default) =>
            throw new TorrentStagingException(
                TorrentStagingFailureCode.HostNotAllowed,
                "Torrent host is not allowed by the source profile.");

        public Task<bool> DeleteAsync(string stagingFileName, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public FileStream OpenRead(string stagingFileName) => throw new FileNotFoundException();

        public Task<int> CleanupExpiredAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
    }
}
