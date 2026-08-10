using System.Net;
using System.Net.Http.Headers;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Metadata;

namespace AnimeGoNet.App.Metadata;

public sealed class OpenAiCompatibleMetadataMatcher(
    HttpClient httpClient,
    AiMatchingOptions options,
    bool ownsHttpClient = false,
    HttpClient? referenceHttpClient = null,
    bool ownsReferenceHttpClient = false)
    : IAiMetadataMatcher, IDisposable
{
    private const int MaxToolRounds = 8;
    private const int MaxAiResponseBytes = 2 * 1024 * 1024;
    private static readonly HashSet<string> ForbiddenResultFields = new(StringComparer.Ordinal)
    {
        "title",
        "confidence",
        "air_date",
        "episode_title",
        "failure_stage",
        "failure_code",
        "matched_title",
        "season_number",
        "tmdb_episode_number",
        "episode_offset",
    };

    public void Dispose()
    {
        if (ownsHttpClient)
        {
            httpClient.Dispose();
        }

        if (ownsReferenceHttpClient
            && referenceHttpClient is not null
            && !ReferenceEquals(referenceHttpClient, httpClient))
        {
            referenceHttpClient.Dispose();
        }
    }

    public async Task<AiMetadataMatchResponse> MatchAsync(
        AiMetadataMatchInput input,
        CancellationToken cancellationToken = default)
    {
        if (options.BaseUrl is null || string.IsNullOrWhiteSpace(options.Model))
        {
            throw new AiMetadataMatcherException(
                MetadataFailureKind.Configuration,
                "ai_provider_not_configured");
        }

        if (AiMetadataResultValidator.ValidateStructure(
            input,
            new AiMetadataMatchCandidate(
                false,
                null,
                input.Files.Select(file => new AiMetadataFileCandidate(
                    file.Name,
                    false,
                    null,
                    null,
                    "input validation")).ToArray(),
                "input validation")) is { Kind: MetadataFailureKind.InvalidInput })
        {
            throw new AiMetadataMatcherException(
                MetadataFailureKind.InvalidInput,
                "ai_metadata_input_invalid");
        }

        var usage = new UsageAccumulator(options.Model);
        var trace = new List<AiMetadataTraceEvent>();
        var sequence = 0;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.HttpTimeout);
        try
        {
            var prompt = AiMetadataPromptRenderer.LoadAndRender(input);
            trace.Add(new(++sequence, "prompt_rendered", AiMetadataPromptRenderer.PromptVersion));
            var registry = new AiMetadataToolRegistry(
                httpClient,
                options,
                input,
                referenceHttpClient);
            await registry.InitializeAsync(timeout.Token).ConfigureAwait(false);
            trace.Add(new(++sequence, "tools_initialized", $"tools={registry.Tools.Count}"));
            if (options.ApiMode == AiApiMode.Responses)
            {
                string? previousResponseId = null;
                IReadOnlyList<ResponsesFunctionOutput>? pendingOutputs = null;
                for (var round = 0; round <= MaxToolRounds; round++)
                {
                    var requestTimer = Stopwatch.StartNew();
                    var responseJson = await SendWithRetryAsync(
                        BuildResponsesRequestJson(
                            prompt,
                            registry.Tools,
                            previousResponseId,
                            pendingOutputs),
                        usage,
                        timeout.Token).ConfigureAwait(false);
                    requestTimer.Stop();
                    var parsed = ParseResponsesResponse(responseJson);
                    usage.Add(parsed.Model, parsed.Usage, parsed.ToolCalls.Count);
                    trace.Add(new(
                        ++sequence,
                        "model_response",
                        $"api=responses; round={round + 1}; tool_calls={parsed.ToolCalls.Count}; content={(string.IsNullOrWhiteSpace(parsed.Content) ? "empty" : "present")}",
                        requestTimer.ElapsedMilliseconds));
                    if (parsed.ToolCalls.Count == 0)
                    {
                        if (string.IsNullOrWhiteSpace(parsed.Content))
                        {
                            throw new AiMetadataMatcherException(
                                MetadataFailureKind.Protocol,
                                "ai_response_content_missing");
                        }

                        return new AiMetadataMatchResponse(
                            ParseCandidate(parsed.Content),
                            usage.Snapshot())
                        {
                            RawOutput = parsed.Content,
                            Trace = trace.ToArray(),
                        };
                    }

                    if (string.IsNullOrWhiteSpace(parsed.ResponseId))
                    {
                        throw new AiMetadataMatcherException(
                            MetadataFailureKind.Protocol,
                            "ai_responses_id_missing");
                    }

                    previousResponseId = parsed.ResponseId;
                    var outputs = new List<ResponsesFunctionOutput>(parsed.ToolCalls.Count);
                    foreach (var call in parsed.ToolCalls)
                    {
                        var toolTimer = Stopwatch.StartNew();
                        var output = await registry.CallAsync(
                            call.Name,
                            call.ArgumentsJson,
                            timeout.Token).ConfigureAwait(false);
                        toolTimer.Stop();
                        trace.Add(new(
                            ++sequence,
                            "tool_call",
                            $"{call.Name}; arguments={TruncateForTrace(call.ArgumentsJson)}; output_bytes={Encoding.UTF8.GetByteCount(output)}",
                            toolTimer.ElapsedMilliseconds));
                        outputs.Add(new(call.Id, output));
                    }

                    pendingOutputs = outputs;
                }

                throw new AiMetadataMatcherException(
                    MetadataFailureKind.Protocol,
                    "ai_tool_round_limit_exceeded");
            }

            var messages = new List<ChatMessageState>
            {
                new("user", prompt, null, null),
            };

            for (var round = 0; round <= MaxToolRounds; round++)
            {
                var requestTimer = Stopwatch.StartNew();
                var responseJson = await SendWithRetryAsync(
                    BuildRequestJson(messages, registry.Tools),
                    usage,
                    timeout.Token).ConfigureAwait(false);
                requestTimer.Stop();
                var parsed = ParseResponse(responseJson);
                usage.Add(parsed.Model, parsed.Usage, parsed.ToolCalls.Count);
                trace.Add(new(
                    ++sequence,
                    "model_response",
                    $"round={round + 1}; tool_calls={parsed.ToolCalls.Count}; content={(string.IsNullOrWhiteSpace(parsed.Content) ? "empty" : "present")}",
                    requestTimer.ElapsedMilliseconds));
                if (parsed.ToolCalls.Count == 0)
                {
                    if (string.IsNullOrWhiteSpace(parsed.Content))
                    {
                        throw new AiMetadataMatcherException(
                            MetadataFailureKind.Protocol,
                            "ai_response_content_missing");
                    }

                    return new AiMetadataMatchResponse(
                        ParseCandidate(parsed.Content),
                        usage.Snapshot())
                    {
                        RawOutput = parsed.Content,
                        Trace = trace.ToArray(),
                    };
                }

                messages.Add(new ChatMessageState(
                    "assistant",
                    parsed.Content,
                    null,
                    parsed.ToolCalls));
                foreach (var call in parsed.ToolCalls)
                {
                    var toolTimer = Stopwatch.StartNew();
                    var output = await registry.CallAsync(
                        call.Name,
                        call.ArgumentsJson,
                        timeout.Token).ConfigureAwait(false);
                    toolTimer.Stop();
                    trace.Add(new(
                        ++sequence,
                        "tool_call",
                        $"{call.Name}; arguments={TruncateForTrace(call.ArgumentsJson)}; output_bytes={Encoding.UTF8.GetByteCount(output)}",
                        toolTimer.ElapsedMilliseconds));
                    messages.Add(new ChatMessageState(
                        "tool",
                        output,
                        call.Id,
                        null));
                }
            }

            throw new AiMetadataMatcherException(
                MetadataFailureKind.Protocol,
                "ai_tool_round_limit_exceeded");
        }
        catch (AiMetadataMatcherException exception) when (exception.Usage is null)
        {
            throw new AiMetadataMatcherException(
                exception.Kind,
                exception.SafeCode,
                exception,
                usage.Snapshot());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            throw new AiMetadataMatcherException(
                MetadataFailureKind.Network,
                "ai_http_timeout",
                exception,
                usage.Snapshot());
        }
        catch (HttpRequestException exception)
        {
            throw new AiMetadataMatcherException(
                MetadataFailureKind.Network,
                "ai_network_error",
                exception,
                usage.Snapshot());
        }
        catch (JsonException exception)
        {
            throw new AiMetadataMatcherException(
                MetadataFailureKind.Protocol,
                "ai_response_json_invalid",
                exception,
                usage.Snapshot());
        }
        catch (InvalidDataException exception)
        {
            throw new AiMetadataMatcherException(
                MetadataFailureKind.Protocol,
                exception.Message == "response_too_large"
                    ? "ai_response_too_large"
                    : "ai_response_invalid",
                exception,
                usage.Snapshot());
        }
    }

    private static string TruncateForTrace(string value)
    {
        const int limit = 2048;
        return value.Length <= limit ? value : string.Concat(value.AsSpan(0, limit), "…");
    }

    public static AiMetadataMatchCandidate ParseCandidate(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new AiMetadataMatcherException(
                    MetadataFailureKind.Protocol,
                    "ai_result_not_object");
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (ForbiddenResultFields.Contains(property.Name))
                {
                    throw new AiMetadataMatcherException(
                        MetadataFailureKind.Protocol,
                        "ai_legacy_result_field");
                }
            }

            return JsonSerializer.Deserialize(
                json,
                AiMetadataJsonContext.Default.AiMetadataMatchCandidate)
                ?? throw new AiMetadataMatcherException(
                    MetadataFailureKind.Protocol,
                    "ai_result_empty");
        }
        catch (JsonException exception)
        {
            throw new AiMetadataMatcherException(
                MetadataFailureKind.Protocol,
                "ai_result_json_invalid",
                exception);
        }
    }

    private async Task<string> SendWithRetryAsync(
        string json,
        UsageAccumulator usage,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                BuildEndpoint(options.BaseUrl!, options.ApiMode));
            if (!string.IsNullOrWhiteSpace(options.ApiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue(
                    "Bearer",
                    options.ApiKey);
            }

            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            try
            {
                usage.RegisterRequest();
                using var response = await httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);
                var raw = await AiMetadataToolRegistry.ReadLimitedAsync(
                    response.Content,
                    MaxAiResponseBytes,
                    cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    return raw;
                }

                if (attempt < options.RetryCount && IsRetryable(response.StatusCode))
                {
                    await DelayRetryAsync(attempt, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                throw StatusException(response.StatusCode);
            }
            catch (HttpRequestException) when (attempt < options.RetryCount)
            {
                await DelayRetryAsync(attempt, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static Uri BuildEndpoint(Uri baseUrl, AiApiMode mode)
    {
        var absolute = baseUrl.AbsoluteUri.EndsWith('/')
            ? baseUrl
            : new Uri(baseUrl.AbsoluteUri + "/", UriKind.Absolute);
        return new Uri(
            absolute,
            mode == AiApiMode.Responses ? "v1/responses" : "v1/chat/completions");
    }

    private static bool IsRetryable(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.TooManyRequests
        || (int)statusCode >= 500;

    private static AiMetadataMatcherException StatusException(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            ? new AiMetadataMatcherException(
                MetadataFailureKind.Authentication,
                "ai_authentication_failed")
            : new AiMetadataMatcherException(
                (int)statusCode >= 500 || statusCode == HttpStatusCode.TooManyRequests
                    ? MetadataFailureKind.RemoteService
                    : MetadataFailureKind.Protocol,
                statusCode == HttpStatusCode.TooManyRequests
                    ? "ai_rate_limited"
                    : (int)statusCode >= 500
                        ? "ai_remote_service_error"
                        : "ai_http_rejected");

    private static Task DelayRetryAsync(int attempt, CancellationToken cancellationToken) =>
        Task.Delay(TimeSpan.FromMilliseconds(250 * (1 << Math.Min(attempt, 4))), cancellationToken);

    private string BuildRequestJson(
        IReadOnlyList<ChatMessageState> messages,
        IReadOnlyList<AiFunctionTool> tools)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("model", options.Model);
            writer.WritePropertyName("messages");
            writer.WriteStartArray();
            foreach (var message in messages)
            {
                WriteMessage(writer, message);
            }

            writer.WriteEndArray();
            writer.WritePropertyName("tools");
            writer.WriteStartArray();
            foreach (var tool in tools)
            {
                writer.WriteStartObject();
                writer.WriteString("type", "function");
                writer.WritePropertyName("function");
                writer.WriteStartObject();
                writer.WriteString("name", tool.Name);
                writer.WriteString("description", tool.Description);
                writer.WritePropertyName("parameters");
                using (var schema = JsonDocument.Parse(tool.ParametersJson))
                {
                    schema.RootElement.WriteTo(writer);
                }

                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WritePropertyName("response_format");
            writer.WriteStartObject();
            writer.WriteString("type", "json_object");
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private string BuildResponsesRequestJson(
        string prompt,
        IReadOnlyList<AiFunctionTool> tools,
        string? previousResponseId,
        IReadOnlyList<ResponsesFunctionOutput>? outputs)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("model", options.Model);
            if (previousResponseId is not null)
            {
                writer.WriteString("previous_response_id", previousResponseId);
            }

            writer.WritePropertyName("input");
            if (outputs is null)
            {
                writer.WriteStringValue(prompt);
            }
            else
            {
                writer.WriteStartArray();
                foreach (var output in outputs)
                {
                    writer.WriteStartObject();
                    writer.WriteString("type", "function_call_output");
                    writer.WriteString("call_id", output.CallId);
                    writer.WriteString("output", output.Output);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }

            if (tools.Count > 0)
            {
                writer.WritePropertyName("tools");
                writer.WriteStartArray();
                foreach (var tool in tools)
                {
                    writer.WriteStartObject();
                    writer.WriteString("type", "function");
                    writer.WriteString("name", tool.Name);
                    writer.WriteString("description", tool.Description);
                    writer.WritePropertyName("parameters");
                    using var schema = JsonDocument.Parse(tool.ParametersJson);
                    schema.RootElement.WriteTo(writer);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteMessage(Utf8JsonWriter writer, ChatMessageState message)
    {
        writer.WriteStartObject();
        writer.WriteString("role", message.Role);
        if (message.Content is not null)
        {
            writer.WriteString("content", message.Content);
        }

        if (message.ToolCallId is not null)
        {
            writer.WriteString("tool_call_id", message.ToolCallId);
        }

        if (message.ToolCalls is not null)
        {
            writer.WritePropertyName("tool_calls");
            writer.WriteStartArray();
            foreach (var call in message.ToolCalls)
            {
                writer.WriteStartObject();
                writer.WriteString("id", call.Id);
                writer.WriteString("type", "function");
                writer.WritePropertyName("function");
                writer.WriteStartObject();
                writer.WriteString("name", call.Name);
                writer.WriteString("arguments", call.ArgumentsJson);
                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }

    private static ParsedChatResponse ParseResponse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array
            || choices.GetArrayLength() != 1
            || !choices[0].TryGetProperty("message", out var message))
        {
            throw new AiMetadataMatcherException(
                MetadataFailureKind.Protocol,
                choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 1
                    ? "ai_chat_response_ambiguous"
                    : "ai_chat_response_invalid");
        }

        string? content = null;
        if (message.TryGetProperty("content", out var contentElement)
            && contentElement.ValueKind == JsonValueKind.String)
        {
            content = contentElement.GetString();
        }

        var calls = new List<AiFunctionCall>();
        if (message.TryGetProperty("tool_calls", out var toolCalls)
            && toolCalls.ValueKind == JsonValueKind.Array)
        {
            foreach (var toolCall in toolCalls.EnumerateArray())
            {
                if (!toolCall.TryGetProperty("id", out var idElement)
                    || !toolCall.TryGetProperty("function", out var function)
                    || !function.TryGetProperty("name", out var nameElement)
                    || !function.TryGetProperty("arguments", out var argumentsElement))
                {
                    throw new AiMetadataMatcherException(
                        MetadataFailureKind.Protocol,
                        "ai_tool_call_invalid");
                }

                var id = idElement.GetString();
                var name = nameElement.GetString();
                var arguments = argumentsElement.GetString();
                if (string.IsNullOrWhiteSpace(id)
                    || string.IsNullOrWhiteSpace(name)
                    || arguments is null)
                {
                    throw new AiMetadataMatcherException(
                        MetadataFailureKind.Protocol,
                        "ai_tool_call_invalid");
                }

                calls.Add(new AiFunctionCall(id, name, arguments));
            }
        }

        var model = document.RootElement.TryGetProperty("model", out var modelElement)
            && modelElement.ValueKind == JsonValueKind.String
                ? modelElement.GetString()
                : null;
        ParsedUsage? usage = null;
        if (document.RootElement.TryGetProperty("usage", out var usageElement)
            && usageElement.ValueKind == JsonValueKind.Object)
        {
            usage = new ParsedUsage(
                ReadNonNegativeInt64(usageElement, "prompt_tokens"),
                ReadNonNegativeInt64(usageElement, "completion_tokens"),
                ReadNonNegativeInt64(usageElement, "total_tokens"));
        }

        return new ParsedChatResponse(content, calls, model, usage);
    }

    private static ParsedResponsesResponse ParseResponsesResponse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("output", out var output)
            || output.ValueKind != JsonValueKind.Array)
        {
            throw new AiMetadataMatcherException(
                MetadataFailureKind.Protocol,
                "ai_responses_response_invalid");
        }

        var calls = new List<AiFunctionCall>();
        string? content = root.TryGetProperty("output_text", out var directText)
            && directText.ValueKind == JsonValueKind.String
                ? directText.GetString()
                : null;
        foreach (var item in output.EnumerateArray())
        {
            if (item.TryGetProperty("type", out var type)
                && type.ValueKind == JsonValueKind.String
                && type.GetString() == "function_call")
            {
                if (!item.TryGetProperty("call_id", out var callId)
                    || !item.TryGetProperty("name", out var name)
                    || !item.TryGetProperty("arguments", out var arguments)
                    || callId.ValueKind != JsonValueKind.String
                    || name.ValueKind != JsonValueKind.String
                    || arguments.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(callId.GetString())
                    || string.IsNullOrWhiteSpace(name.GetString()))
                {
                    throw new AiMetadataMatcherException(
                        MetadataFailureKind.Protocol,
                        "ai_tool_call_invalid");
                }

                calls.Add(new(
                    callId.GetString()!,
                    name.GetString()!,
                    arguments.GetString()!));
                continue;
            }

            if (content is null
                && item.TryGetProperty("content", out var parts)
                && parts.ValueKind == JsonValueKind.Array)
            {
                foreach (var part in parts.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var text)
                        && text.ValueKind == JsonValueKind.String)
                    {
                        content = text.GetString();
                        break;
                    }
                }
            }
        }

        var responseId = root.TryGetProperty("id", out var id)
            && id.ValueKind == JsonValueKind.String
                ? id.GetString()
                : null;
        var model = root.TryGetProperty("model", out var modelElement)
            && modelElement.ValueKind == JsonValueKind.String
                ? modelElement.GetString()
                : null;
        ParsedUsage? usage = null;
        if (root.TryGetProperty("usage", out var usageElement)
            && usageElement.ValueKind == JsonValueKind.Object)
        {
            usage = new ParsedUsage(
                ReadNonNegativeInt64(usageElement, "input_tokens"),
                ReadNonNegativeInt64(usageElement, "output_tokens"),
                ReadNonNegativeInt64(usageElement, "total_tokens"));
        }

        return new ParsedResponsesResponse(
            responseId,
            content,
            calls,
            model,
            usage);
    }

    private static long? ReadNonNegativeInt64(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt64(out var parsed)
            || parsed < 0)
        {
            return null;
        }

        return parsed;
    }

    private sealed record ChatMessageState(
        string Role,
        string? Content,
        string? ToolCallId,
        IReadOnlyList<AiFunctionCall>? ToolCalls);

    private sealed record AiFunctionCall(
        string Id,
        string Name,
        string ArgumentsJson);

    private sealed record ParsedChatResponse(
        string? Content,
        IReadOnlyList<AiFunctionCall> ToolCalls,
        string? Model,
        ParsedUsage? Usage);

    private sealed record ParsedResponsesResponse(
        string? ResponseId,
        string? Content,
        IReadOnlyList<AiFunctionCall> ToolCalls,
        string? Model,
        ParsedUsage? Usage);

    private sealed record ResponsesFunctionOutput(
        string CallId,
        string Output);

    private sealed record ParsedUsage(
        long? PromptTokens,
        long? CompletionTokens,
        long? TotalTokens);

    private sealed class UsageAccumulator(string configuredModel)
    {
        private string _model = configuredModel;
        private long? _promptTokens;
        private long? _completionTokens;
        private long? _totalTokens;
        private int _requestCount;
        private int _toolCallCount;

        public void RegisterRequest() => _requestCount++;

        public void Add(string? model, ParsedUsage? usage, int toolCallCount)
        {
            if (!string.IsNullOrWhiteSpace(model))
            {
                _model = model;
            }

            _promptTokens = AddNullable(_promptTokens, usage?.PromptTokens);
            _completionTokens = AddNullable(_completionTokens, usage?.CompletionTokens);
            _totalTokens = AddNullable(_totalTokens, usage?.TotalTokens);
            _toolCallCount = checked(_toolCallCount + toolCallCount);
        }

        public AiMetadataProviderUsage Snapshot() => new(
            _model,
            _promptTokens,
            _completionTokens,
            _totalTokens,
            _requestCount,
            _toolCallCount);

        private static long? AddNullable(long? current, long? value) =>
            value is null ? current : checked((current ?? 0) + value.Value);
    }
}
