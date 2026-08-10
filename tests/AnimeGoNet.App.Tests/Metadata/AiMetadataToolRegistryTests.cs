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
}
