using System.Net;
using System.Net.Http.Headers;
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

    public async Task<AiMetadataMatchCandidate> MatchAsync(
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

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.HttpTimeout);
        try
        {
            var prompt = AiMetadataPromptRenderer.LoadAndRender(input);
            var registry = new AiMetadataToolRegistry(
                httpClient,
                options,
                input,
                referenceHttpClient);
            await registry.InitializeAsync(timeout.Token).ConfigureAwait(false);
            var messages = new List<ChatMessageState>
            {
                new("user", prompt, null, null),
            };

            for (var round = 0; round <= MaxToolRounds; round++)
            {
                var responseJson = await SendWithRetryAsync(
                    BuildRequestJson(messages, registry.Tools),
                    timeout.Token).ConfigureAwait(false);
                var parsed = ParseResponse(responseJson);
                if (parsed.ToolCalls.Count == 0)
                {
                    if (string.IsNullOrWhiteSpace(parsed.Content))
                    {
                        throw new AiMetadataMatcherException(
                            MetadataFailureKind.Protocol,
                            "ai_response_content_missing");
                    }

                    return ParseCandidate(parsed.Content);
                }

                messages.Add(new ChatMessageState(
                    "assistant",
                    parsed.Content,
                    null,
                    parsed.ToolCalls));
                foreach (var call in parsed.ToolCalls)
                {
                    var output = await registry.CallAsync(
                        call.Name,
                        call.ArgumentsJson,
                        timeout.Token).ConfigureAwait(false);
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            throw new AiMetadataMatcherException(
                MetadataFailureKind.Network,
                "ai_http_timeout",
                exception);
        }
        catch (HttpRequestException exception)
        {
            throw new AiMetadataMatcherException(
                MetadataFailureKind.Network,
                "ai_network_error",
                exception);
        }
        catch (JsonException exception)
        {
            throw new AiMetadataMatcherException(
                MetadataFailureKind.Protocol,
                "ai_response_json_invalid",
                exception);
        }
        catch (InvalidDataException exception)
        {
            throw new AiMetadataMatcherException(
                MetadataFailureKind.Protocol,
                exception.Message == "response_too_large"
                    ? "ai_response_too_large"
                    : "ai_response_invalid",
                exception);
        }
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
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                BuildEndpoint(options.BaseUrl!));
            if (!string.IsNullOrWhiteSpace(options.ApiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue(
                    "Bearer",
                    options.ApiKey);
            }

            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            try
            {
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

    private static Uri BuildEndpoint(Uri baseUrl)
    {
        var absolute = baseUrl.AbsoluteUri.EndsWith('/')
            ? baseUrl
            : new Uri(baseUrl.AbsoluteUri + "/", UriKind.Absolute);
        return new Uri(absolute, "v1/chat/completions");
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

        return new ParsedChatResponse(content, calls);
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
        IReadOnlyList<AiFunctionCall> ToolCalls);
}
