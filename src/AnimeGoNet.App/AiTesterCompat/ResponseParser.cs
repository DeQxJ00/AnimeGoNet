using System.Text.Json;

namespace AnimeGoNet.App.AiTesterCompat;

public static class ResponseParser
{
    public static string? ExtractResponsesOutputText(string rawJson)
    {
        using JsonDocument document = JsonDocument.Parse(rawJson);
        JsonElement root = document.RootElement;

        if (root.TryGetProperty("output_text", out JsonElement direct) && direct.ValueKind == JsonValueKind.String)
        {
            return direct.GetString();
        }

        if (!root.TryGetProperty("output", out JsonElement output) || output.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (JsonElement item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out JsonElement content) || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (JsonElement contentItem in content.EnumerateArray())
            {
                if (contentItem.TryGetProperty("text", out JsonElement text) && text.ValueKind == JsonValueKind.String)
                {
                    return text.GetString();
                }
            }
        }

        return null;
    }

    public static string? ExtractChatOutputText(string rawJson)
    {
        using JsonDocument document = JsonDocument.Parse(rawJson);
        JsonElement root = document.RootElement;
        if (!root.TryGetProperty("choices", out JsonElement choices) || choices.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (JsonElement choice in choices.EnumerateArray())
        {
            if (choice.TryGetProperty("message", out JsonElement message) &&
                message.TryGetProperty("content", out JsonElement content) &&
                content.ValueKind == JsonValueKind.String)
            {
                return content.GetString();
            }
        }

        return null;
    }

    public static ApiUsage ExtractUsage(string rawJson, ApiMode mode)
    {
        using JsonDocument document = JsonDocument.Parse(rawJson);
        if (!document.RootElement.TryGetProperty("usage", out JsonElement usage) || usage.ValueKind != JsonValueKind.Object)
        {
            return ApiUsageParser.Unavailable;
        }

        return mode == ApiMode.Responses
            ? new ApiUsage(
                ApiUsageParser.GetInt(usage, "input_tokens"),
                ApiUsageParser.GetInt(usage, "output_tokens"),
                ApiUsageParser.GetNestedInt(usage, "output_tokens_details", "reasoning_tokens"),
                ApiUsageParser.GetInt(usage, "total_tokens"))
            : new ApiUsage(
                ApiUsageParser.GetInt(usage, "prompt_tokens"),
                ApiUsageParser.GetInt(usage, "completion_tokens"),
                ApiUsageParser.GetNestedInt(usage, "completion_tokens_details", "reasoning_tokens"),
                ApiUsageParser.GetInt(usage, "total_tokens"));
    }
}

public static class ApiUsageParser
{
    public static readonly ApiUsage Unavailable = new(null, null, null, null);

    public static int? GetInt(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out JsonElement property) && property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out int value)
            ? value
            : null;

    public static int? GetNestedInt(JsonElement element, string objectName, string propertyName) =>
        element.TryGetProperty(objectName, out JsonElement nested) && nested.ValueKind == JsonValueKind.Object
            ? GetInt(nested, propertyName)
        : null;
}
