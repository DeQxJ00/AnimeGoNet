using System.Net;
using System.Text;
using AnimeGoNet.App.Downloads;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Downloads;

namespace AnimeGoNet.App.Tests.Downloads;

public sealed class QbittorrentClientTests
{
    [Fact]
    public void RegistryKeepsNamedInstancesIsolated()
    {
        using var registry = new QbittorrentClientRegistry(AnimeGoDefaults.CreateDocker());

        Assert.Equal(["bt", "pt"], registry.InstanceIds.Order(StringComparer.Ordinal).ToArray());
        Assert.NotSame(registry.GetRequired("bt"), registry.GetRequired("pt"));
        Assert.Throws<KeyNotFoundException>(() => registry.GetRequired("missing"));
    }

    [Fact]
    public async Task LoginUsesOfficialFormAndExactReferer()
    {
        using var handler = new RecordingHandler(_ => Text("Ok."));
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        await client.ConnectAsync();

        var request = Assert.Single(handler.Requests);
        Assert.Equal("/api/v2/auth/login", request.Path);
        Assert.Equal("http://qb.invalid:8080/", request.Referrer);
        Assert.Contains("username=admin", request.Body, StringComparison.Ordinal);
        Assert.Contains("password=secret", request.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoginRejectsQbittorrentFailureBody()
    {
        using var handler = new RecordingHandler(_ => Text("Fails."));
        using var httpClient = new HttpClient(handler);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateClient(httpClient).ConnectAsync());

        Assert.Contains("authentication failed", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoginAcceptsQbittorrent52NoContentResponse()
    {
        using var handler = new RecordingHandler(
            _ => new HttpResponseMessage(HttpStatusCode.NoContent));
        using var httpClient = new HttpClient(handler);

        await CreateClient(httpClient).ConnectAsync();

        Assert.Equal("/api/v2/auth/login", Assert.Single(handler.Requests).Path);
    }

    [Fact]
    public async Task ListUsesSourceGeneratedJsonAndCanonicalState()
    {
        const string json = """
            [{"hash":"abc","name":"Episode","state":"downloading","progress":0.25,"downloaded":25,"size":100,"dlspeed":10,"eta":8,"seeding_time":123}]
            """;
        using var handler = new RecordingHandler(_ => Text(json, "application/json"));
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        var item = Assert.Single(await client.ListAsync());

        Assert.Equal(DownloadTaskState.Downloading, item.State);
        Assert.Equal(25, item.DownloadedBytes);
        Assert.Equal(8, item.EtaSeconds);
        Assert.Equal(123, item.SeedingTimeSeconds);
        Assert.Equal("/api/v2/torrents/info", Assert.Single(handler.Requests).Path);
    }

    [Fact]
    public async Task DiagnosticsReadVersionAndDefaultSavePath()
    {
        using var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/v2/app/version" => Text("v5.2.3\n"),
            "/api/v2/app/defaultSavePath" => Text("/downloads/complete\n"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        var version = await client.GetVersionAsync();
        var defaultSavePath = await client.GetDefaultSavePathAsync();

        Assert.Equal("v5.2.3", version);
        Assert.Equal("/downloads/complete", defaultSavePath);
        Assert.Collection(
            handler.Requests,
            request => Assert.Equal("/api/v2/app/version", request.Path),
            request => Assert.Equal("/api/v2/app/defaultSavePath", request.Path));
    }

    [Fact]
    public async Task AddStartsStoppedAndSendsOnlyTorrentBytesToQbittorrent()
    {
        using var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);
        await using var torrent = new MemoryStream([1, 2, 3, 4]);

        await client.AddTorrentAsync(new AddTorrentCommand(
            torrent,
            "item.torrent",
            "/download/incomplete/bt",
            "Episode",
            "animegonet",
            ["mikan", "move"],
            SeedingTimeMinutes: 120));

        var request = Assert.Single(handler.Requests);
        Assert.Equal("/api/v2/torrents/add", request.Path);
        Assert.StartsWith("multipart/form-data", request.ContentType, StringComparison.Ordinal);
        Assert.Contains("name=torrents", request.Body, StringComparison.Ordinal);
        Assert.Contains("/download/incomplete/bt", request.Body, StringComparison.Ordinal);
        Assert.Contains("name=stopped", request.Body, StringComparison.Ordinal);
        Assert.Contains("name=paused", request.Body, StringComparison.Ordinal);
        Assert.Contains("true", request.Body, StringComparison.Ordinal);
        Assert.Contains("name=seedingTimeLimit", request.Body, StringComparison.Ordinal);
        Assert.Contains("120", request.Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-2)]
    [InlineData(5_256_001)]
    public async Task AddRejectsInvalidSeedingTimeBeforeHttp(int minutes)
    {
        using var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);
        await using var torrent = new MemoryStream([1, 2, 3]);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.AddTorrentAsync(
            new AddTorrentCommand(
                torrent, "item.torrent", "/download/incomplete/bt", null, "animegonet", [],
                SeedingTimeMinutes: minutes)));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ListsFilesWithStableIndexesPathsAndPriorities()
    {
        const string json = """
            [
              {"index":3,"name":"Show\\EP01.mkv","size":100,"progress":0.25,"priority":1},
              {"index":7,"name":"Show/EP01.zh-Hans.ass","size":5,"progress":1.0,"priority":0}
            ]
            """;
        using var handler = new RecordingHandler(_ => Text(json, "application/json"));
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);
        var hash = new string('a', 40);

        var files = await client.ListFilesAsync(hash);

        Assert.Collection(
            files,
            file => Assert.Equal(new DownloadFileSnapshot(3, "Show/EP01.mkv", 100, 0.25, 1), file),
            file =>
            {
                Assert.Equal(new DownloadFileSnapshot(7, "Show/EP01.zh-Hans.ass", 5, 1, 0), file);
                Assert.False(file.Wanted);
            });
        var request = Assert.Single(handler.Requests);
        Assert.Equal("/api/v2/torrents/files", request.Path);
        Assert.Equal($"?hash={hash}", request.Query);
    }

    [Fact]
    public async Task SetsFilePriorityWithExplicitIndexes()
    {
        using var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);
        var hash = new string('b', 40);

        await client.SetFilePriorityAsync(hash, [7, 3], 0);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("/api/v2/torrents/filePrio", request.Path);
        Assert.Equal($"hash={hash}&id=7%7C3&priority=0", request.Body);
    }

    [Fact]
    public async Task AddsNormalizedTagsWithOfficialTorrentEndpoint()
    {
        using var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);
        var firstHash = new string('a', 40);
        var secondHash = new string('b', 64);

        await client.AddTagsAsync(
            [firstHash, secondHash],
            [" 2026年4月新番 ", "星期一"]);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("/api/v2/torrents/addTags", request.Path);
        Assert.Equal(
            $"hashes={firstHash}%7C{secondHash}&tags=2026%E5%B9%B44%E6%9C%88%E6%96%B0%E7%95%AA%2C%E6%98%9F%E6%9C%9F%E4%B8%80",
            request.Body);
    }

    [Fact]
    public async Task AddTagsRejectsUnsafeIdentityAndEmptyTagsBeforeHttp()
    {
        using var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.AddTagsAsync(["not-a-hash"], ["tag"]));
        await Assert.ThrowsAsync<ArgumentException>(
            () => client.AddTagsAsync([new string('a', 40)], []));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task FileOperationsRejectUnsafeIdentityBeforeHttp()
    {
        using var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        await Assert.ThrowsAsync<ArgumentException>(() => client.ListFilesAsync("not-a-hash"));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.SetFilePriorityAsync(new string('a', 40), [1, 1], 0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            client.SetFilePriorityAsync(new string('a', 40), [1], 8));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task StopStartAndDeleteUseHashForms()
    {
        using var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        await client.PauseAsync(["a", "b"]);
        await client.ResumeAsync(["a"]);
        await client.DeleteAsync(["a"], deleteFiles: false);

        Assert.Collection(
            handler.Requests,
            request => Assert.Equal(("/api/v2/torrents/stop", "hashes=a%7Cb"), (request.Path, request.Body)),
            request => Assert.Equal(("/api/v2/torrents/start", "hashes=a"), (request.Path, request.Body)),
            request => Assert.Equal(("/api/v2/torrents/delete", "hashes=a&deleteFiles=false"), (request.Path, request.Body)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Qb4FallsBackAndRemembersControlEndpoints(bool pauseFirst)
    {
        using var handler = new RecordingHandler(request =>
            new HttpResponseMessage(request.RequestUri!.AbsolutePath is
                "/api/v2/torrents/start" or "/api/v2/torrents/stop"
                    ? HttpStatusCode.NotFound : HttpStatusCode.OK));
        using var http = new HttpClient(handler);
        var client = CreateClient(http);
        if (pauseFirst) await client.PauseAsync(["a", "b"]);
        else await client.ResumeAsync(["a", "b"]);
        await client.PauseAsync(["a", "b"]);
        await client.ResumeAsync(["a", "b"]);
        Assert.Equal(new[]
        {
            pauseFirst ? "/api/v2/torrents/stop" : "/api/v2/torrents/start",
            pauseFirst ? "/api/v2/torrents/pause" : "/api/v2/torrents/resume",
            "/api/v2/torrents/pause", "/api/v2/torrents/resume",
        }, handler.Requests.Select(r => r.Path));
        Assert.All(handler.Requests, r =>
        {
            Assert.Equal(HttpMethod.Post, r.Method);
            Assert.Equal("hashes=a%7Cb", r.Body);
        });
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task ControlFailuresDoNotTriggerLegacyFallback(HttpStatusCode status)
    {
        using var handler = new RecordingHandler(_ => new HttpResponseMessage(status));
        using var http = new HttpClient(handler);
        var client = CreateClient(http);
        Assert.Equal(status, (await Assert.ThrowsAsync<HttpRequestException>(
            () => client.PauseAsync(["a"]))).StatusCode);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task FailedLegacyEndpointDoesNotLatchLegacyMode()
    {
        using var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var http = new HttpClient(handler);
        var client = CreateClient(http);
        await Assert.ThrowsAsync<HttpRequestException>(() => client.PauseAsync(["a"]));
        await Assert.ThrowsAsync<HttpRequestException>(() => client.ResumeAsync(["a"]));
        Assert.Equal(["/api/v2/torrents/stop", "/api/v2/torrents/pause",
            "/api/v2/torrents/start", "/api/v2/torrents/resume"],
            handler.Requests.Select(r => r.Path));
    }

    [Fact]
    public async Task TransportFailureAndCancellationNeverReplayControl()
    {
        using var failed = new RecordingHandler(_ => throw new HttpRequestException("connection reset"));
        using var canceled = new RecordingHandler(_ => throw new TaskCanceledException());
        using var failedHttp = new HttpClient(failed);
        using var canceledHttp = new HttpClient(canceled);
        await Assert.ThrowsAsync<HttpRequestException>(() => CreateClient(failedHttp).PauseAsync(["a"]));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateClient(canceledHttp).ResumeAsync(["a"]));
        Assert.Single(failed.Requests);
        Assert.Single(canceled.Requests);
    }

    [Fact]
    public async Task LegacyDetectionIsIsolatedBetweenInstances()
    {
        using var oldHandler = new RecordingHandler(r => new HttpResponseMessage(
            r.RequestUri!.AbsolutePath.EndsWith("/stop", StringComparison.Ordinal)
                ? HttpStatusCode.NotFound : HttpStatusCode.OK));
        using var modernHandler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var oldHttp = new HttpClient(oldHandler);
        using var modernHttp = new HttpClient(modernHandler);
        await CreateClient(oldHttp).PauseAsync(["a"]);
        await CreateClient(modernHttp).PauseAsync(["a"]);
        Assert.Equal("/api/v2/torrents/stop", Assert.Single(modernHandler.Requests).Path);
    }

    private static QbittorrentClient CreateClient(HttpClient httpClient) => new(
        httpClient,
        new QbittorrentInstanceOptions
        {
            BaseUrl = new Uri("http://qb.invalid:8080/"),
            Username = "admin",
            Password = "secret",
            DownloadPath = "/download/incomplete/bt",
        });

    private static HttpResponseMessage Text(string value, string mediaType = "text/plain") => new(HttpStatusCode.OK)
    {
        Content = new StringContent(value, Encoding.UTF8, mediaType),
    };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri!.AbsolutePath,
                request.RequestUri.Query,
                body,
                request.Content?.Headers.ContentType?.ToString() ?? string.Empty,
                request.Headers.Referrer?.AbsoluteUri));
            return responder(request);
        }
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        string Path,
        string Query,
        string Body,
        string ContentType,
        string? Referrer);
}
