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
            [{"hash":"abc","name":"Episode","state":"downloading","progress":0.25,"downloaded":25,"size":100,"dlspeed":10,"eta":8}]
            """;
        using var handler = new RecordingHandler(_ => Text(json, "application/json"));
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        var item = Assert.Single(await client.ListAsync());

        Assert.Equal(DownloadTaskState.Downloading, item.State);
        Assert.Equal(25, item.DownloadedBytes);
        Assert.Equal(8, item.EtaSeconds);
        Assert.Equal("/api/v2/torrents/info", Assert.Single(handler.Requests).Path);
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
            ["mikan", "move"]));

        var request = Assert.Single(handler.Requests);
        Assert.Equal("/api/v2/torrents/add", request.Path);
        Assert.StartsWith("multipart/form-data", request.ContentType, StringComparison.Ordinal);
        Assert.Contains("name=torrents", request.Body, StringComparison.Ordinal);
        Assert.Contains("/download/incomplete/bt", request.Body, StringComparison.Ordinal);
        Assert.Contains("name=stopped", request.Body, StringComparison.Ordinal);
        Assert.Contains("true", request.Body, StringComparison.Ordinal);
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
