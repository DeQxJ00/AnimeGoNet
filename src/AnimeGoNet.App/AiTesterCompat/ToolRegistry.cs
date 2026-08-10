using System.Diagnostics;
using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AnimeGoNet.App.AiTesterCompat;

public sealed class ToolRegistry(TesterConfig config, MatchRequestInput input, HttpClient httpClient, McpMetadataCache? metadataCache = null)
{
    private const int MaxToolResponseChars = 20000;
    private readonly List<RegisteredTool> _tools = [];
    private readonly List<ToolTimelineEntry> _timeline = [];
    private McpEndpointClient? _bgm;
    private McpEndpointClient? _tmdb;
    private readonly McpMetadataCache _metadataCache = metadataCache ?? new McpMetadataCache();

    public IReadOnlyList<RegisteredTool> Tools => _tools;
    public IReadOnlyList<ToolTimelineEntry> Timeline => _timeline;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (config.EnableTmdbMcp)
        {
            _timeline.Add(new ToolTimelineEntry("tmdb", "mcp", "enabled", 0, true, SafeUrl(config.TmdbMcpUrl)));
            _tmdb = await TryInitializeMcpAsync("tmdb", config.TmdbMcpUrl, cancellationToken);
        }
        else
        {
            _timeline.Add(new ToolTimelineEntry("tmdb", "mcp", "disabled", 0, true, "TMDB MCP disabled; result is not TMDB verified and does not satisfy production acceptance rules."));
        }

        if (!config.EnableBgmMcp)
        {
            _timeline.Add(new ToolTimelineEntry("bgm", "mcp", "disabled", 0, true, SafeUrl(config.BgmMcpUrl)));
        }
        else if (input.Bgmid is null)
        {
            _timeline.Add(new ToolTimelineEntry("bgm", "mcp", "skipped", 0, true, "bgmid is null"));
        }
        else
        {
            _timeline.Add(new ToolTimelineEntry("bgm", "mcp", "enabled", 0, true, SafeUrl(config.BgmMcpUrl)));
            _bgm = await TryInitializeMcpAsync("bgm", config.BgmMcpUrl, cancellationToken);
        }

        if (!config.EnableAniDbLookup)
        {
            _timeline.Add(new ToolTimelineEntry("anidb", "lookup_anidb_tmdbtv", "disabled", 0, true, SafeUrl(config.AniDbMappingUrlTemplate)));
        }
        else if (input.Anidbid is null)
        {
            _timeline.Add(new ToolTimelineEntry("anidb", "lookup_anidb_tmdbtv", "skipped", 0, true, "anidbid is null"));
        }
        else
        {
            _tools.Add(RegisteredTool.Local("lookup_anidb_tmdbtv", "Look up a reference TMDB TV Series ID for the current fixed AniDB ID. Takes no arguments."));
            _timeline.Add(new ToolTimelineEntry("anidb", "lookup_anidb_tmdbtv", "registered", 0, true, SafeUrl(config.AniDbMappingUrlTemplate)));
        }
    }

    public async Task<string> CallAsync(string toolName, string argumentsJson, CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            string output;
            bool cacheHit = false;
            if (toolName == "lookup_anidb_tmdbtv")
            {
                output = await LookupAniDbAsync(cancellationToken);
            }
            else if (toolName.StartsWith("tmdb__", StringComparison.Ordinal) && _tmdb is not null)
            {
                (output, cacheHit) = await CallMcpToolAsync(_tmdb, toolName["tmdb__".Length..], argumentsJson, cancellationToken);
            }
            else if (toolName.StartsWith("bgm__", StringComparison.Ordinal) && _bgm is not null)
            {
                (output, cacheHit) = await CallMcpToolAsync(_bgm, toolName["bgm__".Length..], argumentsJson, cancellationToken);
            }
            else
            {
                output = JsonError($"Tool '{toolName}' is not available.");
            }

            stopwatch.Stop();
            bool toolSucceeded =
                (!toolName.StartsWith("tmdb__", StringComparison.Ordinal) && !toolName.StartsWith("bgm__", StringComparison.Ordinal)) ||
                IsSuccessfulToolResult(output);
            output = Trim(output);
            string displayOutput = RedactContentForDisplay(output);
            _timeline.Add(new ToolTimelineEntry(
                SourceOf(toolName),
                toolName,
                cacheHit ? "cache-hit" : "call",
                (long)stopwatch.Elapsed.TotalMilliseconds,
                toolSucceeded,
                cacheHit ? "reused cached endpoint catalog" : displayOutput,
                RedactContentForDisplay(argumentsJson),
                displayOutput));
            return output;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or ArgumentException)
        {
            stopwatch.Stop();
            string message = ex.Message;
            _timeline.Add(new ToolTimelineEntry(
                SourceOf(toolName),
                toolName,
                "call",
                (long)stopwatch.Elapsed.TotalMilliseconds,
                false,
                message,
                RedactContentForDisplay(argumentsJson),
                RedactContentForDisplay(JsonError(message))));
            return JsonError(message);
        }
    }

    private async Task<McpEndpointClient?> TryInitializeMcpAsync(string source, string url, CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            var client = new McpEndpointClient(source, url, httpClient, _timeline, _metadataCache);
            IReadOnlyList<RegisteredTool> tools = await client.InitializeAsync(cancellationToken);
            _tools.AddRange(tools);
            stopwatch.Stop();
            _timeline.Add(new ToolTimelineEntry(
                source,
                "tools/list",
                client.ToolsListCacheHit ? "cache-hit" : "discovered",
                (long)stopwatch.Elapsed.TotalMilliseconds,
                true,
                client.ToolsListCacheHit ? $"{tools.Count} cached tools" : $"{tools.Count} tools"));
            return client;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or ArgumentException)
        {
            stopwatch.Stop();
            _timeline.Add(new ToolTimelineEntry(source, "initialize", "error", (long)stopwatch.Elapsed.TotalMilliseconds, false, ex.Message));
            return null;
        }
    }

    private async Task<(string Output, bool CacheHit)> CallMcpToolAsync(
        McpEndpointClient client,
        string toolName,
        string argumentsJson,
        CancellationToken cancellationToken)
    {
        if (toolName == "list-api-endpoints" && HasNoArguments(argumentsJson))
        {
            if (_metadataCache.TryGetEndpointCatalog(client.CacheKey, out string? cached))
            {
                return (cached!, true);
            }

            string catalog = await client.CallToolAsync(toolName, argumentsJson, cancellationToken);
            if (IsSuccessfulToolResult(catalog))
            {
                _metadataCache.StoreEndpointCatalog(client.CacheKey, catalog);
            }
            return (catalog, false);
        }

        return (await client.CallToolAsync(toolName, argumentsJson, cancellationToken), false);
    }

    private static bool HasNoArguments(string argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson)) return true;
        try
        {
            using JsonDocument document = JsonDocument.Parse(argumentsJson);
            return document.RootElement.ValueKind == JsonValueKind.Object && !document.RootElement.EnumerateObject().Any();
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsSuccessfulToolResult(string result)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(result);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                (!document.RootElement.TryGetProperty("isError", out JsonElement isError) || isError.ValueKind != JsonValueKind.True);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task<string> LookupAniDbAsync(CancellationToken cancellationToken)
    {
        if (input.Anidbid is null)
        {
            return "{\"tmdbtv\":null,\"reason\":\"anidbid is not set\"}";
        }

        string url = config.AniDbMappingUrlTemplate.Replace("{anidbid}", input.Anidbid.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(Math.Min(config.TimeoutSeconds, 20)));
        using HttpResponseMessage response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        if (!response.IsSuccessStatusCode)
        {
            return "{\"tmdbtv\":null,\"reason\":\"mapping unavailable\"}";
        }

        string raw = await response.Content.ReadAsStringAsync(cts.Token);
        if (raw.Length > MaxToolResponseChars)
        {
            return "{\"tmdbtv\":null,\"reason\":\"mapping response too large\"}";
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(raw);
            if (!document.RootElement.TryGetProperty("tmdbtv", out JsonElement tmdbtv))
            {
                return "{\"tmdbtv\":null,\"reason\":\"tmdbtv missing\"}";
            }

            string? value = tmdbtv.ValueKind == JsonValueKind.String ? tmdbtv.GetString() : tmdbtv.GetRawText();
            if (string.IsNullOrWhiteSpace(value) || !long.TryParse(value, out long numeric) || numeric <= 0)
            {
                return "{\"tmdbtv\":null,\"reason\":\"tmdbtv empty or invalid\"}";
            }

            _timeline.Add(new ToolTimelineEntry("anidb", "lookup_anidb_tmdbtv", "candidate", 0, true, numeric.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            return "{\"tmdbtv\":" + numeric.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"note\":\"reference only; verify with TMDB MCP\"}";
        }
        catch (JsonException)
        {
            return "{\"tmdbtv\":null,\"reason\":\"mapping JSON malformed\"}";
        }
    }

    private static string SourceOf(string toolName) =>
        toolName.StartsWith("tmdb__", StringComparison.Ordinal) ? "tmdb" :
        toolName.StartsWith("bgm__", StringComparison.Ordinal) ? "bgm" :
        toolName == "lookup_anidb_tmdbtv" ? "anidb" : "unknown";

    private static string Trim(string value) => value.Length <= MaxToolResponseChars ? value : value[..MaxToolResponseChars] + "...[truncated]";

    private static string Redact(string value) => value.Replace("Bearer ", "Bearer [redacted]", StringComparison.OrdinalIgnoreCase);

    internal static string RedactContentForDisplay(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "{}";
        try
        {
            using JsonDocument document = JsonDocument.Parse(value);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                WriteRedactedJson(writer, document.RootElement);
            }
            return Trim(Encoding.UTF8.GetString(stream.ToArray()));
        }
        catch (JsonException)
        {
            return Trim(Redact(value));
        }
    }

    private static void WriteRedactedJson(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    if (IsSensitiveProperty(property.Name)) writer.WriteStringValue("[redacted]");
                    else WriteRedactedJson(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (JsonElement item in element.EnumerateArray()) WriteRedactedJson(writer, item);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(RedactNestedString(element.GetString() ?? ""));
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static string RedactNestedString(string value)
    {
        string trimmed = value.TrimStart();
        if (!trimmed.StartsWith('{') && !trimmed.StartsWith('[')) return Redact(value);
        try
        {
            using JsonDocument nested = JsonDocument.Parse(value);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream)) WriteRedactedJson(writer, nested.RootElement);
            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException)
        {
            return Redact(value);
        }
    }

    private static bool IsSensitiveProperty(string name) => name.Equals("api_key", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("apiKey", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("token", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("access_token", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("authorization", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("cookie", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("password", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("secret", StringComparison.OrdinalIgnoreCase);

    private static string SafeUrl(string value) => Redact(value);

    private static string JsonError(string message)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("error", message);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
public sealed class McpMetadataCache
{
    private readonly ConcurrentDictionary<string, RegisteredTool[]> _tools = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _endpointCatalogs = new(StringComparer.OrdinalIgnoreCase);

    internal bool TryGetTools(string key, out IReadOnlyList<RegisteredTool>? tools)
    {
        if (_tools.TryGetValue(key, out RegisteredTool[]? cached))
        {
            tools = cached;
            return true;
        }

        tools = null;
        return false;
    }

    internal void StoreTools(string key, IReadOnlyList<RegisteredTool> tools) => _tools[key] = [.. tools];

    internal bool TryGetEndpointCatalog(string key, out string? catalog) => _endpointCatalogs.TryGetValue(key, out catalog);

    internal void StoreEndpointCatalog(string key, string catalog) => _endpointCatalogs[key] = catalog;
}

public sealed record RegisteredTool(string Name, string Description, string ParametersJson)
{
    public static RegisteredTool Local(string name, string description) =>
        new(name, description, "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}");
}

internal sealed class McpEndpointClient(
    string source,
    string url,
    HttpClient httpClient,
    List<ToolTimelineEntry> timeline,
    McpMetadataCache metadataCache)
{
    private string? _sessionId;
    private int _id;

    public string CacheKey { get; } = source + "|" + url;
    public bool ToolsListCacheHit { get; private set; }

    public async Task<IReadOnlyList<RegisteredTool>> InitializeAsync(CancellationToken cancellationToken)
    {
        using JsonDocument init = await SendAsync("initialize", WriteInitializeParams, cancellationToken);

        timeline.Add(new ToolTimelineEntry(source, "initialize", "ok", 0, true, init.RootElement.GetProperty("result").GetRawText()));
        await NotifyInitializedAsync(cancellationToken);
        if (metadataCache.TryGetTools(CacheKey, out IReadOnlyList<RegisteredTool>? cachedTools))
        {
            ToolsListCacheHit = true;
            return cachedTools!;
        }

        using JsonDocument listed = await SendAsync("tools/list", null, cancellationToken);
        var tools = new List<RegisteredTool>();
        foreach (JsonElement tool in listed.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray())
        {
            string name = tool.GetProperty("name").GetString() ?? throw new ArgumentException("MCP tool name missing.");
            string description = tool.TryGetProperty("description", out JsonElement desc) ? desc.GetString() ?? name : name;
            string schema = tool.TryGetProperty("inputSchema", out JsonElement inputSchema)
                ? inputSchema.GetRawText()
                : "{\"type\":\"object\",\"additionalProperties\":true}";
            tools.Add(new RegisteredTool(source + "__" + name, NormalizeMcpToolDescription(name, description), NormalizeMcpToolSchema(name, schema)));
        }

        metadataCache.StoreTools(CacheKey, tools);
        return tools;
    }

    public async Task<string> CallToolAsync(string name, string argumentsJson, CancellationToken cancellationToken)
    {
        using JsonDocument result = await SendToolCallAsync(name, argumentsJson, cancellationToken);
        return result.RootElement.GetProperty("result").GetRawText();
    }

    private async Task<JsonDocument> SendToolCallAsync(string name, string argumentsJson, CancellationToken cancellationToken)
    {
        Task<JsonDocument>? sseTask = _sessionId is null ? null : ReadNextSseMessageAsync(cancellationToken);
        try
        {
            using var request = BuildRequest("tools/call", writer => WriteToolCallParams(writer, name, argumentsJson));
            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
            if (response.Headers.TryGetValues("Mcp-Session-Id", out IEnumerable<string>? values))
            {
                _sessionId = values.FirstOrDefault();
            }

            string raw = await response.Content.ReadAsStringAsync(cancellationToken);
            response.EnsureSuccessStatusCode();
            if (!string.IsNullOrWhiteSpace(raw))
            {
                string json = response.Content.Headers.ContentType?.MediaType == "text/event-stream" ? ExtractSseJson(raw) : raw;
                JsonDocument document = JsonDocument.Parse(json);
                if (document.RootElement.TryGetProperty("error", out JsonElement error))
                {
                    throw new ArgumentException(error.GetRawText());
                }

                return document;
            }

            if (sseTask is not null)
            {
                return await sseTask;
            }

            throw new ArgumentException("MCP tools/call returned an empty response.");
        }
        catch
        {
            if (sseTask is not null)
            {
                _ = sseTask.ContinueWith(static task => task.Dispose(), TaskContinuationOptions.OnlyOnRanToCompletion);
            }
            throw;
        }
    }

    private async Task NotifyInitializedAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (_sessionId is not null)
        {
            request.Headers.TryAddWithoutValidation("Mcp-Session-Id", _sessionId);
        }

        string json = "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\",\"params\":{}}";
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task<JsonDocument> SendAsync(string method, Action<Utf8JsonWriter>? writeParams, CancellationToken cancellationToken)
    {
        using var request = BuildRequest(method, writeParams);
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        if (response.Headers.TryGetValues("Mcp-Session-Id", out IEnumerable<string>? values))
        {
            _sessionId = values.FirstOrDefault();
        }

        string raw = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();
        string json = response.Content.Headers.ContentType?.MediaType == "text/event-stream" ? ExtractSseJson(raw) : raw;
        JsonDocument document = JsonDocument.Parse(json);
        if (document.RootElement.TryGetProperty("error", out JsonElement error))
        {
            throw new ArgumentException(error.GetRawText());
        }

        return document;
    }

    private HttpRequestMessage BuildRequest(string method, Action<Utf8JsonWriter>? writeParams)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (_sessionId is not null)
        {
            request.Headers.TryAddWithoutValidation("Mcp-Session-Id", _sessionId);
        }

        request.Content = new StringContent(BuildJsonRpcPayload(++_id, method, writeParams), Encoding.UTF8, "application/json");
        return request;
    }

    private async Task<JsonDocument> ReadNextSseMessageAsync(CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(30));
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.TryAddWithoutValidation("Mcp-Session-Id", _sessionId);
        using HttpResponseMessage response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var data = new StringBuilder();
        while (true)
        {
            string? line = await reader.ReadLineAsync(cts.Token);
            if (line is null) break;
            if (line.Length == 0)
            {
                if (data.Length > 0)
                {
                    return ParseJsonRpcDocument(data.ToString());
                }
                continue;
            }

            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                data.Append(line["data:".Length..].Trim());
            }
        }

        throw new ArgumentException("SSE response did not contain data.");
    }

    private static JsonDocument ParseJsonRpcDocument(string json)
    {
        JsonDocument document = JsonDocument.Parse(json);
        if (document.RootElement.TryGetProperty("error", out JsonElement error))
        {
            document.Dispose();
            throw new ArgumentException(error.GetRawText());
        }

        return document;
    }

    private static string BuildJsonRpcPayload(int id, string method, Action<Utf8JsonWriter>? writeParams)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("jsonrpc", "2.0");
            writer.WriteNumber("id", id);
            writer.WriteString("method", method);
            if (writeParams is not null)
            {
                writer.WritePropertyName("params");
                writeParams(writer);
            }
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteInitializeParams(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("protocolVersion", "2025-03-26");
        writer.WritePropertyName("capabilities");
        writer.WriteStartObject();
        writer.WriteEndObject();
        writer.WritePropertyName("clientInfo");
        writer.WriteStartObject();
        writer.WriteString("name", "AnimeGoNet.AiTester");
        writer.WriteString("version", "1.0");
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteToolCallParams(Utf8JsonWriter writer, string name, string argumentsJson)
    {
        writer.WriteStartObject();
        writer.WriteString("name", name);
        writer.WritePropertyName("arguments");
        if (string.IsNullOrWhiteSpace(argumentsJson))
        {
            writer.WriteStartObject();
            writer.WriteEndObject();
        }
        else
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(argumentsJson);
                if (document.RootElement.ValueKind == JsonValueKind.Object)
                {
                    document.RootElement.WriteTo(writer);
                }
                else
                {
                    writer.WriteStartObject();
                    writer.WriteEndObject();
                }
            }
            catch (JsonException)
            {
                writer.WriteStartObject();
                writer.WriteEndObject();
            }
        }
        writer.WriteEndObject();
    }

    private static string NormalizeMcpToolDescription(string name, string description)
    {
        if (name != "invoke-api-endpoint")
        {
            return description;
        }

        return description + " Use the exact endpoint template path listed by list-api-endpoints or get-api-endpoint-schema, and pass all path/query/body values inside params.";
    }

    private static string NormalizeMcpToolSchema(string name, string schema)
    {
        if (name != "invoke-api-endpoint")
        {
            return schema;
        }

        return """
            {
              "type": "object",
              "properties": {
                "endpoint": {
                  "type": "string",
                  "description": "Exact endpoint template path, for example /3/search/tv or /v0/subjects/{subject_id}."
                },
                "method": {
                  "type": "string",
                  "description": "HTTP method to use.",
                  "enum": [ "GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS", "HEAD" ]
                },
                "params": {
                  "type": "object",
                  "description": "All path, query, and JSON body parameters for the selected endpoint.",
                  "additionalProperties": true
                }
              },
              "required": [ "endpoint", "method", "params" ],
              "additionalProperties": false
            }
            """;
    }

    private static string ExtractSseJson(string raw)
    {
        foreach (string line in raw.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("data:", StringComparison.Ordinal))
            {
                return trimmed["data:".Length..].Trim();
            }
        }

        throw new ArgumentException("SSE response did not contain data.");
    }
}
