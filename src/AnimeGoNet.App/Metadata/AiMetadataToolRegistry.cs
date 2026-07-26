using System.Collections.Concurrent;
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
    AiMetadataMatchInput input)
{
    private const int MaxToolArgumentsChars = 32_768;
    private const int MaxToolResponseBytes = 20_000;
    private readonly List<AiFunctionTool> _tools = [];
    private McpEndpointClient? _tmdb;
    private McpEndpointClient? _bangumi;

    public IReadOnlyList<AiFunctionTool> Tools => _tools;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        try
        {
            _tmdb = new McpEndpointClient("tmdb", options.TmdbMcpUrl, httpClient);
            _tools.AddRange(await _tmdb.InitializeAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsTransportOrProtocol(exception))
        {
            throw new AiMetadataMatcherException(
                MetadataFailureKind.RemoteService,
                "ai_tmdb_mcp_unavailable",
                exception);
        }

        if (input.BangumiSubjectId is not null)
        {
            try
            {
                _bangumi = new McpEndpointClient("bgm", options.BangumiMcpUrl, httpClient);
                _tools.AddRange(await _bangumi.InitializeAsync(cancellationToken).ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (IsTransportOrProtocol(exception))
            {
                _bangumi = null;
            }
        }

        if (input.AniDbAnimeId is not null)
        {
            _tools.Add(new AiFunctionTool(
                "lookup_anidb_tmdbtv",
                "Look up a reference TMDB TV Series ID for the current fixed AniDB ID. Takes no arguments. The result is only a candidate and must be verified with TMDB MCP.",
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
            if (name.StartsWith("tmdb__", StringComparison.Ordinal) && _tmdb is not null)
            {
                return await _tmdb.CallAsync(
                    name["tmdb__".Length..],
                    argumentsJson,
                    cancellationToken).ConfigureAwait(false);
            }

            if (name.StartsWith("bgm__", StringComparison.Ordinal) && _bangumi is not null)
            {
                return await _bangumi.CallAsync(
                    name["bgm__".Length..],
                    argumentsJson,
                    cancellationToken).ConfigureAwait(false);
            }

            if (name == "lookup_anidb_tmdbtv" && input.AniDbAnimeId is not null)
            {
                return await LookupAniDbAsync(cancellationToken).ConfigureAwait(false);
            }

            return ErrorJson("tool_not_available");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsTransportOrProtocol(exception))
        {
            return ErrorJson(exception is HttpRequestException
                ? "tool_network_error"
                : "tool_protocol_error");
        }
    }

    private async Task<string> LookupAniDbAsync(CancellationToken cancellationToken)
    {
        var url = options.AniDbMappingUrlTemplate.Replace(
            "{anidbid}",
            input.AniDbAnimeId!.Value.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
        using var response = await httpClient.GetAsync(
            url,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return """{"tmdbtv":null,"reason":"mapping unavailable"}""";
        }

        var raw = await ReadLimitedAsync(
            response.Content,
            MaxToolResponseBytes,
            cancellationToken).ConfigureAwait(false);
        try
        {
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

            return $$"""{"tmdbtv":{{tmdbId}},"note":"reference only; verify with TMDB MCP"}""";
        }
        catch (JsonException)
        {
            return """{"tmdbtv":null,"reason":"mapping JSON malformed"}""";
        }
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
        HttpClient httpClient)
    {
        private const int MaxMcpResponseBytes = 65_536;
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
            using var request = BuildRequest(method, writeParameters);
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            CaptureSession(response);
            response.EnsureSuccessStatusCode();
            var raw = await ReadLimitedAsync(
                response.Content,
                MaxMcpResponseBytes,
                cancellationToken).ConfigureAwait(false);
            var json = response.Content.Headers.ContentType?.MediaType == "text/event-stream"
                ? ExtractSseJson(raw)
                : raw;
            var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("error", out var error))
            {
                document.Dispose();
                throw new InvalidDataException("mcp_jsonrpc_error:" + error.GetRawText());
            }

            return document;
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
            using var response = await httpClient.SendAsync(
                request,
                cancellationToken).ConfigureAwait(false);
            CaptureSession(response);
            response.EnsureSuccessStatusCode();
        }

        private HttpRequestMessage BuildRequest(
            string method,
            Action<Utf8JsonWriter>? writeParameters)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            AddSession(request);
            request.Content = new StringContent(
                BuildJsonRpcPayload(++_requestId, method, writeParameters),
                Encoding.UTF8,
                "application/json");
            return request;
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
