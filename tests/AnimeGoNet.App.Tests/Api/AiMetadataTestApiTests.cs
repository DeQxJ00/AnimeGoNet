using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text;
using AnimeGoNet.App.Torrents;
using AnimeGoNet.Core.Torrents;
using AnimeGoNet.Core.Metadata;

namespace AnimeGoNet.App.Tests.Api;

public sealed class AiMetadataTestApiTests
{
    private const string EpisodeId = "0123456789abcdef0123456789abcdef01234567";

    [Fact]
    public async Task ExposesCurrentPromptAndUsesRequestLocalTemplateOverride()
    {
        var matcher = new CapturingMatcher();
        await using var app = await RunningApp.StartAsync(
            aiMetadataMatcher: matcher,
            tmdbClient: new ValidTmdbClient());

        var prompt = await app.Client.GetFromJsonAsync<JsonElement>("/api/v1/ai-test/prompt");
        Assert.Equal("tmdb-ai-match-v12", prompt.GetProperty("prompt_version").GetString());
        Assert.Contains("{{SOURCE_TITLE_JSON}}", prompt.GetProperty("template").GetString(), StringComparison.Ordinal);

        const string custom = "CUSTOM TEST {{SOURCE_TITLE_JSON}} {{FILES_JSON}}";
        using var response = await app.Client.PostAsJsonAsync("/api/v1/ai-test/run", new
        {
            title = "Example",
            files = new[] { new { name = "Example.mkv", size_bytes = 1 } },
            prompt_template = custom,
        });
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("CUSTOM TEST", json.GetProperty("rendered_prompt").GetString(), StringComparison.Ordinal);
        Assert.Equal(custom, matcher.LastInput?.PromptTemplateOverride);
    }

    [Fact]
    public async Task ImportsMikanEpisodeRssEvidenceAndTorrentFilesWithoutRunningAi()
    {
        var matcher = new CapturingMatcher();
        var staging = new ImportStagingService();
        await using var app = await RunningApp.StartAsync(
            aiMetadataMatcher: matcher,
            tmdbClient: new ValidTmdbClient(),
            stagingService: staging,
            rssDnsResolver: new PublicDnsResolver(),
            rssHttpTransport: new MikanImportTransport());

        using var response = await app.Client.PostAsJsonAsync("/api/v1/ai-test/mikan-import", new
        {
            episode_url = $"https://mikanime.tv/Home/Episode/{EpisodeId}",
        });
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("[Group] Imported Show - 06", json.GetProperty("title").GetString());
        Assert.Equal(4028, json.GetProperty("mikanid").GetInt32());
        Assert.Equal(123, json.GetProperty("groupid").GetInt32());
        Assert.Equal(590786, json.GetProperty("bgmid").GetInt32());
        Assert.Equal(2, json.GetProperty("torrent_file_count").GetInt32());
        Assert.Equal("Imported Show/06.mkv", json.GetProperty("files")[0].GetProperty("name").GetString());
        Assert.Contains("2026-08-09", json.GetProperty("published_at").GetString(), StringComparison.Ordinal);
        Assert.Null(matcher.LastInput);
        Assert.NotNull(staging.LastUrl);
    }

    [Fact]
    public async Task AppliesConditionalPromptAndReportsOnlyEffectiveFeatures()
    {
        var matcher = new CapturingMatcher();
        await using var app = await RunningApp.StartAsync(
            aiMetadataMatcher: matcher,
            tmdbClient: new ValidTmdbClient());

        using var response = await app.Client.PostAsJsonAsync("/api/v1/ai-test/run", new
        {
            title = "Example",
            files = new[] { new { name = "Example.mkv", size_bytes = 1 } },
            bgmid = 123,
            anidbid = 456,
            imdbid = "tt1234567",
            enable_tmdb_mcp = false,
            enable_bangumi_mcp = false,
            enable_anidb_lookup = false,
        });
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var features = json.GetProperty("effective_features");
        Assert.False(features.GetProperty("tmdb_mcp").GetBoolean());
        Assert.False(features.GetProperty("bangumi_mcp").GetBoolean());
        Assert.False(features.GetProperty("anidb_lookup").GetBoolean());
        Assert.False(features.GetProperty("imdb_lookup").GetBoolean());
        Assert.DoesNotContain("\"bgmid\"", json.GetProperty("rendered_prompt").GetString(), StringComparison.Ordinal);
        Assert.False(matcher.LastInput!.PromptFeaturesOverride!.TmdbMcp);
    }

    [Fact]
    public async Task RejectsMikanImportFromUnconfiguredPrivateHost()
    {
        await using var app = await RunningApp.StartAsync();

        using var response = await app.Client.PostAsJsonAsync("/api/v1/ai-test/mikan-import", new
        {
            episode_url = $"http://127.0.0.1/Home/Episode/{EpisodeId}",
        });
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("ai_test_mikan_episode_url_invalid", json.GetProperty("code").GetString());
    }

    [Fact]
    public async Task RunsProductionMatcherAndTmdbValidatorWithoutPersistingTask()
    {
        var matcher = new CapturingMatcher();
        await using var app = await RunningApp.StartAsync(
            aiMetadataMatcher: matcher,
            tmdbClient: new ValidTmdbClient());

        using var response = await app.Client.PostAsJsonAsync("/api/v1/ai-test/run", new
        {
            title = "[Group] Example - 06",
            files = new[] { new { name = "Example - 06.mkv", size_bytes = 700_000_000 } },
            bgmid = 123,
            torrent_file_count = 1,
            bgm_episode_candidate = 6,
            use_bangumi_pubdate_first = true,
            published_at = "2026-02-06T12:00:00+08:00",
            expected_tmdbid = 42,
            expected_season = 1,
        });
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(json.GetProperty("succeeded").GetBoolean());
        Assert.Equal("tmdb-ai-match-v12", json.GetProperty("prompt_version").GetString());
        Assert.Contains("[Group] Example - 06", json.GetProperty("rendered_prompt").GetString(), StringComparison.Ordinal);
        Assert.Equal("{\"matched\":true}", json.GetProperty("raw_output").GetString());
        Assert.Equal(42, json.GetProperty("validation").GetProperty("tmdbid").GetInt32());
        Assert.Equal(6, json.GetProperty("validation").GetProperty("files")[0].GetProperty("episode").GetInt32());
        Assert.Equal(11, json.GetProperty("usage").GetProperty("total_tokens").GetInt64());
        Assert.Equal("tmdb_validation", json.GetProperty("trace")[1].GetProperty("stage").GetString());
        Assert.Equal(123, matcher.LastInput?.BangumiSubjectId);

        using var tasks = await app.Client.GetAsync("/api/v1/metadata/tasks");
        var taskJson = await tasks.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, taskJson.GetProperty("total_items").GetInt32());
    }

    [Fact]
    public async Task RejectsMalformedFileRowsBeforeCallingMatcher()
    {
        var matcher = new CapturingMatcher();
        await using var app = await RunningApp.StartAsync(
            aiMetadataMatcher: matcher,
            tmdbClient: new ValidTmdbClient());

        using var response = await app.Client.PostAsJsonAsync("/api/v1/ai-test/run", new
        {
            title = "Example",
            files = new[] { new { name = "", size_bytes = -1 } },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(matcher.LastInput);
    }

    [Theory]
    [InlineData("file:///etc/passwd", null)]
    [InlineData(null, "socks5://127.0.0.1:1080")]
    public async Task RejectsUnsupportedTestModelOrProxyUrls(
        string? aiBaseUrl,
        string? proxyUrl)
    {
        var matcher = new CapturingMatcher();
        await using var app = await RunningApp.StartAsync(
            aiMetadataMatcher: matcher,
            tmdbClient: new ValidTmdbClient());

        using var response = await app.Client.PostAsJsonAsync("/api/v1/ai-test/run", new
        {
            title = "Example",
            files = new[] { new { name = "Example.mkv", size_bytes = 1 } },
            ai_base_url = aiBaseUrl,
            http_proxy_url = proxyUrl,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(matcher.LastInput);
    }

    [Fact]
    public async Task ReportsSafeMatcherFailureAndUsageAsDiagnosticResult()
    {
        await using var app = await RunningApp.StartAsync(
            aiMetadataMatcher: new FailingMatcher(),
            tmdbClient: new ValidTmdbClient());

        using var response = await app.Client.PostAsJsonAsync("/api/v1/ai-test/run", new
        {
            title = "Example",
            files = new[] { new { name = "Example.mkv", size_bytes = 1 } },
        });
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(json.GetProperty("succeeded").GetBoolean());
        Assert.Equal("ai_http_timeout", json.GetProperty("error_code").GetString());
        Assert.Equal(1, json.GetProperty("usage").GetProperty("request_count").GetInt32());
        Assert.Equal("matcher_failed", json.GetProperty("trace")[0].GetProperty("stage").GetString());
    }

    private sealed class CapturingMatcher : IAiMetadataMatcher
    {
        public AiMetadataMatchInput? LastInput { get; private set; }

        public Task<AiMetadataMatchResponse> MatchAsync(
            AiMetadataMatchInput input,
            CancellationToken cancellationToken = default)
        {
            LastInput = input;
            return Task.FromResult(new AiMetadataMatchResponse(
                new AiMetadataMatchCandidate(
                    true,
                    42,
                    [new AiMetadataFileCandidate(input.Files[0].Name, true, 1, 6, null)],
                    null),
                new AiMetadataProviderUsage("test-model", 7, 4, 11, 1, 0))
            {
                RawOutput = "{\"matched\":true}",
                Trace = [new AiMetadataTraceEvent(1, "model_response", "round=1", 5)],
            });
        }
    }

    private sealed class FailingMatcher : IAiMetadataMatcher
    {
        public Task<AiMetadataMatchResponse> MatchAsync(
            AiMetadataMatchInput input,
            CancellationToken cancellationToken = default) =>
            throw new AiMetadataMatcherException(
                MetadataFailureKind.Network,
                "ai_http_timeout",
                usage: new AiMetadataProviderUsage("test-model", 3, null, 3, 1, 0));
    }

    private sealed class ValidTmdbClient : ITmdbClient
    {
        private static readonly TmdbSeries Series = new(42, "Example", "Example", new DateOnly(2026, 1, 1));

        public Task<IReadOnlyList<TmdbSeries>> SearchSeriesAsync(string title, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TmdbSeries>>([Series]);

        public Task<TmdbSeries?> GetSeriesAsync(int seriesId, CancellationToken cancellationToken = default) =>
            Task.FromResult<TmdbSeries?>(seriesId == 42 ? Series : null);

        public Task<TmdbSeriesDetails?> GetSeriesDetailsAsync(int seriesId, CancellationToken cancellationToken = default) =>
            Task.FromResult<TmdbSeriesDetails?>(seriesId == 42
                ? new TmdbSeriesDetails(Series, [Season()])
                : null);

        public Task<TmdbSeason?> GetSeasonAsync(int seriesId, int seasonNumber, CancellationToken cancellationToken = default) =>
            Task.FromResult<TmdbSeason?>(seriesId == 42 && seasonNumber == 1 ? Season() : null);

        public Task<TmdbEpisode?> GetEpisodeAsync(int seriesId, int seasonNumber, int episodeNumber, CancellationToken cancellationToken = default) =>
            Task.FromResult<TmdbEpisode?>(seriesId == 42 && seasonNumber == 1 && episodeNumber == 6
                ? new TmdbEpisode(4206, 42, 1, 6, "Episode 6", new DateOnly(2026, 2, 6))
                : null);

        private static TmdbSeason Season() => new(421, 42, 1, "Season 1", new DateOnly(2026, 1, 1), 12);
    }

    private sealed class PublicDnsResolver : ITorrentDnsResolver
    {
        public ValueTask<IReadOnlyList<IPAddress>> ResolveAsync(
            string host,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<IPAddress>>([IPAddress.Parse("1.1.1.1")]);
    }

    private sealed class MikanImportTransport : ITorrentHttpTransport
    {
        public ValueTask<TorrentHttpResponse> SendAsync(
            Uri uri,
            IReadOnlyList<IPAddress> validatedAddresses,
            CancellationToken cancellationToken) => SendAsync(
                uri,
                validatedAddresses,
                new TorrentHttpRequestOptions(),
                cancellationToken);

        public ValueTask<TorrentHttpResponse> SendAsync(
            Uri uri,
            IReadOnlyList<IPAddress> validatedAddresses,
            TorrentHttpRequestOptions requestOptions,
            CancellationToken cancellationToken)
        {
            var body = uri.AbsolutePath switch
            {
                var path when path.StartsWith("/Home/Episode/", StringComparison.Ordinal) =>
                    "<a class=\"mikan-rss\" href=\"/RSS/Bangumi?bangumiId=4028&subgroupid=123\">RSS</a>",
                "/RSS/Bangumi" => $"""
                    <rss><channel><link>https://mikanime.tv/Home/Bangumi/4028</link><item>
                    <title>[Group] Imported Show - 06</title>
                    <link>https://mikanime.tv/Home/Episode/{EpisodeId}</link>
                    <enclosure url="https://mikanime.tv/Download/20260809/{EpisodeId}.torrent" length="42" type="application/x-bittorrent" />
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
}
