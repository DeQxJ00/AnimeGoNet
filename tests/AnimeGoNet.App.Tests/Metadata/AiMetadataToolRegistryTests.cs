using System.Net;
using System.Text;
using System.Text.Json;
using AnimeGoNet.App.Metadata;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Metadata;

namespace AnimeGoNet.App.Tests.Metadata;

public sealed class AiMetadataToolRegistryTests
{
    [Fact]
    public async Task DisabledPromptFeaturesDoNotInitializeOrExposeTheirTools()
    {
        var handler = new ReferenceToolHandler();
        using var client = new HttpClient(handler);
        var input = Input() with
        {
            BangumiSubjectId = 123,
            AniDbAnimeId = 456,
            ImdbTitleId = "tt1234567",
            PromptFeaturesOverride = new(false, false, false, false)
            {
                ImdbLookup = true,
            },
        };
        var registry = new AiMetadataToolRegistry(
            client,
            new AiMatchingOptions(),
            input);

        await registry.InitializeAsync(CancellationToken.None);

        Assert.Empty(registry.Tools);
        Assert.Empty(handler.MappingUris);
        Assert.Empty(handler.InvokeArguments);
        Assert.Contains(
            "tool_not_available",
            await registry.CallAsync("lookup_anidb_tmdbtv", "{}", CancellationToken.None),
            StringComparison.Ordinal);
        Assert.Contains(
            "tool_not_available",
            await registry.CallAsync("lookup_imdb_tmdb_tv", "{}", CancellationToken.None),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task FixedReferenceToolsIgnoreModelIdentifiersAndFilterMovieResults()
    {
        var handler = new ReferenceToolHandler();
        using var client = new HttpClient(handler);
        var options = new AiMatchingOptions
        {
            TmdbMcpUrl = new Uri("http://tmdb-reference.test.invalid/mcp"),
            AniDbMappingUrlTemplate = "http://127.0.0.1/private/{anidbid}",
        };
        var input = Input() with
        {
            AniDbAnimeId = 123,
            ImdbTitleId = "tt1234567",
        };
        var registry = new AiMetadataToolRegistry(client, options, input);

        await registry.InitializeAsync(CancellationToken.None);

        Assert.Contains(registry.Tools, tool => tool.Name == "lookup_anidb_tmdbtv");
        Assert.Contains(registry.Tools, tool => tool.Name == "lookup_imdb_tmdb_tv");
        Assert.All(
            registry.Tools.Where(tool => tool.Name.StartsWith("lookup_", StringComparison.Ordinal)),
            tool => Assert.Equal(
                """{"type":"object","properties":{},"additionalProperties":false}""",
                tool.ParametersJson));

        var bypass = await registry.CallAsync(
            "tmdb__invoke-api-endpoint",
            """
            {
              "endpoint":"/3/find/{external_id}",
              "method":"GET",
              "params":{
                "external_id":"tt9999999",
                "external_source":"imdb_id"
              }
            }
            """,
            CancellationToken.None);
        Assert.Contains(
            "tmdb_find_reference_mismatch",
            bypass,
            StringComparison.Ordinal);
        Assert.Contains(
            "tmdb_invoke_arguments_invalid",
            await registry.CallAsync(
                "tmdb__invoke-api-endpoint",
                """{"method":"GET","params":{}}""",
                CancellationToken.None),
            StringComparison.Ordinal);
        Assert.Empty(handler.InvokeArguments);

        var aniDb = await registry.CallAsync(
            "lookup_anidb_tmdbtv",
            """{"url":"http://127.0.0.1/private","anidbid":999}""",
            CancellationToken.None);
        Assert.Contains("\"tmdbtv\":777", aniDb, StringComparison.Ordinal);
        Assert.Equal(
            AiMatchingOptions.FixedAniDbMappingUrlTemplate.Replace(
                "{anidbid}",
                "123",
                StringComparison.Ordinal),
            Assert.Single(handler.MappingUris).AbsoluteUri);

        var imdb = await registry.CallAsync(
            "lookup_imdb_tmdb_tv",
            """{"url":"http://127.0.0.1/private","imdbid":"tt9999999"}""",
            CancellationToken.None);
        using var result = JsonDocument.Parse(imdb);
        Assert.Equal("tt1234567", result.RootElement.GetProperty("imdbid").GetString());
        Assert.Equal(
            [42L, 43L],
            result.RootElement.GetProperty("tmdb_tv_ids")
                .EnumerateArray()
                .Select(value => value.GetInt64())
                .ToArray());
        Assert.Equal(
            1,
            result.RootElement.GetProperty("movie_results_rejected").GetInt32());
        Assert.DoesNotContain("999", imdb, StringComparison.Ordinal);

        using var invoke = JsonDocument.Parse(
            Assert.Single(handler.InvokeArguments));
        Assert.Equal(
            "/3/find/{external_id}",
            invoke.RootElement.GetProperty("endpoint").GetString());
        Assert.Equal("GET", invoke.RootElement.GetProperty("method").GetString());
        Assert.Equal(
            "tt1234567",
            invoke.RootElement.GetProperty("params")
                .GetProperty("external_id").GetString());
        Assert.Equal(
            "imdb_id",
            invoke.RootElement.GetProperty("params")
                .GetProperty("external_source").GetString());
    }

    [Fact]
    public async Task ReferenceToolsAreAbsentWhenTaskHasNoReferenceIds()
    {
        var handler = new ReferenceToolHandler();
        using var client = new HttpClient(handler);
        var registry = new AiMetadataToolRegistry(
            client,
            new AiMatchingOptions
            {
                TmdbMcpUrl = new Uri("http://tmdb-no-reference.test.invalid/mcp"),
            },
            Input());

        await registry.InitializeAsync(CancellationToken.None);

        Assert.DoesNotContain(
            registry.Tools,
            tool => tool.Name is "lookup_anidb_tmdbtv" or "lookup_imdb_tmdb_tv");
        Assert.Contains(
            "tmdb_find_reference_unavailable",
            await registry.CallAsync(
                "tmdb__invoke-api-endpoint",
                """
                {
                  "endpoint":"/3/find/{external_id}",
                  "method":"GET",
                  "params":{
                    "external_id":"tt1234567",
                    "external_source":"imdb_id"
                  }
                }
                """,
                CancellationToken.None),
            StringComparison.Ordinal);
        Assert.Contains(
            "tool_not_available",
            await registry.CallAsync(
                "lookup_imdb_tmdb_tv",
                "{}",
                CancellationToken.None),
            StringComparison.Ordinal);
        Assert.Empty(handler.MappingUris);
        Assert.Empty(handler.InvokeArguments);
    }

    [Fact]
    public async Task ToolCallAcceptsStreamableHttpAcceptedResponseAndReadsSessionSseResult()
    {
        var handler = new StreamableMcpHandler();
        using var client = new HttpClient(handler);
        var registry = new AiMetadataToolRegistry(
            client,
            new AiMatchingOptions
            {
                TmdbMcpUrl = new Uri("http://tmdb-streamable.test.invalid/mcp"),
            },
            Input());

        await registry.InitializeAsync(CancellationToken.None);
        var output = await registry.CallAsync(
            "tmdb__tv-episode-details",
            """{"series_id":65942,"season_number":1,"episode_number":78}""",
            CancellationToken.None);

        using var result = JsonDocument.Parse(output);
        Assert.False(result.RootElement.GetProperty("isError").GetBoolean());
        Assert.Contains(
            "\"episode_number\":78",
            result.RootElement.GetProperty("content")[0].GetProperty("text").GetString(),
            StringComparison.Ordinal);
        Assert.Equal(1, handler.SseRequestCount);
        Assert.Equal(1, handler.AcceptedToolCallCount);
    }

    [Fact]
    public async Task TmdbMcpTransportFailureIsNotReportedAsMetadataNoMatch()
    {
        var handler = new FailingToolMcpHandler();
        using var client = new HttpClient(handler);
        var registry = new AiMetadataToolRegistry(
            client,
            new AiMatchingOptions
            {
                TmdbMcpUrl = new Uri("http://tmdb-failing-tool.test.invalid/mcp"),
            },
            Input());

        await registry.InitializeAsync(CancellationToken.None);
        var exception = await Assert.ThrowsAsync<AiMetadataMatcherException>(() =>
            registry.CallAsync(
                "tmdb__tv-episode-details",
                """{"series_id":65942,"season_number":1,"episode_number":78}""",
                CancellationToken.None));

        Assert.Equal(MetadataFailureKind.Protocol, exception.Kind);
        Assert.Equal("ai_tmdb_mcp_sse_error", exception.SafeCode);
    }

    [Theory]
    [InlineData(HttpRequestError.NameResolutionError, "ai_tmdb_mcp_dns_error")]
    [InlineData(HttpRequestError.ConnectionError, "ai_tmdb_mcp_connection_error")]
    [InlineData(HttpRequestError.Unknown, "ai_tmdb_mcp_network_error")]
    public async Task TmdbMcpNetworkFailuresUseSpecificClassification(
        HttpRequestError requestError,
        string expectedCode)
    {
        using var client = new HttpClient(new ThrowingMcpHandler(
            new HttpRequestException(requestError, "test MCP transport failure")));
        var registry = new AiMetadataToolRegistry(
            client,
            new AiMatchingOptions
            {
                TmdbMcpUrl = new Uri("http://tmdb-network.test.invalid/mcp"),
            },
            Input());

        var exception = await Assert.ThrowsAsync<AiMetadataMatcherException>(() =>
            registry.InitializeAsync(CancellationToken.None));

        Assert.Equal(MetadataFailureKind.Network, exception.Kind);
        Assert.Equal(expectedCode, exception.SafeCode);
    }

    [Fact]
    public async Task BangumiMcpConnectionFailureUsesBangumiClassification()
    {
        using var client = new HttpClient(new ThrowingMcpHandler(
            new HttpRequestException(
                HttpRequestError.ConnectionError,
                "test Bangumi MCP connection failure")));
        var registry = new AiMetadataToolRegistry(
            client,
            new AiMatchingOptions
            {
                BangumiMcpUrl = new Uri("http://bangumi-network.test.invalid/mcp"),
            },
            Input() with
            {
                BangumiSubjectId = 123,
                PromptFeaturesOverride = new(false, true, false, false),
            });

        var exception = await Assert.ThrowsAsync<AiMetadataMatcherException>(() =>
            registry.InitializeAsync(CancellationToken.None));

        Assert.Equal(MetadataFailureKind.Network, exception.Kind);
        Assert.Equal("ai_bangumi_mcp_connection_error", exception.SafeCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, MetadataFailureKind.Authentication, "ai_tmdb_mcp_authentication_failed")]
    [InlineData(HttpStatusCode.TooManyRequests, MetadataFailureKind.RemoteService, "ai_tmdb_mcp_rate_limited")]
    [InlineData(HttpStatusCode.ServiceUnavailable, MetadataFailureKind.RemoteService, "ai_tmdb_mcp_service_error")]
    [InlineData(HttpStatusCode.BadRequest, MetadataFailureKind.Protocol, "ai_tmdb_mcp_http_rejected")]
    public async Task TmdbMcpHttpFailuresUseSpecificClassification(
        HttpStatusCode statusCode,
        MetadataFailureKind expectedKind,
        string expectedCode)
    {
        using var client = new HttpClient(new StatusMcpHandler(statusCode));
        var registry = new AiMetadataToolRegistry(
            client,
            new AiMatchingOptions
            {
                TmdbMcpUrl = new Uri("http://tmdb-http.test.invalid/mcp"),
            },
            Input());

        var exception = await Assert.ThrowsAsync<AiMetadataMatcherException>(() =>
            registry.InitializeAsync(CancellationToken.None));

        Assert.Equal(expectedKind, exception.Kind);
        Assert.Equal(expectedCode, exception.SafeCode);
    }

    [Fact]
    public async Task RequiredTmdbToolNotUsedHasDedicatedClassification()
    {
        var handler = new ReferenceToolHandler();
        using var client = new HttpClient(handler);
        var registry = new AiMetadataToolRegistry(
            client,
            new AiMatchingOptions
            {
                TmdbMcpUrl = new Uri("http://tmdb-reference.test.invalid/mcp"),
            },
            Input());

        await registry.InitializeAsync(CancellationToken.None);
        var exception = Assert.Throws<AiMetadataMatcherException>(
            registry.EnsureRequiredTmdbToolWasUsed);

        Assert.Equal(MetadataFailureKind.Protocol, exception.Kind);
        Assert.Equal("ai_tmdb_mcp_not_used", exception.SafeCode);
    }

    [Fact]
    public async Task TmdbMcpToolErrorHasDedicatedClassification()
    {
        using var client = new HttpClient(new ErrorResultMcpHandler());
        var registry = new AiMetadataToolRegistry(
            client,
            new AiMatchingOptions
            {
                TmdbMcpUrl = new Uri("http://tmdb-tool-error.test.invalid/mcp"),
            },
            Input());

        await registry.InitializeAsync(CancellationToken.None);
        var output = await registry.CallAsync(
            "tmdb__tv-season-details",
            """{"series_id":65942,"season_number":4}""",
            CancellationToken.None);
        Assert.Contains("\"isError\":true", output, StringComparison.Ordinal);

        var exception = Assert.Throws<AiMetadataMatcherException>(
            registry.EnsureRequiredTmdbToolWasUsed);
        Assert.Equal(MetadataFailureKind.RemoteService, exception.Kind);
        Assert.Equal("ai_tmdb_mcp_tool_error", exception.SafeCode);
    }

    private static AiMetadataMatchInput Input() =>
        new(
            "Task",
            [new AiMetadataFileInput("01.mkv", 100)],
            BangumiSubjectId: null,
            AniDbAnimeId: null,
            ImdbTitleId: null,
            TorrentFileCount: 1,
            PublishedAt: null,
            BangumiEpisodeCandidate: null,
            UseBangumiPubDateFirst: false);

    private sealed class ReferenceToolHandler : HttpMessageHandler
    {
        public List<Uri> MappingUris { get; } = [];

        public List<string> InvokeArguments { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri!.Host == "raw.githubusercontent.com")
            {
                MappingUris.Add(request.RequestUri);
                return Json(HttpStatusCode.OK, """{"tmdbtv":"777"}""");
            }

            if (request.RequestUri.Host.StartsWith(
                "tmdb-",
                StringComparison.Ordinal))
            {
                return await HandleMcpAsync(request, cancellationToken);
            }

            throw new HttpRequestException(
                $"Unexpected test destination {request.RequestUri.Host}.");
        }

        private async Task<HttpResponseMessage> HandleMcpAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            if (body.Contains(
                "\"method\":\"notifications/initialized\"",
                StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.Accepted, "{}");
            }

            using var document = JsonDocument.Parse(body);
            var method = document.RootElement.GetProperty("method").GetString();
            var id = document.RootElement.GetProperty("id").GetInt32();
            switch (method)
            {
                case "initialize":
                    {
                        var response = Json(
                            HttpStatusCode.OK,
                            $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"result\":{{\"protocolVersion\":\"2025-03-26\",\"capabilities\":{{}},\"serverInfo\":{{\"name\":\"fake\",\"version\":\"1\"}}}}}}");
                        response.Headers.Add("Mcp-Session-Id", "reference-session");
                        return response;
                    }
                case "tools/list":
                    return Json(
                        HttpStatusCode.OK,
                        $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"result\":{{\"tools\":[{{\"name\":\"invoke-api-endpoint\",\"description\":\"Invoke endpoint\",\"inputSchema\":{{\"type\":\"object\",\"properties\":{{}},\"additionalProperties\":true}}}}]}}}}");
                case "tools/call":
                    {
                        var parameters = document.RootElement.GetProperty("params");
                        Assert.Equal(
                            "invoke-api-endpoint",
                            parameters.GetProperty("name").GetString());
                        InvokeArguments.Add(
                            parameters.GetProperty("arguments").GetRawText());
                        const string payload =
                            """{"tv_results":[{"id":43},{"id":42},{"id":42}],"movie_results":[{"id":999}]}""";
                        return Json(
                            HttpStatusCode.OK,
                            JsonSerializer.Serialize(new
                            {
                                jsonrpc = "2.0",
                                id,
                                result = new
                                {
                                    content = new[]
                                    {
                                    new { type = "text", text = payload },
                                    },
                                    isError = false,
                                },
                            }));
                    }
                default:
                    return Json(HttpStatusCode.BadRequest, "{}");
            }
        }

        private static HttpResponseMessage Json(
            HttpStatusCode status,
            string json) =>
            new(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
    }

    private sealed class StreamableMcpHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource<string> _ssePayload = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public int SseRequestCount { get; private set; }

        public int AcceptedToolCallCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get)
            {
                SseRequestCount++;
                var payload = await _ssePayload.Task.WaitAsync(cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "data: " + payload + "\n\n",
                        Encoding.UTF8,
                        "text/event-stream"),
                };
            }

            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            if (body.Contains(
                "\"method\":\"notifications/initialized\"",
                StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.Accepted);
            }

            using var document = JsonDocument.Parse(body);
            var method = document.RootElement.GetProperty("method").GetString();
            var id = document.RootElement.GetProperty("id").GetInt32();
            if (method == "initialize")
            {
                var response = Json(
                    HttpStatusCode.OK,
                    $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"result\":{{\"protocolVersion\":\"2025-03-26\",\"capabilities\":{{}},\"serverInfo\":{{\"name\":\"fake\",\"version\":\"1\"}}}}}}");
                response.Headers.Add("Mcp-Session-Id", "streamable-session");
                return response;
            }

            if (method == "tools/list")
            {
                return Json(
                    HttpStatusCode.OK,
                    $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"result\":{{\"tools\":[{{\"name\":\"tv-episode-details\",\"description\":\"Episode\",\"inputSchema\":{{\"type\":\"object\"}}}}]}}}}");
            }

            Assert.Equal("tools/call", method);
            Assert.Equal("streamable-session", request.Headers.GetValues("Mcp-Session-Id").Single());
            AcceptedToolCallCount++;
            _ssePayload.TrySetResult(
                $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"result\":{{\"content\":[{{\"type\":\"text\",\"text\":\"{{\\\"episode_number\\\":78}}\"}}],\"isError\":false}}}}");
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }

        private static HttpResponseMessage Json(
            HttpStatusCode status,
            string json) =>
            new(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
    }

    private sealed class FailingToolMcpHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "data: not-json\n\n",
                        Encoding.UTF8,
                        "text/event-stream"),
                };
            }

            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            if (body.Contains(
                "\"method\":\"notifications/initialized\"",
                StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.Accepted);
            }

            using var document = JsonDocument.Parse(body);
            var method = document.RootElement.GetProperty("method").GetString();
            var id = document.RootElement.GetProperty("id").GetInt32();
            if (method == "initialize")
            {
                var response = Json(
                    HttpStatusCode.OK,
                    $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"result\":{{\"protocolVersion\":\"2025-03-26\",\"capabilities\":{{}},\"serverInfo\":{{\"name\":\"fake\",\"version\":\"1\"}}}}}}");
                response.Headers.Add("Mcp-Session-Id", "failing-session");
                return response;
            }

            if (method == "tools/list")
            {
                return Json(
                    HttpStatusCode.OK,
                    $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"result\":{{\"tools\":[{{\"name\":\"tv-episode-details\",\"description\":\"Episode\",\"inputSchema\":{{\"type\":\"object\"}}}}]}}}}");
            }

            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }

        private static HttpResponseMessage Json(
            HttpStatusCode status,
            string json) =>
            new(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
    }

    private sealed class ThrowingMcpHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(exception);
    }

    private sealed class StatusMcpHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode));
    }

    private sealed class ErrorResultMcpHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            if (body.Contains(
                "\"method\":\"notifications/initialized\"",
                StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.Accepted);
            }

            using var document = JsonDocument.Parse(body);
            var method = document.RootElement.GetProperty("method").GetString();
            var id = document.RootElement.GetProperty("id").GetInt32();
            var json = method switch
            {
                "initialize" =>
                    $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"result\":{{\"protocolVersion\":\"2025-03-26\",\"capabilities\":{{}},\"serverInfo\":{{\"name\":\"fake\",\"version\":\"1\"}}}}}}",
                "tools/list" =>
                    $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"result\":{{\"tools\":[{{\"name\":\"tv-season-details\",\"description\":\"Season\",\"inputSchema\":{{\"type\":\"object\"}}}}]}}}}",
                _ =>
                    $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"result\":{{\"content\":[{{\"type\":\"text\",\"text\":\"TMDB 404\"}}],\"isError\":true}}}}",
            };
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            if (method == "initialize")
            {
                response.Headers.Add("Mcp-Session-Id", "error-result-session");
            }
            return response;
        }
    }
}
