using System.Net;
using System.Text.Json;
using AnimeGoNet.App.AiTesterCompat;

namespace AnimeGoNet.App.Tests.AiTesterCompat;

public sealed class RequestJsonTests
{
    [Fact]
    public async Task ResponsesRequestIncludesReasoningAndWebSearch()
    {
        var config = new TesterConfig("https://example.test", "key", "model-x", ApiMode.Responses, "medium", true, 30, null);
        using var client = new HttpClient(new StubHandler(HttpStatusCode.OK, "{}"));
        var api = new OpenAiCompatibleClient(client, config);

        using HttpRequestMessage request = api.CreateRequest("prompt");
        string json = await request.Content!.ReadAsStringAsync();
        using JsonDocument document = JsonDocument.Parse(json);

        Assert.Equal("https://example.test/v1/responses", request.RequestUri!.ToString());
        Assert.Equal("model-x", document.RootElement.GetProperty("model").GetString());
        Assert.Equal("medium", document.RootElement.GetProperty("reasoning").GetProperty("effort").GetString());
        Assert.Equal("web_search_preview", document.RootElement.GetProperty("tools")[0].GetProperty("type").GetString());
    }

    [Fact]
    public async Task ChatRequestCanOmitReasoning()
    {
        var config = new TesterConfig("https://example.test/", "key", "model-x", ApiMode.ChatCompletions, null, false, 30, null);
        using var client = new HttpClient(new StubHandler(HttpStatusCode.OK, "{}"));
        var api = new OpenAiCompatibleClient(client, config);

        using HttpRequestMessage request = api.CreateRequest("prompt");
        string json = await request.Content!.ReadAsStringAsync();

        Assert.Equal("https://example.test/v1/chat/completions", request.RequestUri!.ToString());
        Assert.DoesNotContain("reasoning", json);
        Assert.Contains("json_object", json);
    }

    [Fact]
    public void BaseUrlPathPrefixIsPreserved()
    {
        var config = new TesterConfig("https://zenmux.ai/api/", "key", "model-x", ApiMode.Responses, "medium", false, 30, null);
        using var client = new HttpClient(new StubHandler(HttpStatusCode.OK, "{}"));
        var api = new OpenAiCompatibleClient(client, config);

        using HttpRequestMessage request = api.CreateRequest("prompt");

        Assert.Equal("https://zenmux.ai/api/v1/responses", request.RequestUri!.ToString());
    }

    [Fact]
    public async Task FinalResponsesRequestContainsComputedPubDateBranchOnlyWhenGatePasses()
    {
        var config = new TesterConfig("https://example.test", "key", "model-x", ApiMode.Responses, "medium", false, 30, null);
        var input = new MatchRequestInput(
            "Title",
            [new MatchFileInput("E04.mkv", 1)],
            123,
            null,
            "2023-01-24T21:02:56.558766",
            1,
            true,
            4,
            true);
        string template = PromptTemplate.LoadFromMarkdown(PromptTemplate.FindDefaultMarkdownPath());
        string prompt = PromptTemplate.Render(template, input, PromptFeatures.From(config, input)).Text;
        using var client = new HttpClient(new StubHandler(HttpStatusCode.OK, "{}"));
        var api = new OpenAiCompatibleClient(client, config);

        using HttpRequestMessage request = api.CreateRequest(prompt);
        using JsonDocument document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
        string finalInput = document.RootElement.GetProperty("input").GetString()!;

        Assert.Contains("\"mikan_pub_date\"", finalInput);
        Assert.Contains("\"bgm_episode_candidate\": 4", finalInput);
        Assert.Contains("把 bgm_episode_candidate 及原始 files.name", finalInput);
        Assert.DoesNotContain("调用方已用 mikan_pub_date", finalInput);
    }

    [Fact]
    public async Task FinalResponsesRequestOmitsMikanPubDateWhenPrioritySwitchIsDisabled()
    {
        var config = new TesterConfig("https://example.test", "key", "model-x", ApiMode.Responses, "medium", false, 30, null);
        var input = new MatchRequestInput(
            "Title",
            [new MatchFileInput("E04.mkv", 1)],
            123,
            null,
            "2023-01-24T21:02:56.558766",
            1,
            false,
            4,
            true);
        string template = PromptTemplate.LoadFromMarkdown(PromptTemplate.FindDefaultMarkdownPath());
        string prompt = PromptTemplate.Render(template, input, PromptFeatures.From(config, input)).Text;
        using var client = new HttpClient(new StubHandler(HttpStatusCode.OK, "{}"));
        var api = new OpenAiCompatibleClient(client, config);

        using HttpRequestMessage request = api.CreateRequest(prompt);
        using JsonDocument document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
        string finalInput = document.RootElement.GetProperty("input").GetString()!;

        Assert.DoesNotContain("\"mikan_pub_date\"", finalInput);
        Assert.DoesNotContain("{{MIKAN_PUB_DATE_JSON}}", finalInput);
        Assert.Contains("\"request_identity\"", finalInput);
    }

    [Fact]
    public void RenderedPromptOmitsMikanPubDateWhenPriorityGateDoesNotPass()
    {
        var config = new TesterConfig("https://example.test", "key", "model-x", ApiMode.Responses, "medium", false, 30, null);
        var input = new MatchRequestInput(
            "Title",
            [new MatchFileInput("E04.mkv", 1)],
            123,
            null,
            "2023-01-24T21:02:56.558766",
            2,
            true,
            4,
            true);
        string template = PromptTemplate.LoadFromMarkdown(PromptTemplate.FindDefaultMarkdownPath());

        string prompt = PromptTemplate.Render(template, input, PromptFeatures.From(config, input)).Text;

        Assert.DoesNotContain("\"mikan_pub_date\"", prompt);
        Assert.Contains("\"request_identity\"", prompt);
    }

    [Fact]
    public async Task ResponsesRequestSendsLocalToolsBeforeWebSearchFallback()
    {
        var config = new TesterConfig("https://example.test", "key", "model-x", ApiMode.Responses, "medium", true, 30, null, "http://bgm.test/mcp", "http://tmdb.test/mcp");
        var handler = new ToolOrderHandler();
        using var client = new HttpClient(handler);
        var registry = new ToolRegistry(config, new MatchRequestInput("Title", [new MatchFileInput("04.mkv", 4)]), client);
        var api = new OpenAiCompatibleClient(client, config);

        ApiCallResult result = await api.SendAsync("prompt", registry, CancellationToken.None);

        Assert.True(result.Success, result.ErrorMessage);
        using JsonDocument document = JsonDocument.Parse(handler.OpenAiRequestJson);
        JsonElement tools = document.RootElement.GetProperty("tools");
        Assert.Equal("function", tools[0].GetProperty("type").GetString());
        Assert.Equal("tmdb__list-api-endpoints", tools[0].GetProperty("name").GetString());
        Assert.Equal("web_search_preview", tools[tools.GetArrayLength() - 1].GetProperty("type").GetString());
    }

    [Fact]
    public async Task ResponsesToolOutputFallsBackToStatelessContinuation()
    {
        var config = new TesterConfig("https://example.test", "key", "model-x", ApiMode.Responses, "medium", false, 30, null, "http://bgm.test/mcp", "http://tmdb.test/mcp", false, true, false);
        var handler = new StatelessResponsesHandler();
        using var client = new HttpClient(handler);
        var registry = new ToolRegistry(config, new MatchRequestInput("Title", [new MatchFileInput("04.mkv", 4)]), client);
        var api = new OpenAiCompatibleClient(client, config);

        ApiCallResult result = await api.SendAsync("prompt", registry, CancellationToken.None);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Contains("\"type\":\"function_call\"", handler.StatelessRetryJson);
        Assert.Contains("\"type\":\"function_call_output\"", handler.StatelessRetryJson);
        Assert.Contains("\"call_id\":\"call_1\"", handler.StatelessRetryJson);
        Assert.Equal(3, handler.ResponsesRequestCount);
    }

    [Fact]
    public async Task ReportsAndSumsUsageAcrossModelRounds()
    {
        var config = new TesterConfig("https://example.test", "key", "model-x", ApiMode.Responses, "medium", false, 30, null, "http://bgm.test/mcp", "http://tmdb.test/mcp", false, false, false);
        using var client = new HttpClient(new UsageRoundsHandler());
        var registry = new ToolRegistry(config, new MatchRequestInput("Title", [new MatchFileInput("01.mkv", 1)]), client);
        var api = new OpenAiCompatibleClient(client, config);
        var progress = new List<ExecutionProgress>();

        ApiCallResult result = await api.SendAsync(
            "prompt",
            registry,
            (value, _) =>
            {
                progress.Add(value);
                return ValueTask.CompletedTask;
            },
            CancellationToken.None);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(new ApiUsage(30, 5, 2, 35), result.Usage);
        ExecutionProgress[] modelSteps = [.. progress.Where(item => item.Type == "model-complete")];
        Assert.Equal(2, modelSteps.Length);
        Assert.Equal(new ApiUsage(10, 2, 1, 12), modelSteps[0].Usage);
        Assert.Equal(new ApiUsage(20, 3, 1, 23), modelSteps[1].Usage);
        Assert.Contains(progress, item => item.Type == "tool-complete" && item.Message.Contains("tmdb__missing", StringComparison.Ordinal));
        ExecutionProgress[] requests = [.. progress.Where(item => item.Type == "model-start")];
        Assert.Equal(2, requests.Length);
        Assert.All(requests, item => Assert.Equal("/v1/responses", item.Endpoint));
        Assert.Contains("\"input\":\"prompt\"", requests[0].Content);
        Assert.Contains("function_call_output", requests[1].Content);
    }

    [Fact]
    public async Task FailedBgmPriorityCallCanContinueToTmdbAndFinalResult()
    {
        var config = new TesterConfig("https://example.test", "key", "model-x", ApiMode.Responses, "medium", false, 30, null, "http://bgm.test/mcp", "http://tmdb.test/mcp");
        var input = new MatchRequestInput("Title", [new MatchFileInput("E04.mkv", 1)], 123, null, "2023-01-24T21:02:56", 1, true, 4);
        var handler = new PubDateFallbackHandler();
        using var client = new HttpClient(handler);
        var registry = new ToolRegistry(config, input, client);
        var api = new OpenAiCompatibleClient(client, config);

        ApiCallResult result = await api.SendAsync("pubDate priority prompt", registry, CancellationToken.None);

        Assert.True(result.Success, result.ErrorMessage);
        ToolTimelineEntry[] calls = [.. result.ToolTimeline!.Where(entry => entry.Phase == "call")];
        Assert.Equal(["bgm__get-episodes", "tmdb__search-tv"], calls.Select(entry => entry.Name).ToArray());
        Assert.False(calls[0].Success);
        Assert.True(calls[1].Success);
        Assert.Equal(new ApiUsage(30, 6, null, 36), result.Usage);
    }

    private sealed class UsageRoundsHandler : HttpMessageHandler
    {
        private int _requestCount;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _requestCount++;
            string json = _requestCount == 1
                ? "{\"id\":\"resp_1\",\"output\":[{\"type\":\"function_call\",\"call_id\":\"call_1\",\"name\":\"tmdb__missing\",\"arguments\":\"{}\"}],\"usage\":{\"input_tokens\":10,\"output_tokens\":2,\"output_tokens_details\":{\"reasoning_tokens\":1},\"total_tokens\":12}}"
                : "{\"id\":\"resp_2\",\"output_text\":\"{}\",\"usage\":{\"input_tokens\":20,\"output_tokens\":3,\"output_tokens_details\":{\"reasoning_tokens\":1},\"total_tokens\":23}}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }

    private sealed class PubDateFallbackHandler : HttpMessageHandler
    {
        private int _openAiRound;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            string host = request.RequestUri!.Host;
            if (host == "example.test")
            {
                _openAiRound++;
                return _openAiRound switch
                {
                    1 => Json(HttpStatusCode.OK, "{\"id\":\"r1\",\"output\":[{\"type\":\"function_call\",\"call_id\":\"c1\",\"name\":\"bgm__get-episodes\",\"arguments\":\"{}\"}],\"usage\":{\"input_tokens\":10,\"output_tokens\":2,\"total_tokens\":12}}"),
                    2 => Json(HttpStatusCode.OK, "{\"id\":\"r2\",\"output\":[{\"type\":\"function_call\",\"call_id\":\"c2\",\"name\":\"tmdb__search-tv\",\"arguments\":\"{}\"}],\"usage\":{\"input_tokens\":10,\"output_tokens\":2,\"total_tokens\":12}}"),
                    _ => Json(HttpStatusCode.OK, "{\"id\":\"r3\",\"output_text\":\"{\\\"matched\\\":false}\",\"usage\":{\"input_tokens\":10,\"output_tokens\":2,\"total_tokens\":12}}")
                };
            }

            if (body.Contains("\"method\":\"initialize\"", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"protocolVersion\":\"2025-03-26\",\"serverInfo\":{\"name\":\"fake\"}}}", host);
            }
            if (body.Contains("notifications/initialized", StringComparison.Ordinal)) return Json(HttpStatusCode.OK, "{}");
            if (body.Contains("\"method\":\"tools/list\"", StringComparison.Ordinal))
            {
                string name = host == "bgm.test" ? "get-episodes" : "search-tv";
                return Json(HttpStatusCode.OK, $"{{\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{{\"tools\":[{{\"name\":\"{name}\",\"description\":\"fake\",\"inputSchema\":{{\"type\":\"object\"}}}}]}}}}");
            }
            if (body.Contains("\"method\":\"tools/call\"", StringComparison.Ordinal))
            {
                bool bgm = host == "bgm.test";
                return Json(HttpStatusCode.OK, $"{{\"jsonrpc\":\"2.0\",\"id\":3,\"result\":{{\"content\":[{{\"type\":\"text\",\"text\":\"{{}}\"}}],\"isError\":{bgm.ToString().ToLowerInvariant()}}}}}");
            }
            return Json(HttpStatusCode.NotFound, "{}");
        }

        private static HttpResponseMessage Json(HttpStatusCode statusCode, string json, string? session = null)
        {
            var response = new HttpResponseMessage(statusCode) { Content = new StringContent(json) };
            if (session is not null) response.Headers.TryAddWithoutValidation("Mcp-Session-Id", session);
            return response;
        }
    }

    private sealed class ToolOrderHandler : HttpMessageHandler
    {
        public string OpenAiRequestJson { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            if (request.RequestUri!.ToString().Contains("/v1/responses", StringComparison.Ordinal))
            {
                OpenAiRequestJson = body;
                return Json(HttpStatusCode.OK, "{\"id\":\"resp_1\",\"output_text\":\"{}\"}");
            }

            if (body.Contains("\"method\":\"initialize\"", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"protocolVersion\":\"2025-03-26\",\"serverInfo\":{\"name\":\"mcp-openapi-server\"}}}", "s1");
            }

            if (body.Contains("notifications/initialized", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, "{}");
            }

            if (body.Contains("\"method\":\"tools/list\"", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, "{\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{\"tools\":[{\"name\":\"list-api-endpoints\",\"description\":\"list\",\"inputSchema\":{\"type\":\"object\"}}]}}");
            }

            return Json(HttpStatusCode.NotFound, "{}");
        }

        private static HttpResponseMessage Json(HttpStatusCode statusCode, string json, string? session = null)
        {
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(json)
            };
            if (session is not null)
            {
                response.Headers.TryAddWithoutValidation("Mcp-Session-Id", session);
            }
            return response;
        }
    }

    private sealed class StatelessResponsesHandler : HttpMessageHandler
    {
        public int ResponsesRequestCount { get; private set; }
        public string StatelessRetryJson { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            if (request.RequestUri!.ToString().Contains("/v1/responses", StringComparison.Ordinal))
            {
                ResponsesRequestCount++;
                return ResponsesRequestCount switch
                {
                    1 => Json(HttpStatusCode.OK, "{\"id\":\"resp_1\",\"output\":[{\"type\":\"function_call\",\"call_id\":\"call_1\",\"name\":\"tmdb__list-api-endpoints\",\"arguments\":\"{}\"}]}"),
                    2 => Json(HttpStatusCode.BadRequest, "{\"error\":{\"message\":\"No tool call found for function call output with call_id call_1.\",\"type\":\"invalid_request_error\"}}"),
                    3 => ReturnStatelessSuccess(body),
                    _ => Json(HttpStatusCode.BadRequest, "{}")
                };
            }

            if (body.Contains("\"method\":\"initialize\"", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"protocolVersion\":\"2025-03-26\",\"serverInfo\":{\"name\":\"mcp-openapi-server\"}}}", "s1");
            }

            if (body.Contains("notifications/initialized", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, "{}");
            }

            if (body.Contains("\"method\":\"tools/list\"", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, "{\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{\"tools\":[{\"name\":\"list-api-endpoints\",\"description\":\"list\",\"inputSchema\":{\"type\":\"object\"}}]}}");
            }

            if (body.Contains("\"method\":\"tools/call\"", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, "{\"jsonrpc\":\"2.0\",\"id\":3,\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"{\\\"endpoints\\\":[]}\"}]}}");
            }

            return Json(HttpStatusCode.NotFound, "{}");
        }

        private HttpResponseMessage ReturnStatelessSuccess(string body)
        {
            StatelessRetryJson = body;
            return Json(HttpStatusCode.OK, "{\"id\":\"resp_2\",\"output_text\":\"{\\\"matched\\\":false}\"}");
        }

        private static HttpResponseMessage Json(HttpStatusCode statusCode, string json, string? session = null)
        {
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(json)
            };
            if (session is not null)
            {
                response.Headers.TryAddWithoutValidation("Mcp-Session-Id", session);
            }
            return response;
        }
    }
}
