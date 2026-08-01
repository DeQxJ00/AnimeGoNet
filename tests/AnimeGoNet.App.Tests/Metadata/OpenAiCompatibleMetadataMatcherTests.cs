using System.Net;
using System.Text;
using System.Text.Json;
using AnimeGoNet.App.Metadata;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Metadata;

namespace AnimeGoNet.App.Tests.Metadata;

public sealed class OpenAiCompatibleMetadataMatcherTests
{
    [Fact]
    public async Task ExecutesNamespacedTmdbMcpToolLoopAndParsesCandidate()
    {
        var handler = new FakeAiAndMcpHandler();
        using var client = new HttpClient(handler);
        using var matcher = new OpenAiCompatibleMetadataMatcher(
            client,
            Options(),
            ownsHttpClient: false);

        var result = await matcher.MatchAsync(Input());

        Assert.True(result.Matched);
        Assert.Equal(42, result.TmdbId);
        Assert.Equal(2, handler.AiCalls);
        Assert.Equal(1, handler.McpInitializeCalls);
        Assert.InRange(handler.McpToolsListCalls, 0, 1);
        Assert.Equal(1, handler.McpToolCalls);
        Assert.All(handler.AiBodies, body =>
            Assert.DoesNotContain(@"E:\", body, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            handler.AiBodies,
            body => body.Contains("\"role\":\"tool\"", StringComparison.Ordinal));
        Assert.All(handler.AuthorizationValues, value =>
            Assert.Equal("Bearer local-secret", value));
    }

    [Fact]
    public async Task DoesNotInitializeBangumiMcpWhenBgmidIsNull()
    {
        var handler = new FakeAiAndMcpHandler();
        using var client = new HttpClient(handler);
        using var matcher = new OpenAiCompatibleMetadataMatcher(client, Options());

        _ = await matcher.MatchAsync(Input());

        Assert.DoesNotContain(
            handler.RequestHosts,
            host => host.Equals("bgm.test.invalid", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MissingProviderConfigurationFailsBeforeNetworkAccess()
    {
        var handler = new FakeAiAndMcpHandler();
        using var client = new HttpClient(handler);
        using var matcher = new OpenAiCompatibleMetadataMatcher(
            client,
            Options() with { BaseUrl = null, Model = null });

        var exception = await Assert.ThrowsAsync<AiMetadataMatcherException>(
            () => matcher.MatchAsync(Input()));

        Assert.Equal(MetadataFailureKind.Configuration, exception.Kind);
        Assert.Equal("ai_provider_not_configured", exception.SafeCode);
        Assert.Empty(handler.RequestHosts);
    }

    [Fact]
    public async Task RetriesRateLimitThenSucceeds()
    {
        var handler = new FakeAiAndMcpHandler { RateLimitFirstAiRequest = true };
        using var client = new HttpClient(handler);
        using var matcher = new OpenAiCompatibleMetadataMatcher(
            client,
            Options() with { RetryCount = 1 });

        var result = await matcher.MatchAsync(Input());

        Assert.True(result.Matched);
        Assert.Equal(3, handler.AiCalls);
    }

    [Fact]
    public async Task ExhaustedRateLimitUsesStableSafeClassification()
    {
        var handler = new FakeAiAndMcpHandler { AlwaysRateLimitAiRequests = true };
        using var client = new HttpClient(handler);
        using var matcher = new OpenAiCompatibleMetadataMatcher(
            client,
            Options() with { RetryCount = 1 });

        var exception = await Assert.ThrowsAsync<AiMetadataMatcherException>(
            () => matcher.MatchAsync(Input()));

        Assert.Equal(MetadataFailureKind.RemoteService, exception.Kind);
        Assert.Equal("ai_rate_limited", exception.SafeCode);
        Assert.Equal(2, handler.AiCalls);
        Assert.DoesNotContain("retry", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProviderTimeoutUsesStableNetworkClassification()
    {
        var handler = new FakeAiAndMcpHandler { DelayAiResponse = true };
        using var client = new HttpClient(handler);
        using var matcher = new OpenAiCompatibleMetadataMatcher(
            client,
            Options() with
            {
                HttpTimeout = TimeSpan.FromMilliseconds(30),
                RetryCount = 0,
            });

        var exception = await Assert.ThrowsAsync<AiMetadataMatcherException>(
            () => matcher.MatchAsync(Input()));

        Assert.Equal(MetadataFailureKind.Network, exception.Kind);
        Assert.Equal("ai_http_timeout", exception.SafeCode);
        Assert.Equal(1, handler.AiCalls);
    }

    [Fact]
    public async Task MalformedChatJsonUsesStableProtocolClassification()
    {
        var handler = new FakeAiAndMcpHandler { RawAiResponse = "{" };
        using var client = new HttpClient(handler);
        using var matcher = new OpenAiCompatibleMetadataMatcher(client, Options());

        var exception = await Assert.ThrowsAsync<AiMetadataMatcherException>(
            () => matcher.MatchAsync(Input()));

        Assert.Equal(MetadataFailureKind.Protocol, exception.Kind);
        Assert.Equal("ai_response_json_invalid", exception.SafeCode);
    }

    [Fact]
    public async Task MalformedModelJsonUsesStableProtocolClassification()
    {
        var handler = new FakeAiAndMcpHandler { FinalModelResult = "{" };
        using var client = new HttpClient(handler);
        using var matcher = new OpenAiCompatibleMetadataMatcher(client, Options());

        var exception = await Assert.ThrowsAsync<AiMetadataMatcherException>(
            () => matcher.MatchAsync(Input()));

        Assert.Equal(MetadataFailureKind.Protocol, exception.Kind);
        Assert.Equal("ai_result_json_invalid", exception.SafeCode);
    }

    [Fact]
    public async Task MultipleProviderChoicesAreRejectedAsAmbiguous()
    {
        var handler = new FakeAiAndMcpHandler
        {
            RawAiResponse =
                """
                {
                  "choices": [
                    {"message":{"content":"{\"matched\":false}"}},
                    {"message":{"content":"{\"matched\":true}"}}
                  ]
                }
                """,
        };
        using var client = new HttpClient(handler);
        using var matcher = new OpenAiCompatibleMetadataMatcher(client, Options());

        var exception = await Assert.ThrowsAsync<AiMetadataMatcherException>(
            () => matcher.MatchAsync(Input()));

        Assert.Equal(MetadataFailureKind.Protocol, exception.Kind);
        Assert.Equal("ai_chat_response_ambiguous", exception.SafeCode);
    }

    [Fact]
    public async Task McpToolSchemaCacheAvoidsRepeatedDiscoveryForSameEndpoint()
    {
        var handler = new FakeAiAndMcpHandler();
        using var client = new HttpClient(handler);
        var options = Options() with
        {
            TmdbMcpUrl = new Uri("http://tmdb.test.invalid/mcp/cache-boundary-v1"),
        };

        using (var first = new OpenAiCompatibleMetadataMatcher(client, options))
        {
            Assert.True((await first.MatchAsync(Input())).Matched);
        }

        using (var second = new OpenAiCompatibleMetadataMatcher(client, options))
        {
            Assert.True((await second.MatchAsync(Input())).Matched);
        }

        Assert.Equal(2, handler.McpInitializeCalls);
        Assert.Equal(1, handler.McpToolsListCalls);
    }

    [Fact]
    public async Task FakeProviderCannotForgeNonexistentTmdbSeries()
    {
        var handler = new FakeAiAndMcpHandler
        {
            FinalModelResult =
                """{"matched":true,"tmdb_id":999999,"files":[{"name":"Season 1/01.mkv","matched":true,"season":1,"episode":1,"reason":null}],"reason":null}""",
        };
        using var client = new HttpClient(handler);
        using var matcher = new OpenAiCompatibleMetadataMatcher(client, Options());
        var input = Input();

        var candidate = await matcher.MatchAsync(input);
        var tmdb = new RejectingTmdbClient();
        var validation = await new AiMetadataResultValidator(tmdb).ValidateAsync(input, candidate);

        Assert.Equal(MetadataFailureKind.SemanticNoMatch, validation.Failure!.Kind);
        Assert.Equal("ai_tmdb_series_not_found", validation.Failure.Code);
        Assert.Equal(1, tmdb.SeriesDetailsCalls);
    }

    [Fact]
    public async Task FakeProviderFileListConflictIsRejectedBeforeTmdbAccess()
    {
        var handler = new FakeAiAndMcpHandler
        {
            FinalModelResult =
                """{"matched":true,"tmdb_id":42,"files":[{"name":"Season 1/02.mkv","matched":true,"season":1,"episode":2,"reason":null}],"reason":null}""",
        };
        using var client = new HttpClient(handler);
        using var matcher = new OpenAiCompatibleMetadataMatcher(client, Options());
        var input = Input();

        var candidate = await matcher.MatchAsync(input);
        var tmdb = new RejectingTmdbClient();
        var validation = await new AiMetadataResultValidator(tmdb).ValidateAsync(input, candidate);

        Assert.Equal(MetadataFailureKind.Protocol, validation.Failure!.Kind);
        Assert.Equal("ai_file_identity_mismatch", validation.Failure.Code);
        Assert.Equal(0, tmdb.SeriesDetailsCalls);
    }

    [Fact]
    public async Task AuthenticationFailureUsesSafeClassification()
    {
        var handler = new FakeAiAndMcpHandler { RejectAiAuthentication = true };
        using var client = new HttpClient(handler);
        using var matcher = new OpenAiCompatibleMetadataMatcher(
            client,
            Options() with { RetryCount = 0 });

        var exception = await Assert.ThrowsAsync<AiMetadataMatcherException>(
            () => matcher.MatchAsync(Input()));

        Assert.Equal(MetadataFailureKind.Authentication, exception.Kind);
        Assert.Equal("ai_authentication_failed", exception.SafeCode);
        Assert.DoesNotContain("local-secret", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsLegacyEpisodeOffsetField()
    {
        var exception = Assert.Throws<AiMetadataMatcherException>(() =>
            OpenAiCompatibleMetadataMatcher.ParseCandidate(
                """
                {
                  "matched": false,
                  "tmdb_id": null,
                  "episode_offset": null,
                  "files": [],
                  "reason": "not matched"
                }
                """));

        Assert.Equal("ai_legacy_result_field", exception.SafeCode);
    }

    private static AiMetadataMatchInput Input() =>
        new(
            "Task title",
            [new AiMetadataFileInput("Season 1/01.mkv", 100)],
            BangumiSubjectId: null,
            AniDbAnimeId: null,
            ImdbTitleId: null,
            TorrentFileCount: 1,
            PublishedAt: null,
            BangumiEpisodeCandidate: null,
            UseBangumiPubDateFirst: false);

    private static AiMatchingOptions Options() =>
        new()
        {
            BaseUrl = new Uri("https://ai.test.invalid/compatible/"),
            ApiKey = "local-secret",
            Model = "test-model",
            RetryCount = 0,
            HttpTimeout = TimeSpan.FromSeconds(10),
            TmdbMcpUrl = new Uri("http://tmdb.test.invalid/mcp"),
            BangumiMcpUrl = new Uri("http://bgm.test.invalid/mcp"),
        };

    private sealed class FakeAiAndMcpHandler : HttpMessageHandler
    {
        private int _aiSequence;

        public int AiCalls { get; private set; }

        public int McpInitializeCalls { get; private set; }

        public int McpToolsListCalls { get; private set; }

        public int McpToolCalls { get; private set; }

        public bool RateLimitFirstAiRequest { get; init; }

        public bool AlwaysRateLimitAiRequests { get; init; }

        public bool DelayAiResponse { get; init; }

        public bool RejectAiAuthentication { get; init; }

        public string? RawAiResponse { get; init; }

        public string? FinalModelResult { get; init; }

        public List<string> RequestHosts { get; } = [];

        public List<string> AiBodies { get; } = [];

        public List<string?> AuthorizationValues { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestHosts.Add(request.RequestUri!.Host);
            if (request.RequestUri.Host == "ai.test.invalid")
            {
                return await HandleAiAsync(request, cancellationToken);
            }

            if (request.RequestUri.Host is "tmdb.test.invalid" or "bgm.test.invalid")
            {
                return await HandleMcpAsync(request, cancellationToken);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private async Task<HttpResponseMessage> HandleAiAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            AiCalls++;
            AuthorizationValues.Add(request.Headers.Authorization?.ToString());
            AiBodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            if (DelayAiResponse)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            if (RejectAiAuthentication)
            {
                return Json(HttpStatusCode.Unauthorized, """{"error":{"message":"private detail"}}""");
            }

            if (AlwaysRateLimitAiRequests || (RateLimitFirstAiRequest && AiCalls == 1))
            {
                return Json(HttpStatusCode.TooManyRequests, """{"error":{"message":"retry"}}""");
            }

            if (RawAiResponse is not null)
            {
                return Json(HttpStatusCode.OK, RawAiResponse);
            }

            _aiSequence++;
            if (_aiSequence == 1)
            {
                return Json(HttpStatusCode.OK,
                    """
                    {
                      "choices": [
                        {
                          "message": {
                            "content": null,
                            "tool_calls": [
                              {
                                "id": "call-1",
                                "type": "function",
                                "function": {
                                  "name": "tmdb__list-api-endpoints",
                                  "arguments": "{}"
                                }
                              }
                            ]
                          }
                        }
                      ]
                    }
                    """);
            }

            var modelResult = FinalModelResult
                ?? """{"matched":true,"tmdb_id":42,"files":[{"name":"Season 1/01.mkv","matched":true,"season":1,"episode":1,"reason":null}],"reason":null}""";
            return Json(
                HttpStatusCode.OK,
                "{\"choices\":[{\"message\":{\"content\":"
                + JsonSerializer.Serialize(modelResult)
                + "}}]}");
        }

        private async Task<HttpResponseMessage> HandleMcpAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            if (body.Contains("\"method\":\"notifications/initialized\"", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.Accepted, "{}");
            }

            using var document = JsonDocument.Parse(body);
            var method = document.RootElement.GetProperty("method").GetString();
            var id = document.RootElement.GetProperty("id").GetInt32();
            HttpResponseMessage response;
            switch (method)
            {
                case "initialize":
                    McpInitializeCalls++;
                    response = Json(
                        HttpStatusCode.OK,
                        $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"result\":{{\"protocolVersion\":\"2025-03-26\",\"capabilities\":{{}},\"serverInfo\":{{\"name\":\"fake\",\"version\":\"1\"}}}}}}");
                    response.Headers.Add("Mcp-Session-Id", "test-session");
                    return response;
                case "tools/list":
                    McpToolsListCalls++;
                    return Json(
                        HttpStatusCode.OK,
                        $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"result\":{{\"tools\":[{{\"name\":\"list-api-endpoints\",\"description\":\"List endpoints\",\"inputSchema\":{{\"type\":\"object\",\"properties\":{{}},\"additionalProperties\":false}}}}]}}}}");
                case "tools/call":
                    McpToolCalls++;
                    return Json(
                        HttpStatusCode.OK,
                        $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"result\":{{\"content\":[{{\"type\":\"text\",\"text\":\"GET /3/tv/{{series_id}}\"}}],\"isError\":false}}}}");
                default:
                    return Json(HttpStatusCode.BadRequest, "{}");
            }
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
            new(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
    }

    private sealed class RejectingTmdbClient : ITmdbClient
    {
        public int SeriesDetailsCalls { get; private set; }

        public Task<IReadOnlyList<TmdbSeries>> SearchSeriesAsync(
            string title,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TmdbSeries?> GetSeriesAsync(
            int seriesId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TmdbSeriesDetails?> GetSeriesDetailsAsync(
            int seriesId,
            CancellationToken cancellationToken = default)
        {
            SeriesDetailsCalls++;
            return Task.FromResult<TmdbSeriesDetails?>(null);
        }

        public Task<TmdbSeason?> GetSeasonAsync(
            int seriesId,
            int seasonNumber,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TmdbEpisode?> GetEpisodeAsync(
            int seriesId,
            int seasonNumber,
            int episodeNumber,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
