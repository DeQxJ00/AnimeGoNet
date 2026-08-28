using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using AnimeGoNet.App.Metadata;
using AnimeGoNet.App.Torrents;
using AnimeGoNet.Core.Torrents;
using AnimeGoNet.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeGoNet.App.Tests.Api;

public sealed class AiMetadataTestApiTests
{
    private const string EpisodeId = "0123456789abcdef0123456789abcdef01234567";

    [Fact]
    public async Task ExposesValidatedTesterPromptAndConfiguredBootstrap()
    {
        await using var app = await RunningApp.StartAsync(configure: options => options with
        {
            Metadata = options.Metadata with
            {
                Ai = options.Metadata.Ai with
                {
                    ApiKey = "configured-ai-test-key",
                    Model = "configured-ai-test-model",
                    ApiMode = AnimeGoNet.Core.Configuration.AiApiMode.ChatCompletions,
                    WebSearchEnabled = false,
                },
            },
        });

        var prompt = await app.Client.GetFromJsonAsync<JsonElement>("/api/v1/ai-test/prompt");
        var bootstrap = await app.Client.GetFromJsonAsync<JsonElement>("/api/v1/ai-test/bootstrap");

        Assert.Equal("tmdb-ai-match-v17", prompt.GetProperty("prompt_version").GetString());
        Assert.Contains("{{SOURCE_TITLE_JSON}}", prompt.GetProperty("template").GetString(), StringComparison.Ordinal);
        Assert.Contains("{{OPTIONAL_BGM_ID_JSON}}", prompt.GetProperty("template").GetString(), StringComparison.Ordinal);
        Assert.Equal(
            prompt.GetProperty("default_template").GetString(),
            prompt.GetProperty("template").GetString());
        Assert.False(prompt.GetProperty("customized").GetBoolean());
        Assert.Equal(
            "configured-ai-test-key",
            bootstrap.GetProperty("defaults").GetProperty("api_key").GetString());
        Assert.Equal(
            "configured-ai-test-model",
            bootstrap.GetProperty("defaults").GetProperty("model").GetString());
        Assert.Equal(0, bootstrap.GetProperty("defaults").GetProperty("mode").GetInt32());
        Assert.True(bootstrap.GetProperty("defaults").GetProperty("web_search_enabled").GetBoolean());
        Assert.Equal(
            prompt.GetProperty("template").GetString(),
            bootstrap.GetProperty("prompt_template").GetString());
    }

    [Fact]
    public async Task TesterAndBackgroundWorkerExposeTheSameConfiguredProductionPrompt()
    {
        var configuredPrompt = AiMetadataPromptRenderer.LoadTemplate()
            .Replace("你是一个动画 TMDB 元数据匹配器。", "你是一个动画 TMDB 元数据匹配器。（共享配置验收）", StringComparison.Ordinal);
        await using var app = await RunningApp.StartAsync(configure: options => options with
        {
            Metadata = options.Metadata with
            {
                Ai = options.Metadata.Ai with { PromptTemplate = configuredPrompt },
            },
        });

        var prompt = await app.Client.GetFromJsonAsync<JsonElement>("/api/v1/ai-test/prompt");
        var bootstrap = await app.Client.GetFromJsonAsync<JsonElement>("/api/v1/ai-test/bootstrap");

        Assert.True(prompt.GetProperty("customized").GetBoolean());
        Assert.Equal(configuredPrompt, prompt.GetProperty("template").GetString());
        Assert.Equal(configuredPrompt, bootstrap.GetProperty("prompt_template").GetString());
    }

    [Fact]
    public async Task ResolvesMikanEpisodeForManualIngestWithoutStagingTorrent()
    {
        var staging = new ImportStagingService();
        var transport = new MikanImportTransport();
        await using var app = await RunningApp.StartAsync(
            stagingService: staging,
            rssDnsResolver: new PublicDnsResolver(),
            rssHttpTransport: transport);

        using var response = await app.Client.PostAsJsonAsync("/api/v1/ingest/mikan/resolve", new
        {
            source_profile_id = "mikan",
            episode_url = $"https://mikanime.tv/Home/Episode/{EpisodeId}",
        });
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("[Group] Imported Show - 06", json.GetProperty("title").GetString());
        Assert.Equal(EpisodeId, json.GetProperty("source_item_id").GetString());
        Assert.Equal("4028", json.GetProperty("source_work_id").GetString());
        Assert.Equal(4028, json.GetProperty("mikanid").GetInt32());
        Assert.Equal(123, json.GetProperty("groupid").GetInt32());
        Assert.Equal(590786, json.GetProperty("bgmid").GetInt32());
        Assert.Contains("passkey=secret", json.GetProperty("torrent_url").GetString(), StringComparison.Ordinal);
        Assert.Null(staging.LastUrl);
    }

    [Fact]
    public async Task UnmodifiedAnimeGoHelperResolvesEpisodeIdentityBeforeLegacyIngest()
    {
        var transport = new MikanImportTransport();
        await using var app = await RunningApp.StartAsync(
            rssDnsResolver: new PublicDnsResolver(),
            rssHttpTransport: transport);
        var title = "[LoliHouse] Heroine - 08 [1080p]";

        using var response = await app.Client.PostAsJsonAsync("/api/download/manager", new
        {
            source = "mikan",
            data = new[]
            {
                new
                {
                    torrent = $"https://mikanani.me/Download/20260813/{EpisodeId}.torrent",
                    info = new
                    {
                        name = title,
                        url = $"https://mikanani.me/Home/Episode/{EpisodeId}",
                    },
                },
            },
        });
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(200, json.GetProperty("code").GetInt32());
        Assert.Equal(1, json.GetProperty("data").GetProperty("accepted_count").GetInt32());

        var database = app.App.Services.GetRequiredService<AnimeGoSqliteDatabase>();
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT source_item_id, source_work_id, mikanid, groupid,
                   bangumi_subject_id, title, source_page_url,
                   source_published_at_raw, source_published_at
            FROM ingest_tasks
            LIMIT 1;
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(EpisodeId, reader.GetString(0));
        Assert.Equal("4028", reader.GetString(1));
        Assert.Equal(4028, reader.GetInt32(2));
        Assert.Equal(123, reader.GetInt32(3));
        Assert.Equal(590786, reader.GetInt32(4));
        Assert.Equal(title, reader.GetString(5));
        Assert.Equal($"https://mikanani.me/Home/Episode/{EpisodeId}", reader.GetString(6));
        Assert.StartsWith("2026-08-09T08:55:16.532", reader.GetString(7), StringComparison.Ordinal);
        Assert.StartsWith("2026-08-09T08:55:16.532", reader.GetString(8), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportsMikanIntoTrustedTesterSnapshotWithoutReturningPasskeyUrl()
    {
        var staging = new ImportStagingService();
        var transport = new MikanImportTransport();
        await using var app = await RunningApp.StartAsync(
            stagingService: staging,
            rssDnsResolver: new PublicDnsResolver(),
            rssHttpTransport: transport);

        using var response = await app.Client.PostAsJsonAsync("/api/v1/ai-test/mikan-import", new
        {
            episode_url = $"https://mikanime.tv/Home/Episode/{EpisodeId}",
            proxy_url = (string?)null,
        });
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.Equal("[Group] Imported Show - 06", json.GetProperty("title").GetString());
        Assert.Equal(4028, json.GetProperty("mikan_id").GetInt32());
        Assert.Equal(123, json.GetProperty("group_id").GetInt32());
        Assert.Equal(590786, json.GetProperty("bgmid").GetInt32());
        Assert.Equal(2, json.GetProperty("torrent_file_count").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("import_id").GetString()));
        Assert.False(json.TryGetProperty("torrent_url", out _));
        Assert.Equal("Imported Show/06.mkv", json.GetProperty("files")[0].GetProperty("name").GetString());
        Assert.Equal("Imported Show/06.mkv", json.GetProperty("file_episode_candidates")[0].GetProperty("name").GetString());
        Assert.NotNull(staging.LastUrl);

        using var repeatedResponse = await app.Client.PostAsJsonAsync("/api/v1/ai-test/mikan-import", new
        {
            episode_url = $"https://mikanime.tv/Home/Episode/{EpisodeId}",
            proxy_url = (string?)null,
        });

        Assert.Equal(HttpStatusCode.OK, repeatedResponse.StatusCode);
        Assert.Equal(
            1,
            transport.Requests.Count(uri => uri.AbsolutePath.StartsWith(
                "/Home/Episode/",
                StringComparison.Ordinal)));
        Assert.Equal(
            1,
            transport.Requests.Count(uri => uri.AbsolutePath.StartsWith(
                "/Home/Bangumi/",
                StringComparison.Ordinal)));
    }

    [Fact]
    public async Task TorrentImportReturnsServerIssuedIdAndRealFileCount()
    {
        await using var app = await RunningApp.StartAsync();
        var torrent = Encoding.ASCII.GetBytes("d4:infod6:lengthi1e4:name6:01.mkvee");

        using var response = await app.Client.PostAsJsonAsync("/api/v1/ai-test/torrent-import", new
        {
            data_base64 = Convert.ToBase64String(torrent),
        });
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.Equal(1, json.GetProperty("torrent_file_count").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("import_id").GetString()));
        Assert.Equal("01.mkv", json.GetProperty("files")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task InvalidStreamRequestUsesTesterNdjsonErrorEnvelope()
    {
        await using var app = await RunningApp.StartAsync();
        using var response = await app.Client.PostAsJsonAsync("/api/v1/ai-test/run-stream", new
        {
            base_url = "http://openai.test/",
            api_key = "key",
            model = "model",
            title = "Example",
            files_json = "[]",
            timeout_seconds = 600,
        });

        Assert.Equal("application/x-ndjson", response.Content.Headers.ContentType?.MediaType);
        var lines = (await response.Content.ReadAsStringAsync())
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var json = JsonDocument.Parse(lines[^1]).RootElement;
        Assert.Equal("error", json.GetProperty("type").GetString());
        Assert.Contains("at least one", json.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StreamsExactTesterProgressAndAddsProductionTmdbValidation()
    {
        const string modelJson = "{\"matched\":true,\"tmdb_id\":42,\"files\":[{\"name\":\"Example - 06.mkv\",\"matched\":true,\"season\":1,\"episode\":6,\"reason\":null}],\"reason\":null}";
        var providerJson = JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content = modelJson } } },
            usage = new { prompt_tokens = 7, completion_tokens = 4, total_tokens = 11 },
        });
        await using var provider = new OneShotApiServer(providerJson);
        await using var app = await RunningApp.StartAsync(tmdbClient: new ValidTmdbClient());

        using var response = await app.Client.PostAsJsonAsync("/api/v1/ai-test/run-stream", new
        {
            base_url = provider.BaseUrl.AbsoluteUri,
            api_key = "test-key",
            model = "test-model",
            mode = "chat-completions",
            reasoning_effort = "none",
            web_search_enabled = false,
            timeout_seconds = 30,
            proxy_url = "",
            title = "[Group] Example - 06",
            files_json = "[{\"name\":\"Example - 06.mkv\",\"size_bytes\":700000000}]",
            use_bangumi_pubdate_first = false,
            enable_tmdb_mcp = false,
            enable_bgm_mcp = false,
            enable_anidb_lookup = false,
            tmdb_mcp_url = "http://tmdb.mcp.test/mcp",
            bgm_mcp_url = "http://bgm.mcp.test/mcp",
            anidb_mapping_url_template = "https://example.test/{anidbid}.json",
            run_id = Guid.NewGuid().ToString(),
        });
        var envelopes = (await response.Content.ReadAsStringAsync())
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonDocument.Parse(line).RootElement.Clone())
            .ToArray();

        Assert.Contains(envelopes, item => item.GetProperty("type").GetString() == "progress"
            && item.GetProperty("progress").GetProperty("type").GetString() == "model-start");
        var result = envelopes.Single(item => item.GetProperty("type").GetString() == "result")
            .GetProperty("result");
        Assert.True(result.GetProperty("success").GetBoolean());
        Assert.True(result.GetProperty("result_json_valid").GetBoolean());
        Assert.True(result.GetProperty("production_validation").GetProperty("success").GetBoolean());
        Assert.Equal(11, result.GetProperty("usage").GetProperty("total_tokens").GetInt32());
        Assert.Contains("/v1/chat/completions", result.GetProperty("ai_api_requests")[0].GetProperty("endpoint").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("test-key", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsMikanImportFromUnconfiguredPrivateHostWithoutEchoingUrl()
    {
        await using var app = await RunningApp.StartAsync();
        var secretUrl = $"http://127.0.0.1/Home/Episode/{EpisodeId}?passkey=secret";

        using var response = await app.Client.PostAsJsonAsync("/api/v1/ai-test/mikan-import", new
        {
            episode_url = secretUrl,
            proxy_url = (string?)null,
        });
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Mikan Episode URL", body, StringComparison.Ordinal);
        Assert.DoesNotContain("passkey", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", body, StringComparison.Ordinal);
    }

    private sealed class PublicDnsResolver : ITorrentDnsResolver
    {
        public ValueTask<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<IPAddress>>([IPAddress.Parse("1.1.1.1")]);
    }

    private sealed class MikanImportTransport : ITorrentHttpTransport
    {
        public List<Uri> Requests { get; } = [];

        public ValueTask<TorrentHttpResponse> SendAsync(
            Uri uri,
            IReadOnlyList<IPAddress> validatedAddresses,
            CancellationToken cancellationToken) =>
            SendAsync(uri, validatedAddresses, new TorrentHttpRequestOptions(), cancellationToken);

        public ValueTask<TorrentHttpResponse> SendAsync(
            Uri uri,
            IReadOnlyList<IPAddress> validatedAddresses,
            TorrentHttpRequestOptions requestOptions,
            CancellationToken cancellationToken)
        {
            Requests.Add(uri);
            var body = uri.AbsolutePath switch
            {
                var path when path.StartsWith("/Home/Episode/", StringComparison.Ordinal) =>
                    "<a class=\"mikan-rss\" href=\"/RSS/Bangumi?bangumiId=4028&subgroupid=123\">RSS</a>",
                "/RSS/Bangumi" => $"""
                    <rss><channel><link>https://mikanime.tv/Home/Bangumi/4028</link><item>
                    <title>[Group] Imported Show - 06</title>
                    <link>https://mikanime.tv/Home/Episode/{EpisodeId}</link>
                    <enclosure url="https://mikanime.tv/Download/20260809/{EpisodeId}.torrent?passkey=secret" length="42" type="application/x-bittorrent" />
                    <torrent:pubDate xmlns:torrent="https://mikanime.tv/">2026-08-09T08:55:16.532</torrent:pubDate>
                    </item></channel></rss>
                    """,
                "/Home/Bangumi/4028" =>
                    "<p class=\"bangumi-info\"><a href=\"https://bgm.tv/subject/590786\">Bangumi</a></p>",
                _ => throw new InvalidOperationException($"Unexpected URL path: {uri.AbsolutePath}"),
            };
            return ValueTask.FromResult(new TorrentHttpResponse(
                HttpStatusCode.OK,
                null,
                Encoding.UTF8.GetByteCount(body),
                new MemoryStream(Encoding.UTF8.GetBytes(body))));
        }
    }

    private sealed class ImportStagingService : ITorrentStagingService
    {
        public Uri? LastUrl { get; private set; }

        public Task<StagedTorrent> StageAsync(
            Uri secretUrl,
            TorrentSourcePolicy sourcePolicy,
            CancellationToken cancellationToken = default)
        {
            LastUrl = secretUrl;
            var path = Path.Combine(Path.GetTempPath(), $"animegonet-ai-import-{Guid.NewGuid():N}.torrent");
            File.WriteAllBytes(path, [1]);
            return Task.FromResult(new StagedTorrent(
                path,
                new TorrentMetadata(
                    "Imported Show",
                    new string('a', 40),
                    700_000_123,
                    [
                        new TorrentFile("Imported Show/06.mkv", 700_000_000, false),
                        new TorrentFile("Imported Show/06.ass", 123, false),
                    ])));
        }

        public Task<bool> DeleteAsync(string stagingFileName, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public FileStream OpenRead(string stagingFileName) => throw new NotSupportedException();

        public Task<int> CleanupExpiredAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
    }

    private sealed class ValidTmdbClient : AnimeGoNet.Core.Metadata.ITmdbClient
    {
        private static readonly AnimeGoNet.Core.Metadata.TmdbSeries Series =
            new(42, "Example", "Example", new DateOnly(2026, 1, 1));

        public Task<IReadOnlyList<AnimeGoNet.Core.Metadata.TmdbSeries>> SearchSeriesAsync(
            string title,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AnimeGoNet.Core.Metadata.TmdbSeries>>([Series]);

        public Task<AnimeGoNet.Core.Metadata.TmdbSeries?> GetSeriesAsync(
            int seriesId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<AnimeGoNet.Core.Metadata.TmdbSeries?>(seriesId == 42 ? Series : null);

        public Task<AnimeGoNet.Core.Metadata.TmdbSeriesDetails?> GetSeriesDetailsAsync(
            int seriesId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<AnimeGoNet.Core.Metadata.TmdbSeriesDetails?>(seriesId == 42
                ? new(Series, [Season()])
                : null);

        public Task<AnimeGoNet.Core.Metadata.TmdbSeason?> GetSeasonAsync(
            int seriesId,
            int seasonNumber,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<AnimeGoNet.Core.Metadata.TmdbSeason?>(seriesId == 42 && seasonNumber == 1 ? Season() : null);

        public Task<AnimeGoNet.Core.Metadata.TmdbEpisode?> GetEpisodeAsync(
            int seriesId,
            int seasonNumber,
            int episodeNumber,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<AnimeGoNet.Core.Metadata.TmdbEpisode?>(seriesId == 42 && seasonNumber == 1 && episodeNumber == 6
                ? new(4206, 42, 1, 6, "Episode 6", new DateOnly(2026, 2, 6))
                : null);

        private static AnimeGoNet.Core.Metadata.TmdbSeason Season() =>
            new(421, 42, 1, "Season 1", new DateOnly(2026, 1, 1), 12);
    }

    private sealed class OneShotApiServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly Task _serveTask;

        public OneShotApiServer(string responseJson)
        {
            _listener.Start();
            BaseUrl = new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/");
            _serveTask = ServeAsync(responseJson);
        }

        public Uri BaseUrl { get; }

        public async ValueTask DisposeAsync()
        {
            _listener.Stop();
            await _serveTask.ConfigureAwait(false);
        }

        private async Task ServeAsync(string responseJson)
        {
            try
            {
                using TcpClient client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                await using NetworkStream stream = client.GetStream();
                var buffer = new byte[16 * 1024];
                var received = new MemoryStream();
                int contentLength = 0;
                while (true)
                {
                    int read = await stream.ReadAsync(buffer).ConfigureAwait(false);
                    if (read == 0) break;
                    received.Write(buffer, 0, read);
                    var text = Encoding.ASCII.GetString(received.ToArray());
                    int headerEnd = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                    if (headerEnd < 0) continue;
                    foreach (var line in text[..headerEnd].Split("\r\n"))
                    {
                        if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                            contentLength = int.Parse(line["Content-Length:".Length..].Trim(), System.Globalization.CultureInfo.InvariantCulture);
                    }
                    if (received.Length >= headerEnd + 4 + contentLength) break;
                }

                byte[] payload = Encoding.UTF8.GetBytes(responseJson);
                byte[] headers = Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(headers).ConfigureAwait(false);
                await stream.WriteAsync(payload).ConfigureAwait(false);
                await stream.FlushAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }
}
