using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

#pragma warning disable CA1822, CA1859 // Kept behavior-compatible with the validated tester.

namespace AnimeGoNet.App.AiTesterCompat;

public sealed class OpenAiCompatibleClient(HttpClient httpClient, TesterConfig config)
{
    private const int MaxToolRounds = 8;

    public async Task<ApiCallResult> SendAsync(string prompt, CancellationToken cancellationToken) =>
        await SendAsync(prompt, null, cancellationToken);

    public async Task<ApiCallResult> SendAsync(string prompt, ToolRegistry? tools, CancellationToken cancellationToken)
        => await SendAsync(prompt, tools, null, cancellationToken);

    public async Task<ApiCallResult> SendAsync(
        string prompt,
        ToolRegistry? tools,
        Func<ExecutionProgress, CancellationToken, ValueTask>? progress,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        string lastRaw = "";
        ApiUsage usage = ApiUsageParser.Unavailable;
        try
        {
            if (tools is not null)
            {
                await tools.InitializeAsync(cancellationToken);
                foreach (ToolTimelineEntry entry in tools.Timeline)
                {
                    await ReportAsync(progress, new ExecutionProgress("status", 0, $"{entry.Source}: {entry.Name} {entry.Phase}", null, entry), cancellationToken);
                }
            }

            if (config.Mode == ApiMode.Responses)
            {
                return await SendResponsesLoopAsync(prompt, tools, progress, stopwatch, cancellationToken);
            }

            return await SendChatLoopAsync(prompt, tools, progress, stopwatch, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or ArgumentException)
        {
            stopwatch.Stop();
            return new ApiCallResult(false, 0, lastRaw, null, usage, stopwatch.Elapsed, ex.Message, tools?.Timeline);
        }
    }

    public HttpRequestMessage CreateRequest(string prompt)
    {
        string path = config.Mode == ApiMode.Responses ? "v1/responses" : "v1/chat/completions";
        var request = new HttpRequestMessage(HttpMethod.Post, BuildEndpointUri(config.BaseUrl, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
        string json = config.Mode == ApiMode.Responses
            ? BuildResponsesRequestJson(prompt, null, null, null)
            : BuildChatRequestJson([new ChatLoopMessage("user", prompt, null, null)], null);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return request;
    }

    private async Task<ApiCallResult> SendResponsesLoopAsync(
        string prompt,
        ToolRegistry? registry,
        Func<ExecutionProgress, CancellationToken, ValueTask>? progress,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        string? previousResponseId = null;
        IReadOnlyList<FunctionOutput>? pendingOutputs = null;
        var statelessItems = new List<ResponsesStatelessItem>();
        bool useStatelessContinuation = false;
        string lastRaw = "";
        var usageSteps = new List<ApiUsage>();
        int modelStep = 0;

        for (int round = 0; round <= MaxToolRounds; round++)
        {
            string json = useStatelessContinuation && pendingOutputs is not null
                ? BuildResponsesStatelessContinuationJson(prompt, registry?.Tools, statelessItems, pendingOutputs)
                : BuildResponsesRequestJson(prompt, registry?.Tools, previousResponseId, pendingOutputs);
            modelStep++;
            await ReportAsync(progress, new ExecutionProgress("model-start", modelStep, $"模型第 {modelStep} 轮请求开始", Content: json, Endpoint: "/v1/responses"), cancellationToken);
            using HttpResponseMessage response = await SendJsonAsync("v1/responses", json, cancellationToken);
            lastRaw = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                if (!useStatelessContinuation &&
                    pendingOutputs is not null &&
                    lastRaw.Contains("No tool call found", StringComparison.OrdinalIgnoreCase))
                {
                    useStatelessContinuation = true;
                    string retryJson = BuildResponsesStatelessContinuationJson(prompt, registry?.Tools, statelessItems, pendingOutputs);
                    modelStep++;
                    await ReportAsync(progress, new ExecutionProgress("model-start", modelStep, $"模型第 {modelStep} 轮无状态重试开始", Content: retryJson, Endpoint: "/v1/responses"), cancellationToken);
                    using HttpResponseMessage retryResponse = await SendJsonAsync("v1/responses", retryJson, cancellationToken);
                    lastRaw = await retryResponse.Content.ReadAsStringAsync(cancellationToken);
                    if (!retryResponse.IsSuccessStatusCode)
                    {
                        stopwatch.Stop();
                        return new ApiCallResult(false, (int)retryResponse.StatusCode, lastRaw, null, SumUsage(usageSteps), stopwatch.Elapsed, BuildFailureMessage(retryResponse, lastRaw, registry), registry?.Timeline);
                    }

                    ApiUsage retryUsage = ResponseParser.ExtractUsage(lastRaw, ApiMode.Responses);
                    usageSteps.Add(retryUsage);
                    await ReportAsync(progress, new ExecutionProgress("model-complete", modelStep, $"模型第 {modelStep} 轮完成", retryUsage), cancellationToken);
                    previousResponseId = TryGetString(lastRaw, "id") ?? previousResponseId;
                    List<FunctionCall> retryCalls = ExtractResponsesFunctionCalls(lastRaw);
                    if (retryCalls.Count == 0)
                    {
                        stopwatch.Stop();
                        return new ApiCallResult(true, (int)retryResponse.StatusCode, lastRaw, ResponseParser.ExtractResponsesOutputText(lastRaw), SumUsage(usageSteps), stopwatch.Elapsed, null, registry?.Timeline);
                    }

                    if (registry is null)
                    {
                        stopwatch.Stop();
                        return new ApiCallResult(false, (int)retryResponse.StatusCode, lastRaw, null, SumUsage(usageSteps), stopwatch.Elapsed, "Model requested tools, but no local tool registry is available.", registry?.Timeline);
                    }

                    statelessItems.AddRange(pendingOutputs.Select(ResponsesStatelessItem.FromOutput));
                    statelessItems.AddRange(retryCalls.Select(ResponsesStatelessItem.FromCall));
                    pendingOutputs = await ExecuteCallsAsync(retryCalls, registry, progress, cancellationToken);
                    continue;
                }

                stopwatch.Stop();
                return new ApiCallResult(false, (int)response.StatusCode, lastRaw, null, SumUsage(usageSteps), stopwatch.Elapsed, BuildFailureMessage(response, lastRaw, registry), registry?.Timeline);
            }

            ApiUsage roundUsage = ResponseParser.ExtractUsage(lastRaw, ApiMode.Responses);
            usageSteps.Add(roundUsage);
            await ReportAsync(progress, new ExecutionProgress("model-complete", modelStep, $"模型第 {modelStep} 轮完成", roundUsage), cancellationToken);
            previousResponseId = TryGetString(lastRaw, "id") ?? previousResponseId;
            List<FunctionCall> calls = ExtractResponsesFunctionCalls(lastRaw);
            if (calls.Count == 0)
            {
                stopwatch.Stop();
                return new ApiCallResult(true, (int)response.StatusCode, lastRaw, ResponseParser.ExtractResponsesOutputText(lastRaw), SumUsage(usageSteps), stopwatch.Elapsed, null, registry?.Timeline);
            }

            if (registry is null)
            {
                stopwatch.Stop();
                return new ApiCallResult(false, (int)response.StatusCode, lastRaw, null, SumUsage(usageSteps), stopwatch.Elapsed, "Model requested tools, but no local tool registry is available.", registry?.Timeline);
            }

            if (pendingOutputs is not null)
            {
                statelessItems.AddRange(pendingOutputs.Select(ResponsesStatelessItem.FromOutput));
            }
            statelessItems.AddRange(calls.Select(ResponsesStatelessItem.FromCall));
            pendingOutputs = await ExecuteCallsAsync(calls, registry, progress, cancellationToken);
        }

        stopwatch.Stop();
        return new ApiCallResult(false, 0, lastRaw, null, SumUsage(usageSteps), stopwatch.Elapsed, "Tool call round limit exceeded.", registry?.Timeline);
    }

    private async Task<ApiCallResult> SendChatLoopAsync(
        string prompt,
        ToolRegistry? registry,
        Func<ExecutionProgress, CancellationToken, ValueTask>? progress,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        if (config.WebSearchEnabled)
        {
            stopwatch.Stop();
            return new ApiCallResult(false, 0, "", null, ApiUsageParser.Unavailable, stopwatch.Elapsed, "Hosted web_search fallback is unavailable in Chat Completions mode; use Responses mode or disable web_search.", registry?.Timeline);
        }

        var messages = new List<ChatLoopMessage> { new("user", prompt, null, null) };
        string lastRaw = "";
        var usageSteps = new List<ApiUsage>();
        int modelStep = 0;

        for (int round = 0; round <= MaxToolRounds; round++)
        {
            string json = BuildChatRequestJson(messages, registry?.Tools);
            modelStep++;
            await ReportAsync(progress, new ExecutionProgress("model-start", modelStep, $"模型第 {modelStep} 轮请求开始", Content: json, Endpoint: "/v1/chat/completions"), cancellationToken);
            using HttpResponseMessage response = await SendJsonAsync("v1/chat/completions", json, cancellationToken);
            lastRaw = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                stopwatch.Stop();
                return new ApiCallResult(false, (int)response.StatusCode, lastRaw, null, SumUsage(usageSteps), stopwatch.Elapsed, BuildFailureMessage(response, lastRaw, registry), registry?.Timeline);
            }

            ApiUsage roundUsage = ResponseParser.ExtractUsage(lastRaw, ApiMode.ChatCompletions);
            usageSteps.Add(roundUsage);
            await ReportAsync(progress, new ExecutionProgress("model-complete", modelStep, $"模型第 {modelStep} 轮完成", roundUsage), cancellationToken);
            List<FunctionCall> calls = ExtractChatFunctionCalls(lastRaw);
            if (calls.Count == 0)
            {
                stopwatch.Stop();
                return new ApiCallResult(true, (int)response.StatusCode, lastRaw, ResponseParser.ExtractChatOutputText(lastRaw), SumUsage(usageSteps), stopwatch.Elapsed, null, registry?.Timeline);
            }

            if (registry is null)
            {
                stopwatch.Stop();
                return new ApiCallResult(false, (int)response.StatusCode, lastRaw, null, SumUsage(usageSteps), stopwatch.Elapsed, "Model requested tools, but no local tool registry is available.", registry?.Timeline);
            }

            messages.Add(new ChatLoopMessage("assistant", null, null, calls));
            IReadOnlyList<FunctionOutput> outputs = await ExecuteCallsAsync(calls, registry, progress, cancellationToken);
            foreach (FunctionOutput output in outputs)
            {
                messages.Add(new ChatLoopMessage("tool", output.Output, output.CallId, null));
            }
        }

        stopwatch.Stop();
        return new ApiCallResult(false, 0, lastRaw, null, SumUsage(usageSteps), stopwatch.Elapsed, "Tool call round limit exceeded.", registry?.Timeline);
    }

    private async Task<IReadOnlyList<FunctionOutput>> ExecuteCallsAsync(
        IReadOnlyList<FunctionCall> calls,
        ToolRegistry registry,
        Func<ExecutionProgress, CancellationToken, ValueTask>? progress,
        CancellationToken cancellationToken)
    {
        var outputs = new List<FunctionOutput>(calls.Count);
        foreach (FunctionCall call in calls)
        {
            await ReportAsync(progress, new ExecutionProgress("tool-start", 0, $"调用工具 {call.Name}"), cancellationToken);
            string output = await registry.CallAsync(call.Name, call.ArgumentsJson, cancellationToken);
            outputs.Add(new FunctionOutput(call.CallId, output));
            ToolTimelineEntry? timeline = registry.Timeline.LastOrDefault(entry => entry.Name == call.Name && (entry.Phase == "call" || entry.Phase == "cache-hit"));
            await ReportAsync(progress, new ExecutionProgress("tool-complete", 0, $"工具 {call.Name} 完成", null, timeline), cancellationToken);
        }

        return outputs;
    }

    private static async ValueTask ReportAsync(
        Func<ExecutionProgress, CancellationToken, ValueTask>? progress,
        ExecutionProgress value,
        CancellationToken cancellationToken)
    {
        if (progress is not null) await progress(value, cancellationToken);
    }

    private static ApiUsage SumUsage(IReadOnlyList<ApiUsage> steps) => new(
        SumAvailable(steps.Select(step => step.InputTokens)),
        SumAvailable(steps.Select(step => step.OutputTokens)),
        SumAvailable(steps.Select(step => step.ReasoningTokens)),
        SumAvailable(steps.Select(step => step.TotalTokens)));

    private static int? SumAvailable(IEnumerable<int?> values)
    {
        int total = 0;
        bool any = false;
        foreach (int? value in values)
        {
            if (value is null) return null;
            total = checked(total + value.Value);
            any = true;
        }
        return any ? total : null;
    }

    private async Task<HttpResponseMessage> SendJsonAsync(string relativePath, string json, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, BuildEndpointUri(config.BaseUrl, relativePath));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return await httpClient.SendAsync(request, cancellationToken);
    }

    private string BuildResponsesRequestJson(string prompt, IReadOnlyList<RegisteredTool>? tools, string? previousResponseId, IReadOnlyList<FunctionOutput>? outputs)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("model", config.Model);
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
                foreach (FunctionOutput output in outputs)
                {
                    writer.WriteStartObject();
                    writer.WriteString("type", "function_call_output");
                    writer.WriteString("call_id", output.CallId);
                    writer.WriteString("output", output.Output);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }

            WriteReasoning(writer);
            WriteResponsesTools(writer, tools);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private string BuildResponsesStatelessContinuationJson(string prompt, IReadOnlyList<RegisteredTool>? tools, IReadOnlyList<ResponsesStatelessItem> items, IReadOnlyList<FunctionOutput> outputs)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("model", config.Model);
            writer.WritePropertyName("input");
            writer.WriteStartArray();
            writer.WriteStartObject();
            writer.WriteString("role", "user");
            writer.WriteString("content", prompt);
            writer.WriteEndObject();
            foreach (ResponsesStatelessItem item in items)
            {
                item.WriteTo(writer);
            }
            foreach (FunctionOutput output in outputs)
            {
                ResponsesStatelessItem.FromOutput(output).WriteTo(writer);
            }
            writer.WriteEndArray();
            WriteReasoning(writer);
            WriteResponsesTools(writer, tools);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private string BuildChatRequestJson(IReadOnlyList<ChatLoopMessage> messages, IReadOnlyList<RegisteredTool>? tools)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("model", config.Model);
            writer.WritePropertyName("messages");
            writer.WriteStartArray();
            foreach (ChatLoopMessage message in messages)
            {
                writer.WriteStartObject();
                writer.WriteString("role", message.Role);
                if (message.Role == "tool")
                {
                    writer.WriteString("tool_call_id", message.ToolCallId);
                    writer.WriteString("content", message.Content);
                }
                else if (message.ToolCalls is not null)
                {
                    writer.WriteNull("content");
                    writer.WritePropertyName("tool_calls");
                    WriteChatToolCalls(writer, message.ToolCalls);
                }
                else
                {
                    writer.WriteString("content", message.Content);
                }
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            WriteReasoning(writer);
            writer.WritePropertyName("response_format");
            writer.WriteStartObject();
            writer.WriteString("type", "json_object");
            writer.WriteEndObject();
            WriteChatTools(writer, tools);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private void WriteReasoning(Utf8JsonWriter writer)
    {
        if (config.ReasoningEffort is null) return;
        writer.WritePropertyName("reasoning");
        writer.WriteStartObject();
        writer.WriteString("effort", config.ReasoningEffort);
        writer.WriteEndObject();
    }

    private void WriteResponsesTools(Utf8JsonWriter writer, IReadOnlyList<RegisteredTool>? tools)
    {
        bool hasTools = tools is { Count: > 0 } || config.WebSearchEnabled;
        if (!hasTools) return;
        writer.WritePropertyName("tools");
        writer.WriteStartArray();
        if (tools is not null)
        {
            foreach (RegisteredTool tool in tools)
            {
                writer.WriteStartObject();
                writer.WriteString("type", "function");
                writer.WriteString("name", tool.Name);
                writer.WriteString("description", tool.Description);
                writer.WritePropertyName("parameters");
                WriteRawJson(writer, tool.ParametersJson);
                writer.WriteEndObject();
            }
        }
        if (config.WebSearchEnabled)
        {
            writer.WriteStartObject();
            writer.WriteString("type", "web_search_preview");
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WriteChatTools(Utf8JsonWriter writer, IReadOnlyList<RegisteredTool>? tools)
    {
        if (tools is not { Count: > 0 }) return;
        writer.WritePropertyName("tools");
        writer.WriteStartArray();
        foreach (RegisteredTool tool in tools)
        {
            writer.WriteStartObject();
            writer.WriteString("type", "function");
            writer.WritePropertyName("function");
            writer.WriteStartObject();
            writer.WriteString("name", tool.Name);
            writer.WriteString("description", tool.Description);
            writer.WritePropertyName("parameters");
            WriteRawJson(writer, tool.ParametersJson);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WriteChatToolCalls(Utf8JsonWriter writer, IReadOnlyList<FunctionCall> calls)
    {
        writer.WriteStartArray();
        foreach (FunctionCall call in calls)
        {
            writer.WriteStartObject();
            writer.WriteString("id", call.CallId);
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

    private static void WriteRawJson(Utf8JsonWriter writer, string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        document.RootElement.WriteTo(writer);
    }

    private static List<FunctionCall> ExtractResponsesFunctionCalls(string raw)
    {
        using JsonDocument document = JsonDocument.Parse(raw);
        var calls = new List<FunctionCall>();
        if (!document.RootElement.TryGetProperty("output", out JsonElement output) || output.ValueKind != JsonValueKind.Array) return calls;
        foreach (JsonElement item in output.EnumerateArray())
        {
            if (item.TryGetProperty("type", out JsonElement type) && type.GetString() == "function_call")
            {
                string callId = item.GetProperty("call_id").GetString() ?? item.GetProperty("id").GetString() ?? Guid.NewGuid().ToString("N");
                string name = item.GetProperty("name").GetString() ?? "";
                string arguments = item.TryGetProperty("arguments", out JsonElement args) ? args.GetString() ?? "{}" : "{}";
                calls.Add(new FunctionCall(callId, name, arguments));
            }
        }
        return calls;
    }

    private static List<FunctionCall> ExtractChatFunctionCalls(string raw)
    {
        using JsonDocument document = JsonDocument.Parse(raw);
        var calls = new List<FunctionCall>();
        if (!document.RootElement.TryGetProperty("choices", out JsonElement choices) || choices.ValueKind != JsonValueKind.Array) return calls;
        foreach (JsonElement choice in choices.EnumerateArray())
        {
            if (!choice.TryGetProperty("message", out JsonElement message) || !message.TryGetProperty("tool_calls", out JsonElement toolCalls)) continue;
            foreach (JsonElement toolCall in toolCalls.EnumerateArray())
            {
                string id = toolCall.GetProperty("id").GetString() ?? Guid.NewGuid().ToString("N");
                JsonElement function = toolCall.GetProperty("function");
                string name = function.GetProperty("name").GetString() ?? "";
                string arguments = function.TryGetProperty("arguments", out JsonElement args) ? args.GetString() ?? "{}" : "{}";
                calls.Add(new FunctionCall(id, name, arguments));
            }
        }
        return calls;
    }

    private static string? TryGetString(string raw, string property)
    {
        using JsonDocument document = JsonDocument.Parse(raw);
        return document.RootElement.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static Uri BuildEndpointUri(string baseUrl, string relativePath) =>
        new(new Uri(baseUrl.TrimEnd('/') + "/", UriKind.Absolute), relativePath);

    private static string BuildFailureMessage(HttpResponseMessage response, string raw, ToolRegistry? registry)
    {
        string message = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
        try
        {
            ErrorEnvelope? error = JsonSerializer.Deserialize(raw, AiTesterJsonContext.Default.ErrorEnvelope);
            if (!string.IsNullOrWhiteSpace(error?.Error?.Message)) message += $": {error.Error.Message}";
        }
        catch (JsonException) { if (!string.IsNullOrWhiteSpace(raw)) message += $": {raw}"; }
        if (raw.Contains("reasoning", StringComparison.OrdinalIgnoreCase)) message += " Compatible endpoint may not support reasoning; retry with --reasoning-effort none.";
        if (raw.Contains("tool", StringComparison.OrdinalIgnoreCase) && registry?.Tools.Count > 0) message += " Compatible endpoint may not support function tools.";
        return message;
    }
}

internal sealed record FunctionCall(string CallId, string Name, string ArgumentsJson);
internal sealed record FunctionOutput(string CallId, string Output);
internal sealed record ChatLoopMessage(string Role, string? Content, string? ToolCallId, IReadOnlyList<FunctionCall>? ToolCalls);

internal sealed record ResponsesStatelessItem(string Type, string CallId, string? Name, string? ArgumentsJson, string? Output)
{
    public static ResponsesStatelessItem FromCall(FunctionCall call) =>
        new("function_call", call.CallId, call.Name, call.ArgumentsJson, null);

    public static ResponsesStatelessItem FromOutput(FunctionOutput output) =>
        new("function_call_output", output.CallId, null, null, output.Output);

    public void WriteTo(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("type", Type);
        writer.WriteString("call_id", CallId);
        if (Type == "function_call")
        {
            writer.WriteString("name", Name);
            writer.WriteString("arguments", ArgumentsJson ?? "{}");
        }
        else
        {
            writer.WriteString("output", Output ?? "");
        }
        writer.WriteEndObject();
    }
}
