using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using AnimeGoNet.Data.Notifications;

namespace AnimeGoNet.App.Notifications;

public sealed class WebhookNotificationSender(HttpClient httpClient) : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public async Task<NotificationSendResult> SendAsync(
        NotificationChannel channel,
        NotificationEvent value,
        CancellationToken cancellationToken = default)
    {
        var timer = Stopwatch.StartNew();
        try
        {
            using var request = BuildRequest(channel, value);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Timeout);
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token).ConfigureAwait(false);
            var excerpt = await ReadExcerptAsync(response, timeout.Token).ConfigureAwait(false);
            return new NotificationSendResult(
                response.IsSuccessStatusCode,
                (int)response.StatusCode,
                response.IsSuccessStatusCode ? null : $"notification_http_{(int)response.StatusCode}",
                excerpt,
                timer.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failed("notification_timeout", timer);
        }
        catch (HttpRequestException)
        {
            return Failed("notification_connection_failed", timer);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Failed("notification_configuration_invalid", timer, exception.Message);
        }
    }

    private static HttpRequestMessage BuildRequest(NotificationChannel channel, NotificationEvent value)
    {
        var options = JsonNode.Parse(channel.OptionsJson)?.AsObject() ?? new JsonObject();
        return channel.Provider switch
        {
            "bark" => JsonRequest(BarkEndpoint(channel.EndpointUrl), BarkBody(channel, value, options)),
            "discord" => JsonRequest(channel.EndpointUrl, new JsonObject
            {
                ["content"] = $"**{value.Title}**\n{value.Body}",
            }),
            "slack" => JsonRequest(channel.EndpointUrl, new JsonObject
            {
                ["text"] = $"{value.Title}\n{value.Body}",
            }),
            "telegram" => JsonRequest(
                $"{channel.EndpointUrl.TrimEnd('/')}/bot{Require(channel.Secret, "bot token")}/sendMessage",
                new JsonObject
                {
                    ["chat_id"] = Require(channel.Target, "chat ID"),
                    ["text"] = $"{value.Title}\n\n{value.Body}",
                    ["disable_web_page_preview"] = true,
                }),
            "serverchan" => FormRequest(
                $"{channel.EndpointUrl.TrimEnd('/')}/{Require(channel.Secret, "SendKey")}.send",
                new Dictionary<string, string> { ["title"] = value.Title, ["desp"] = value.Body }),
            "pushplus" => JsonRequest(channel.EndpointUrl, new JsonObject
            {
                ["token"] = Require(channel.Secret, "token"),
                ["title"] = value.Title,
                ["content"] = value.Body,
                ["template"] = "txt",
                ["topic"] = channel.Target,
            }),
            "generic" => GenericRequest(channel, value, options),
            _ => throw new ArgumentException("Unsupported notification provider."),
        };
    }

    private static JsonObject BarkBody(
        NotificationChannel channel,
        NotificationEvent value,
        JsonObject options)
    {
        var body = new JsonObject
        {
            ["device_key"] = Require(channel.Secret, "Bark device key"),
            ["title"] = value.Title,
            ["body"] = value.Body,
        };
        CopyString(options, body, "group");
        CopyString(options, body, "sound");
        CopyString(options, body, "icon");
        CopyString(options, body, "url");
        CopyString(options, body, "level");
        CopyString(options, body, "copy");
        if (options["badge"]?.GetValue<int?>() is { } badge) body["badge"] = badge;
        if (options["auto_copy"]?.GetValue<bool?>() is { } autoCopy) body["autoCopy"] = autoCopy ? "1" : "0";
        return body;
    }

    private static HttpRequestMessage GenericRequest(
        NotificationChannel channel,
        NotificationEvent value,
        JsonObject options)
    {
        var template = options["body_template"]?.GetValue<string>()
            ?? "{\"event\":{{event_json}},\"title\":{{title_json}},\"body\":{{body_json}},\"task_id\":{{task_id_json}}}";
        var body = template
            .Replace("{{event_json}}", JsonValue.Create(value.EventType)!.ToJsonString(), StringComparison.Ordinal)
            .Replace("{{title_json}}", JsonValue.Create(value.Title)!.ToJsonString(), StringComparison.Ordinal)
            .Replace("{{body_json}}", JsonValue.Create(value.Body)!.ToJsonString(), StringComparison.Ordinal)
            .Replace("{{task_id_json}}", value.TaskId is null ? "null" : JsonValue.Create(value.TaskId)!.ToJsonString(), StringComparison.Ordinal);
        var request = new HttpRequestMessage(HttpMethod.Post, channel.EndpointUrl)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (options["headers"] is JsonObject headers)
        {
            foreach (var pair in headers)
            {
                var headerValue = pair.Value?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(headerValue)
                    || pair.Key.Equals("Host", StringComparison.OrdinalIgnoreCase)
                    || pair.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) continue;
                if (!request.Headers.TryAddWithoutValidation(pair.Key, headerValue))
                    request.Content.Headers.TryAddWithoutValidation(pair.Key, headerValue);
            }
        }
        return request;
    }

    private static HttpRequestMessage JsonRequest(string endpoint, JsonObject body) => new(
        HttpMethod.Post, endpoint)
    {
        Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
    };

    private static HttpRequestMessage FormRequest(string endpoint, Dictionary<string, string> body) => new(
        HttpMethod.Post, endpoint)
    {
        Content = new FormUrlEncodedContent(body),
    };

    private static string BarkEndpoint(string endpoint) =>
        endpoint.TrimEnd('/').EndsWith("/push", StringComparison.OrdinalIgnoreCase)
            ? endpoint.TrimEnd('/')
            : endpoint.TrimEnd('/') + "/push";

    private static string Require(string? value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"Notification {name} is required.")
            : value.Trim();

    private static void CopyString(JsonObject source, JsonObject target, string key)
    {
        if (source[key]?.GetValue<string>() is { Length: > 0 } value) target[key] = value;
    }

    private static async Task<string?> ReadExcerptAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is 0) return null;
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8, true, 1024, leaveOpen: false);
        var buffer = new char[2048];
        var count = await reader.ReadBlockAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
        return count == 0 ? null : new string(buffer, 0, count);
    }

    private static NotificationSendResult Failed(
        string code,
        Stopwatch timer,
        string? response = null) =>
        new(false, null, code, response, timer.ElapsedMilliseconds);

    public void Dispose() => httpClient.Dispose();
}
