using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Metadata;

namespace AnimeGoNet.App.Metadata;

internal sealed record AiFunctionTool(
    string Name,
    string Description,
    string ParametersJson);

internal sealed class AiMetadataToolRegistry(
    HttpClient httpClient,
    AiMatchingOptions options,
    AiMetadataMatchInput input,
    HttpClient? referenceHttpClient = null,
    AiMetadataDebugCapture? debugCapture = null)
{
    private const int MaxToolArgumentsChars = 32_768;
    private const int MaxToolResponseBytes = 20_000;
    private readonly List<AiFunctionTool> _tools = [];
    private readonly AiMetadataPromptFeatures _features = AiMetadataPromptFeatures.Resolve(input);
    private McpEndpointClient? _tmdb;
    private McpEndpointClient? _bangumi;
    private int _successfulTmdbToolCalls;
    private int _failedTmdbToolCalls;

    public IReadOnlyList<AiFunctionTool> Tools => _tools;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (_features.TmdbMcp)
        {
            try
            {
                _tmdb = new McpEndpointClient(
                    "tmdb",
                    options.TmdbMcpUrl,
                    httpClient,
                    debugCapture);
                _tools.AddRange(await _tmdb.InitializeAsync(cancellationToken).ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (IsTransportOrProtocol(exception))
            {
                throw ClassifyMcpFailure("tmdb", exception);
            }
        }

        if (_features.BangumiMcp)
        {
            try
            {
                _bangumi = new McpEndpointClient(
                    "bgm",
                    options.BangumiMcpUrl,
                    httpClient,
                    debugCapture);
                _tools.AddRange(await _bangumi.InitializeAsync(cancellationToken).ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (IsTransportOrProtocol(exception))
            {
                throw ClassifyMcpFailure("bangumi", exception);
            }
        }

        if (_features.AniDbLookup)
        {
            _tools.Add(new AiFunctionTool(
                "lookup_anidb_tmdbtv",
                "Look up a reference TMDB TV Series ID for the current fixed AniDB ID. Takes no arguments. The result is only a candidate and must pass final TMDB validation.",
                """{"type":"object","properties":{},"additionalProperties":false}"""));
        }

        if (_features.ImdbLookup)
        {
            _tools.Add(new AiFunctionTool(
                "lookup_imdb_tmdb_tv",
                "Look up TMDB TV Series candidates for the current fixed IMDb Title ID through TMDB MCP. Takes no arguments. Movie results are removed; every TV candidate still requires Series, Season, and Episode verification.",
                """{"type":"object","properties":{},"additionalProperties":false}"""));
        }
    }

    public async Task<string> CallAsync(
        string name,
        string argumentsJson,
        CancellationToken cancellationToken)
    {
        if (argumentsJson.Length > MaxToolArgumentsChars)
        {
            return ErrorJson("tool_arguments_too_large");
        }

        try
        {
            if (_features.TmdbMcp
                && name.StartsWith("tmdb__", StringComparison.Ordinal)
                && _tmdb is not null)
            {
                var rawName = name["tmdb__".Length..];
                if (rawName == "invoke-api-endpoint"
                    && TmdbFindArgumentsMatchBoundImdb(argumentsJson) is { } error)
                {
                    return ErrorJson(error);
                }

                var output = await _tmdb.CallAsync(
                    rawName,
                    argumentsJson,
                    cancellationToken).ConfigureAwait(false);
                if (McpToolReturnedError(output))
                {
                    _failedTmdbToolCalls++;
                }
                else
                {
                    _successfulTmdbToolCalls++;
                }
                return output;
            }

            if (_features.BangumiMcp
                && name.StartsWith("bgm__", StringComparison.Ordinal)
                && _bangumi is not null)
            {
                return await _bangumi.CallAsync(
                    name["bgm__".Length..],
                    argumentsJson,
                    cancellationToken).ConfigureAwait(false);
            }

            if (name == "lookup_anidb_tmdbtv" && _features.AniDbLookup)
            {
                return await LookupAniDbAsync(cancellationToken).ConfigureAwait(false);
            }

            if (name == "lookup_imdb_tmdb_tv"
                && _features.ImdbLookup
                && _tmdb is not null)
            {
                return await LookupImdbAsync(cancellationToken).ConfigureAwait(false);
            }

            return ErrorJson("tool_not_available");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsTransportOrProtocol(exception))
        {
            if (name.StartsWith("tmdb__", StringComparison.Ordinal)
                || name == "lookup_imdb_tmdb_tv")
            {
                throw ClassifyMcpFailure("tmdb", exception);
            }

            if (name.StartsWith("bgm__", StringComparison.Ordinal))
            {
                throw ClassifyMcpFailure("bangumi", exception);
            }

            throw new AiMetadataMatcherException(
                MetadataFailureKind.RemoteService,
                "ai_reference_tool_failed",
                exception);
        }
    }

    public void EnsureRequiredTmdbToolWasUsed()
    {
        if (!_features.TmdbMcp || _successfulTmdbToolCalls > 0)
        {
            return;
        }

        throw new AiMetadataMatcherException(
            _failedTmdbToolCalls > 0
                ? MetadataFailureKind.RemoteService
                : MetadataFailureKind.Protocol,
            _failedTmdbToolCalls > 0
                ? "ai_tmdb_mcp_tool_error"
                : "ai_tmdb_mcp_not_used");
    }

    private static bool McpToolReturnedError(string output)
    {
        try
        {
            using var document = JsonDocument.Parse(output);
            return document.RootElement.TryGetProperty("isError", out var isError)
                && isError.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return true;
        }
    }

    private static AiMetadataMatcherException ClassifyMcpFailure(
        string source,
        Exception exception)
    {
        var prefix = source == "tmdb" ? "ai_tmdb_mcp_" : "ai_bangumi_mcp_";
        if (exception is TaskCanceledException)
        {
            return new AiMetadataMatcherException(
                MetadataFailureKind.Network,
                prefix + "timeout",
                exception);
        }

        if (exception is HttpRequestException httpException)
        {
            if (httpException.StatusCode is { } statusCode)
            {
                return statusCode switch
                {
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                        new AiMetadataMatcherException(
                            MetadataFailureKind.Authentication,
                            prefix + "authentication_failed",
                            exception),
                    HttpStatusCode.TooManyRequests => new AiMetadataMatcherException(
                        MetadataFailureKind.RemoteService,
                        prefix + "rate_limited",
                        exception),
                    _ when (int)statusCode >= 500 => new AiMetadataMatcherException(
                        MetadataFailureKind.RemoteService,
                        prefix + "service_error",
                        exception),
                    _ => new AiMetadataMatcherException(
                        MetadataFailureKind.Protocol,
                        prefix + "http_rejected",
                        exception),
                };
            }

            var failureCode = httpException.HttpRequestError switch
            {
                HttpRequestError.NameResolutionError => "dns_error",
                HttpRequestError.ConnectionError
                    or HttpRequestError.SecureConnectionError
                    or HttpRequestError.ProxyTunnelError => "connection_error",
                _ => "network_error",
            };
            return new AiMetadataMatcherException(
                MetadataFailureKind.Network,
                prefix + failureCode,
                exception);
        }

        if (exception is InvalidDataException invalidData
            && invalidData.Message.StartsWith("mcp_sse_", StringComparison.Ordinal))
        {
            return new AiMetadataMatcherException(
                MetadataFailureKind.Protocol,
                prefix + "sse_error",
                exception);
        }

        return new AiMetadataMatcherException(
            MetadataFailureKind.Protocol,
            prefix + "protocol_error",
            exception);
    }

    private async Task<string> LookupAniDbAsync(CancellationToken cancellationToken)
    {
        var url = AiMatchingOptions.FixedAniDbMappingUrlTemplate.Replace(
            "{anidbid}",
            input.AniDbAnimeId!.Value.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
        var endpoint = new Uri(url, UriKind.Absolute);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var response = await (referenceHttpClient ?? httpClient).GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            var raw = await ReadLimitedAsync(
                response.Content,
                MaxToolResponseBytes,
                cancellationToken).ConfigureAwait(false);
            timer.Stop();
            debugCapture?.Record(
                "reference",
                "anidb_lookup",
                endpoint,
                null,
                (int)response.StatusCode,
                raw,
                timer.ElapsedMilliseconds,
                response.IsSuccessStatusCode ? null : "http_status");
            if (!response.IsSuccessStatusCode)
            {
                return """{"tmdbtv":null,"reason":"mapping unavailable"}""";
            }

            using var document = JsonDocument.Parse(raw);
            if (!document.RootElement.TryGetProperty("tmdbtv", out var value))
            {
                return """{"tmdbtv":null,"reason":"tmdbtv missing"}""";
            }

            var text = value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : value.GetRawText();
            if (!long.TryParse(
                text,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var tmdbId)
                || tmdbId <= 0)
            {
                return """{"tmdbtv":null,"reason":"tmdbtv empty or invalid"}""";
            }

            return $$"""{"tmdbtv":{{tmdbId}},"note":"reference only; final TMDB validation required"}""";
        }
        catch (JsonException)
        {
            return """{"tmdbtv":null,"reason":"mapping JSON malformed"}""";
        }
        catch (Exception exception) when (
            exception is HttpRequestException or InvalidDataException or OperationCanceledException)
        {
            timer.Stop();
            debugCapture?.Record(
                "reference",
                "anidb_lookup",
                endpoint,
                null,
                null,
                null,
                timer.ElapsedMilliseconds,
                exception.GetType().Name);
            throw;
        }
    }

    private async Task<string> LookupImdbAsync(CancellationToken cancellationToken)
    {
        var arguments = BuildImdbLookupArguments(input.ImdbTitleId!);
        var raw = await _tmdb!.CallAsync(
            "invoke-api-endpoint",
            arguments,
            cancellationToken).ConfigureAwait(false);
        return FilterTmdbFindResult(raw, input.ImdbTitleId!);
    }

    private string? TmdbFindArgumentsMatchBoundImdb(string argumentsJson)
    {
        try
        {
            using var arguments = JsonDocument.Parse(argumentsJson);
            if (arguments.RootElement.ValueKind != JsonValueKind.Object
                || !arguments.RootElement.TryGetProperty("endpoint", out var endpoint)
                || endpoint.ValueKind != JsonValueKind.String)
            {
                return "tmdb_invoke_arguments_invalid";
            }

            var path = endpoint.GetString()!;
            if (!path.Equals(
                    "/3/find/{external_id}",
                    StringComparison.OrdinalIgnoreCase)
                && !path.StartsWith("/3/find/", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (input.ImdbTitleId is null)
            {
                return "tmdb_find_reference_unavailable";
            }

            if (!arguments.RootElement.TryGetProperty("params", out var parameters)
                || parameters.ValueKind != JsonValueKind.Object
                || !parameters.TryGetProperty("external_source", out var source)
                || source.ValueKind != JsonValueKind.String
                || source.GetString() != "imdb_id")
            {
                return "tmdb_find_reference_mismatch";
            }

            var boundId = parameters.TryGetProperty("external_id", out var externalId)
                && externalId.ValueKind == JsonValueKind.String
                ? externalId.GetString()
                : path.Equals(
                    "/3/find/" + input.ImdbTitleId,
                    StringComparison.OrdinalIgnoreCase)
                    ? input.ImdbTitleId
                    : null;
            return boundId == input.ImdbTitleId
                ? null
                : "tmdb_find_reference_mismatch";
        }
        catch (JsonException)
        {
            return "tmdb_invoke_arguments_invalid";
        }
    }

    private static string BuildImdbLookupArguments(string imdbTitleId)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("endpoint", "/3/find/{external_id}");
            writer.WriteString("method", "GET");
            writer.WritePropertyName("params");
            writer.WriteStartObject();
            writer.WriteString("external_id", imdbTitleId);
            writer.WriteString("external_source", "imdb_id");
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string FilterTmdbFindResult(
        string rawMcpResult,
        string imdbTitleId)
    {
        try
        {
            using var mcp = JsonDocument.Parse(rawMcpResult);
            if (mcp.RootElement.TryGetProperty("isError", out var isError)
                && isError.ValueKind == JsonValueKind.True)
            {
                return WriteImdbLookupResult(
                    imdbTitleId,
                    [],
                    0,
                    "tmdb find unavailable");
            }

            if (!mcp.RootElement.TryGetProperty("content", out var content)
                || content.ValueKind != JsonValueKind.Array)
            {
                return WriteImdbLookupResult(
                    imdbTitleId,
                    [],
                    0,
                    "tmdb find response malformed");
            }

            var tvIds = new SortedSet<long>();
            var rejectedMovies = 0;
            foreach (var item in content.EnumerateArray())
            {
                if (!item.TryGetProperty("type", out var type)
                    || type.GetString() != "text"
                    || !item.TryGetProperty("text", out var text)
                    || text.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                using var payload = JsonDocument.Parse(text.GetString()!);
                if (payload.RootElement.TryGetProperty("tv_results", out var tvResults)
                    && tvResults.ValueKind == JsonValueKind.Array)
                {
                    foreach (var result in tvResults.EnumerateArray())
                    {
                        if (result.TryGetProperty("id", out var id)
                            && id.TryGetInt64(out var value)
                            && value > 0)
                        {
                            tvIds.Add(value);
                        }
                    }
                }

                if (payload.RootElement.TryGetProperty("movie_results", out var movieResults)
                    && movieResults.ValueKind == JsonValueKind.Array)
                {
                    rejectedMovies += movieResults.GetArrayLength();
                }
            }

            return WriteImdbLookupResult(
                imdbTitleId,
                tvIds,
                rejectedMovies,
                tvIds.Count == 0 ? "no TMDB TV candidate" : null);
        }
        catch (JsonException)
        {
            return WriteImdbLookupResult(
                imdbTitleId,
                [],
                0,
                "tmdb find response malformed");
        }
    }

    private static string WriteImdbLookupResult(
        string imdbTitleId,
        IEnumerable<long> tvIds,
        int rejectedMovies,
        string? reason)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("imdbid", imdbTitleId);
            writer.WritePropertyName("tmdb_tv_ids");
            writer.WriteStartArray();
            foreach (var id in tvIds)
            {
                writer.WriteNumberValue(id);
            }
            writer.WriteEndArray();
            writer.WriteNumber("movie_results_rejected", rejectedMovies);
            if (reason is not null)
            {
                writer.WriteString("reason", reason);
            }
            writer.WriteString(
                "note",
                "Reference candidates only; verify TMDB TV Series, Season, and Episode.");
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string ErrorJson(string code)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("error", code);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    internal static async Task<string> ReadLimitedAsync(
        HttpContent content,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > maxBytes)
        {
            throw new InvalidDataException("response_too_large");
        }

        await using var source = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var destination = new MemoryStream();
        var buffer = new byte[4096];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (destination.Length + read > maxBytes)
            {
                throw new InvalidDataException("response_too_large");
            }

            destination.Write(buffer, 0, read);
        }

        return Encoding.UTF8.GetString(destination.ToArray());
    }

    private static bool IsTransportOrProtocol(Exception exception) =>
        exception is HttpRequestException
            or TaskCanceledException
            or JsonException
            or InvalidDataException
            or ArgumentException;

    private sealed class McpEndpointClient(
        string source,
        Uri endpoint,
        HttpClient httpClient,
        AiMetadataDebugCapture? debugCapture)
    {
        private const int MaxMcpResponseBytes = 65_536;
        private const int MaxMcpSseEnvelopeBytes = 8 * 1024 * 1024;
        private static readonly ConcurrentDictionary<string, AiFunctionTool[]> ToolCache =
            new(StringComparer.Ordinal);
        private string? _sessionId;
        private int _requestId;

        public async Task<IReadOnlyList<AiFunctionTool>> InitializeAsync(
            CancellationToken cancellationToken)
        {
            using var initialized = await SendAsync(
                "initialize",
                WriteInitializeParameters,
                cancellationToken).ConfigureAwait(false);
            if (!initialized.RootElement.TryGetProperty("result", out _))
            {
                throw new InvalidDataException("mcp_initialize_result_missing");
            }

            await NotifyInitializedAsync(cancellationToken).ConfigureAwait(false);
            var cacheKey = source + "|" + endpoint.AbsoluteUri;
            if (ToolCache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }

            using var listed = await SendAsync(
                "tools/list",
                null,
                cancellationToken).ConfigureAwait(false);
            if (!listed.RootElement.TryGetProperty("result", out var result)
                || !result.TryGetProperty("tools", out var toolsElement)
                || toolsElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("mcp_tools_list_invalid");
            }

            var tools = new List<AiFunctionTool>();
            foreach (var tool in toolsElement.EnumerateArray())
            {
                var rawName = tool.GetProperty("name").GetString();
                if (string.IsNullOrWhiteSpace(rawName))
                {
                    throw new InvalidDataException("mcp_tool_name_missing");
                }

                var description = tool.TryGetProperty("description", out var descriptionElement)
                    ? descriptionElement.GetString() ?? rawName
                    : rawName;
                var schema = tool.TryGetProperty("inputSchema", out var schemaElement)
                    ? schemaElement.GetRawText()
                    : """{"type":"object","additionalProperties":true}""";
                tools.Add(new AiFunctionTool(
                    source + "__" + rawName,
                    NormalizeDescription(rawName, description),
                    NormalizeSchema(rawName, schema)));
            }

            var stored = tools.ToArray();
            ToolCache.TryAdd(cacheKey, stored);
            return stored;
        }

        public async Task<string> CallAsync(
            string name,
            string argumentsJson,
            CancellationToken cancellationToken)
        {
            using var response = await SendAsync(
                "tools/call",
                writer => WriteToolCallParameters(writer, name, argumentsJson),
                cancellationToken).ConfigureAwait(false);
            if (!response.RootElement.TryGetProperty("result", out var result))
            {
                throw new InvalidDataException("mcp_tool_result_missing");
            }

            var raw = result.GetRawText();
            return raw.Length <= MaxToolResponseBytes
                ? raw
                : raw[..MaxToolResponseBytes] + "...[truncated]";
        }

        private async Task<JsonDocument> SendAsync(
            string method,
            Action<Utf8JsonWriter>? writeParameters,
            CancellationToken cancellationToken)
        {
            var requestId = ++_requestId;
            using var request = BuildRequest(requestId, method, writeParameters);
            var requestBody = await request.Content!.ReadAsStringAsync(
                cancellationToken).ConfigureAwait(false);
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var responseCaptured = false;
            try
            {
                using var response = await httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);
                CaptureSession(response);
                var raw = await ReadLimitedAsync(
                    response.Content,
                    MaxMcpResponseBytes,
                    cancellationToken).ConfigureAwait(false);
                timer.Stop();
                debugCapture?.Record(
                    "mcp:" + source,
                    method,
                    endpoint,
                    requestBody,
                    (int)response.StatusCode,
                    raw,
                    timer.ElapsedMilliseconds,
                    response.IsSuccessStatusCode ? null : "http_status");
                responseCaptured = true;
                response.EnsureSuccessStatusCode();
                if (string.IsNullOrWhiteSpace(raw))
                {
                    if (method != "tools/call" || _sessionId is null)
                    {
                        throw new InvalidDataException("mcp_response_empty");
                    }

                    return await ReadSseResponseAsync(
                        requestId,
                        cancellationToken).ConfigureAwait(false);
                }

                var json = response.Content.Headers.ContentType?.MediaType == "text/event-stream"
                    ? ExtractSseJson(raw)
                    : raw;
                return ParseJsonRpc(json, requestId);
            }
            catch (Exception exception) when (!responseCaptured
                && exception is (HttpRequestException or OperationCanceledException))
            {
                timer.Stop();
                debugCapture?.Record(
                    "mcp:" + source,
                    method,
                    endpoint,
                    requestBody,
                    null,
                    null,
                    timer.ElapsedMilliseconds,
                    exception.GetType().Name);
                throw;
            }
        }

        private async Task<JsonDocument> ReadSseResponseAsync(
            int expectedRequestId,
            CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            AddSession(request);
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var responseCaptured = false;
            int? responseStatusCode = null;
            try
            {
                using var response = await httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);
                responseStatusCode = (int)response.StatusCode;
                CaptureSession(response);
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(
                    cancellationToken).ConfigureAwait(false);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                var data = new StringBuilder();
                var receivedBytes = 0;
                while (true)
                {
                    var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                    if (line is null)
                    {
                        throw new InvalidDataException("mcp_sse_ended_without_result");
                    }

                    receivedBytes = checked(receivedBytes + Encoding.UTF8.GetByteCount(line) + 1);
                    if (receivedBytes > MaxMcpSseEnvelopeBytes)
                    {
                        throw new InvalidDataException("mcp_sse_response_too_large");
                    }

                    if (line.Length == 0)
                    {
                        if (data.Length == 0)
                        {
                            continue;
                        }

                        var json = data.ToString();
                        data.Clear();
                        JsonDocument candidate;
                        try
                        {
                            candidate = JsonDocument.Parse(json);
                        }
                        catch (JsonException exception)
                        {
                            throw new InvalidDataException(
                                "mcp_sse_json_invalid",
                                exception);
                        }
                        using (candidate)
                        {
                            if (!candidate.RootElement.TryGetProperty("id", out var id)
                                || id.ValueKind != JsonValueKind.Number
                                || id.GetInt32() != expectedRequestId)
                            {
                                continue;
                            }

                            timer.Stop();
                            debugCapture?.Record(
                                "mcp:" + source,
                                "tools/call/sse",
                                endpoint,
                                null,
                                (int)response.StatusCode,
                                json,
                                timer.ElapsedMilliseconds,
                                null);
                            responseCaptured = true;
                            return ParseJsonRpc(json, expectedRequestId);
                        }
                    }

                    if (line.StartsWith("data:", StringComparison.Ordinal))
                    {
                        if (data.Length > 0)
                        {
                            data.AppendLine();
                        }
                        data.Append(line["data:".Length..].TrimStart());
                    }
                }
            }
            catch (Exception exception) when (!responseCaptured
                && exception is (HttpRequestException
                    or OperationCanceledException
                    or JsonException
                    or InvalidDataException))
            {
                timer.Stop();
                debugCapture?.Record(
                    "mcp:" + source,
                    "tools/call/sse",
                    endpoint,
                    null,
                    responseStatusCode,
                    null,
                    timer.ElapsedMilliseconds,
                    exception.GetType().Name);
                throw;
            }
        }

        private async Task NotifyInitializedAsync(CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            AddSession(request);
            request.Content = new StringContent(
                """{"jsonrpc":"2.0","method":"notifications/initialized","params":{}}""",
                Encoding.UTF8,
                "application/json");
            var requestBody = await request.Content.ReadAsStringAsync(
                cancellationToken).ConfigureAwait(false);
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var responseCaptured = false;
            try
            {
                using var response = await httpClient.SendAsync(
                    request,
                    cancellationToken).ConfigureAwait(false);
                CaptureSession(response);
                var raw = await ReadLimitedAsync(
                    response.Content,
                    MaxMcpResponseBytes,
                    cancellationToken).ConfigureAwait(false);
                timer.Stop();
                debugCapture?.Record(
                    "mcp:" + source,
                    "notifications/initialized",
                    endpoint,
                    requestBody,
                    (int)response.StatusCode,
                    raw,
                    timer.ElapsedMilliseconds,
                    response.IsSuccessStatusCode ? null : "http_status");
                responseCaptured = true;
                response.EnsureSuccessStatusCode();
            }
            catch (Exception exception) when (!responseCaptured
                && exception is (HttpRequestException or OperationCanceledException))
            {
                timer.Stop();
                debugCapture?.Record(
                    "mcp:" + source,
                    "notifications/initialized",
                    endpoint,
                    requestBody,
                    null,
                    null,
                    timer.ElapsedMilliseconds,
                    exception.GetType().Name);
                throw;
            }
        }

        private HttpRequestMessage BuildRequest(
            int requestId,
            string method,
            Action<Utf8JsonWriter>? writeParameters)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            AddSession(request);
            request.Content = new StringContent(
                BuildJsonRpcPayload(requestId, method, writeParameters),
                Encoding.UTF8,
                "application/json");
            return request;
        }

        private static JsonDocument ParseJsonRpc(string json, int expectedRequestId)
        {
            var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("id", out var id))
            {
                document.Dispose();
                throw new InvalidDataException("mcp_jsonrpc_id_missing");
            }

            if (id.ValueKind != JsonValueKind.Number || id.GetInt32() != expectedRequestId)
            {
                document.Dispose();
                throw new InvalidDataException("mcp_jsonrpc_id_mismatch");
            }

            if (document.RootElement.TryGetProperty("error", out var error))
            {
                document.Dispose();
                throw new InvalidDataException("mcp_jsonrpc_error:" + error.GetRawText());
            }

            return document;
        }

        private void AddSession(HttpRequestMessage request)
        {
            if (_sessionId is not null)
            {
                request.Headers.TryAddWithoutValidation("Mcp-Session-Id", _sessionId);
            }
        }

        private void CaptureSession(HttpResponseMessage response)
        {
            if (response.Headers.TryGetValues("Mcp-Session-Id", out var values))
            {
                _sessionId = values.FirstOrDefault();
            }
        }

        private static string BuildJsonRpcPayload(
            int id,
            string method,
            Action<Utf8JsonWriter>? writeParameters)
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteString("jsonrpc", "2.0");
                writer.WriteNumber("id", id);
                writer.WriteString("method", method);
                if (writeParameters is not null)
                {
                    writer.WritePropertyName("params");
                    writeParameters(writer);
                }

                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }

        private static void WriteInitializeParameters(Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            writer.WriteString("protocolVersion", "2025-03-26");
            writer.WritePropertyName("capabilities");
            writer.WriteStartObject();
            writer.WriteEndObject();
            writer.WritePropertyName("clientInfo");
            writer.WriteStartObject();
            writer.WriteString("name", "AnimeGoNet");
            writer.WriteString("version", "1.0");
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        private static void WriteToolCallParameters(
            Utf8JsonWriter writer,
            string name,
            string argumentsJson)
        {
            writer.WriteStartObject();
            writer.WriteString("name", name);
            writer.WritePropertyName("arguments");
            try
            {
                using var arguments = JsonDocument.Parse(argumentsJson);
                if (arguments.RootElement.ValueKind != JsonValueKind.Object)
                {
                    throw new JsonException();
                }

                arguments.RootElement.WriteTo(writer);
            }
            catch (JsonException)
            {
                writer.WriteStartObject();
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        private static string NormalizeDescription(string name, string description) =>
            name == "invoke-api-endpoint"
                ? description + " Use the exact endpoint template path returned by list-api-endpoints or get-api-endpoint-schema, with path/query/body values in params."
                : description;

        private static string NormalizeSchema(string name, string schema) =>
            name == "invoke-api-endpoint"
                ? """
                  {
                    "type":"object",
                    "properties":{
                      "endpoint":{"type":"string"},
                      "method":{"type":"string","enum":["GET","POST","PUT","PATCH","DELETE","OPTIONS","HEAD"]},
                      "params":{"type":"object","additionalProperties":true}
                    },
                    "required":["endpoint","method","params"],
                    "additionalProperties":false
                  }
                  """
                : schema;

        private static string ExtractSseJson(string raw)
        {
            var data = new StringBuilder();
            foreach (var line in raw.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("data:", StringComparison.Ordinal))
                {
                    data.Append(trimmed["data:".Length..].Trim());
                }
            }

            return data.Length > 0
                ? data.ToString()
                : throw new InvalidDataException("mcp_sse_data_missing");
        }
    }
}
