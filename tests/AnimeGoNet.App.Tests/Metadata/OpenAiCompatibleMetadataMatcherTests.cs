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

        public bool RejectAiAuthentication { get; init; }

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
            if (RejectAiAuthentication)
            {
                return Json(HttpStatusCode.Unauthorized, """{"error":{"message":"private detail"}}""");
            }

            if (RateLimitFirstAiRequest && AiCalls == 1)
            {
                return Json(HttpStatusCode.TooManyRequests, """{"error":{"message":"retry"}}""");
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

            const string modelResult =
                """{"matched":true,"tmdb_id":42,"files":[{"name":"Season 1/01.mkv","matched":true,"season":1,"episode":1,"reason":null}],"reason":null}""";
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
}
