using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using AnimeGoNet.App.Torrents;
using AnimeGoNet.Data.Cache;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AnimeGoNet.App.Tests.Api;

public sealed class LegacyResponseFieldGoldenTests
{
    private const string FeedXml = """
        <rss><channel><link>https://mikanani.me/RSS?bangumiId=3951</link>
          <item><title>Golden Show [03] [1080p]</title><link>https://mikanani.me/Home/Episode/golden</link>
            <enclosure type="application/x-bittorrent" length="42" url="https://mikanani.me/Download/golden.torrent" /></item>
        </channel></rss>
        """;

    [Fact]
    public async Task EveryPinnedUpstreamOperationMatchesItsExactResponseFieldGolden()
    {
        using JsonDocument goldenDocument = await ReadGoldenAsync();
        JsonElement golden = goldenDocument.RootElement;
        Assert.Equal(UpstreamOperations(), golden.EnumerateObject()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray());

        await using (var app = await RunningApp.StartAsync())
        {
            var store = app.App.Services.GetRequiredService<SqliteJsonCacheStore>();
            await store.PutJsonAsync(
                "bolt",
                "golden",
                "entry",
                """{"value":7}""",
                TimeSpan.FromMinutes(10),
                DateTimeOffset.UtcNow);

            using JsonDocument ping = await GetAsync(app, "/ping");
            AssertSuccessEnvelope(golden, "GET /ping", ping.RootElement);
            AssertFields(golden, "GET /ping", "data", ping.RootElement.GetProperty("data"));

            using JsonDocument sha = await GetAsync(app, "/sha256?access_key=golden");
            AssertSuccessEnvelope(golden, "GET /sha256", sha.RootElement);
            AssertKind(golden, "GET /sha256", sha.RootElement.GetProperty("data"));

            using JsonDocument buckets = await GetAsync(app, "/api/bolt?type=bucket");
            AssertSuccessEnvelope(golden, "GET /api/bolt", buckets.RootElement);
            AssertFields(golden, "GET /api/bolt", "data_bucket", buckets.RootElement.GetProperty("data"));

            using JsonDocument keys = await GetAsync(
                app,
                "/api/bolt?type=key&bucket=golden");
            AssertSuccessEnvelope(golden, "GET /api/bolt", keys.RootElement);
            AssertFields(golden, "GET /api/bolt", "data_key", keys.RootElement.GetProperty("data"));

            using JsonDocument value = await GetAsync(
                app,
                "/api/bolt/value?bucket=golden&key=entry");
            AssertSuccessEnvelope(golden, "GET /api/bolt/value", value.RootElement);
            AssertFields(golden, "GET /api/bolt/value", "data", value.RootElement.GetProperty("data"));

            using JsonDocument deleted = await SendAsync(
                app,
                HttpMethod.Delete,
                "/api/bolt/value?bucket=golden&key=entry");
            AssertSuccessEnvelope(golden, "DELETE /api/bolt/value", deleted.RootElement);
            AssertKind(golden, "DELETE /api/bolt/value", deleted.RootElement.GetProperty("data"));

            using JsonDocument raw = await GetAsync(app, "/api/config?key=raw");
            AssertSuccessEnvelope(golden, "GET /api/config", raw.RootElement);
            AssertKind(golden, "GET /api/config", raw.RootElement.GetProperty("data"));
            string rawConfiguration = raw.RootElement.GetProperty("data").GetString()!;
            using JsonDocument updated = await SendAsync(
                app,
                HttpMethod.Put,
                "/api/config?backup=false",
                JsonSerializer.Serialize(new { key = "raw", config_raw = rawConfiguration }));
            AssertSuccessEnvelope(golden, "PUT /api/config", updated.RootElement);
            AssertKind(golden, "PUT /api/config", updated.RootElement.GetProperty("data"));

            const string filterJson = """
                {"Filiter0":{},"Filiter1":{},"Filiter2":{},"Filiter3":{},"Filiter4":{}}
                """;
            string pluginBody = JsonSerializer.Serialize(new
            {
                name = "filter/mikan_tool.py",
                data = Convert.ToBase64String(Encoding.UTF8.GetBytes(filterJson)),
            });
            using JsonDocument pluginPost = await SendAsync(
                app,
                HttpMethod.Post,
                "/api/plugin/config",
                pluginBody);
            AssertSuccessEnvelope(golden, "POST /api/plugin/config", pluginPost.RootElement);
            AssertFields(
                golden,
                "POST /api/plugin/config",
                "data",
                pluginPost.RootElement.GetProperty("data"));

            using JsonDocument pluginGet = await GetAsync(
                app,
                "/api/plugin/config?name=filter%2Fmikan_tool.py");
            AssertSuccessEnvelope(golden, "GET /api/plugin/config", pluginGet.RootElement);
            AssertFields(
                golden,
                "GET /api/plugin/config",
                "data",
                pluginGet.RootElement.GetProperty("data"));

            const string managerBody = """
                {
                  "source":"mikan",
                  "data":[{
                    "torrent":"https://mikanani.me/Download/manager-golden.torrent",
                    "info":{"name":"Manager Golden","url":"https://mikanani.me/Home/Bangumi/3951"}
                  }]
                }
                """;
            using JsonDocument manager = await SendAsync(
                app,
                HttpMethod.Post,
                "/api/download/manager",
                managerBody);
            AssertSuccessEnvelope(golden, "POST /api/download/manager", manager.RootElement);
            JsonElement managerData = manager.RootElement.GetProperty("data");
            AssertFields(golden, "POST /api/download/manager", "data", managerData);
            AssertFields(
                golden,
                "POST /api/download/manager",
                "item",
                managerData.GetProperty("items")[0]);

            using JsonDocument failure = await GetAsync(app, "/api/config?key=unsupported");
            Assert.Equal(300, failure.RootElement.GetProperty("code").GetInt32());
            AssertFields(golden, "GET /api/config", "root", failure.RootElement);
            Assert.Equal(JsonValueKind.Null, failure.RootElement.GetProperty("data").ValueKind);
        }

        var transport = new StaticTransport();
        await using (var app = await RunningApp.StartAsync(
            rssDnsResolver: new PublicDnsResolver(),
            rssHttpTransport: transport))
        {
            const string rssBody = """
                {"source":"mikan","rss":{"url":"https://mikanani.me/RSS?bangumiId=3951"}}
                """;
            using JsonDocument rss = await SendAsync(app, HttpMethod.Post, "/api/rss", rssBody);
            AssertSuccessEnvelope(golden, "POST /api/rss", rss.RootElement);
            JsonElement rssData = rss.RootElement.GetProperty("data");
            AssertFields(golden, "POST /api/rss", "data", rssData);
            AssertFields(golden, "POST /api/rss", "item", rssData.GetProperty("items")[0]);
        }
    }

    [Fact]
    public async Task WebSocketLogAndControlFramesMatchTheirExactFieldGolden()
    {
        using JsonDocument goldenDocument = await ReadGoldenAsync();
        JsonElement golden = goldenDocument.RootElement;
        await using var app = await RunningApp.StartAsync();
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(WebSocketUri(app), CancellationToken.None);
        var logger = app.App.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("AnimeGoNet.Tests.LegacyGolden");
        logger.Log(
            LogLevel.Information,
            default,
            "legacy-response-golden-marker",
            exception: null,
            static (state, _) => state);

        string frame = await ReceiveUntilAsync(
            socket,
            value => value.Contains("legacy-response-golden-marker", StringComparison.Ordinal));
        string header = frame.Split("\n\n", 2, StringSplitOptions.None)[0];
        using JsonDocument headerJson = JsonDocument.Parse(header);
        AssertFields(
            golden,
            "GET /websocket/log",
            "frame_header",
            headerJson.RootElement);

        await SendAsync(socket, """{"action":"invalid"}""");
        string control = await ReceiveUntilAsync(
            socket,
            value => value.Contains("unknown_action", StringComparison.Ordinal));
        using JsonDocument controlJson = JsonDocument.Parse(control);
        AssertFields(
            golden,
            "GET /websocket/log",
            "control",
            controlJson.RootElement);
        await socket.CloseAsync(
            WebSocketCloseStatus.NormalClosure,
            "golden complete",
            CancellationToken.None);
    }

    private static void AssertSuccessEnvelope(
        JsonElement golden,
        string operation,
        JsonElement response)
    {
        Assert.Equal(200, response.GetProperty("code").GetInt32());
        Assert.Equal(JsonValueKind.String, response.GetProperty("msg").ValueKind);
        AssertFields(golden, operation, "root", response);
    }

    private static void AssertFields(
        JsonElement golden,
        string operation,
        string section,
        JsonElement actual)
    {
        string[] expected = golden.GetProperty(operation).GetProperty(section)
            .EnumerateArray().Select(value => value.GetString()!).ToArray();
        string[] fields = actual.EnumerateObject().Select(property => property.Name)
            .Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(expected, fields);
    }

    private static void AssertKind(JsonElement golden, string operation, JsonElement actual)
    {
        string expected = golden.GetProperty(operation).GetProperty("data_kind").GetString()!;
        string kind = actual.ValueKind switch
        {
            JsonValueKind.Null => "null",
            JsonValueKind.String => "string",
            _ => actual.ValueKind.ToString().ToLowerInvariant(),
        };
        Assert.Equal(expected, kind);
    }

    private static string[] UpstreamOperations()
    {
        string root = FindRepositoryRoot();
        using JsonDocument openApi = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            root,
            "docs",
            "baseline",
            "openapi-upstream.json")));
        var operations = new List<string>();
        foreach (JsonProperty path in openApi.RootElement.GetProperty("paths").EnumerateObject())
        {
            foreach (JsonProperty method in path.Value.EnumerateObject())
            {
                string normalized = method.Name.ToUpperInvariant();
                if (normalized is "GET" or "POST" or "PUT" or "DELETE" or "PATCH")
                {
                    operations.Add($"{normalized} {path.Name}");
                }
            }
        }
        return operations.Order(StringComparer.Ordinal).ToArray();
    }

    private static async Task<JsonDocument> ReadGoldenAsync() => JsonDocument.Parse(
        await File.ReadAllTextAsync(Path.Combine(
            FindRepositoryRoot(),
            "tests",
            "AnimeGoNet.App.Tests",
            "Api",
            "Fixtures",
            "legacy-response-fields.golden.json")));

    private static async Task<JsonDocument> GetAsync(RunningApp app, string path)
    {
        using HttpResponseMessage response = await app.Client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
    }

    private static async Task<JsonDocument> SendAsync(
        RunningApp app,
        HttpMethod method,
        string path,
        string? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }
        using HttpResponseMessage response = await app.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
    }

    private static Uri WebSocketUri(RunningApp app)
    {
        var builder = new UriBuilder(app.Client.BaseAddress!)
        {
            Scheme = "ws",
            Path = "/websocket/log",
        };
        return builder.Uri;
    }

    private static async Task SendAsync(ClientWebSocket socket, string payload) =>
        await socket.SendAsync(
            Encoding.UTF8.GetBytes(payload),
            WebSocketMessageType.Text,
            endOfMessage: true,
            CancellationToken.None);

    private static async Task<string> ReceiveUntilAsync(
        ClientWebSocket socket,
        Func<string, bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        for (var attempt = 0; attempt < 20; attempt++)
        {
            string frame = await ReceiveTextAsync(socket, timeout.Token);
            if (predicate(frame))
            {
                return frame;
            }
        }
        throw new Xunit.Sdk.XunitException("Expected WebSocket frame was not received.");
    }

    private static async Task<string> ReceiveTextAsync(
        ClientWebSocket socket,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[1024];
        using var payload = new MemoryStream();
        ValueWebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer.AsMemory(), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new Xunit.Sdk.XunitException(
                    "WebSocket closed before the expected text frame.");
            }
            Assert.Equal(WebSocketMessageType.Text, result.MessageType);
            payload.Write(buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);
        return Encoding.UTF8.GetString(payload.GetBuffer(), 0, (int)payload.Length);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AnimeGoNet.slnx")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate AnimeGoNet.slnx.");
    }

    private sealed class PublicDnsResolver : ITorrentDnsResolver
    {
        public ValueTask<IReadOnlyList<IPAddress>> ResolveAsync(
            string host,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<IPAddress>>([IPAddress.Parse("1.1.1.1")]);
    }

    private sealed class StaticTransport : ITorrentHttpTransport
    {
        public ValueTask<TorrentHttpResponse> SendAsync(
            Uri uri,
            IReadOnlyList<IPAddress> validatedAddresses,
            CancellationToken cancellationToken)
        {
            string body = uri.AbsolutePath.StartsWith(
                "/Home/Bangumi/",
                StringComparison.OrdinalIgnoreCase)
                ? """
                    <p class="bangumi-info">
                      <a href="https://bgm.tv/subject/547888">Bangumi</a>
                    </p>
                    """
                : FeedXml;
            byte[] bytes = Encoding.UTF8.GetBytes(body);
            return ValueTask.FromResult(new TorrentHttpResponse(
                HttpStatusCode.OK,
                null,
                bytes.Length,
                new MemoryStream(bytes, writable: false)));
        }
    }
}
