using System.Net;
using AnimeGoNet.App.Notifications;
using AnimeGoNet.Data.Notifications;

namespace AnimeGoNet.App.Tests.Notifications;

public sealed class WebhookNotificationSenderTests
{
    [Theory]
    [InlineData("bark", "https://api.day.app", "device", null, "/push", "device_key")]
    [InlineData("generic", "https://notify.example/hook", null, null, "/hook", "metadata_failed")]
    [InlineData("discord", "https://discord.example/hook", null, null, "/hook", "**Anime**")]
    [InlineData("slack", "https://slack.example/hook", null, null, "/hook", "Anime")]
    [InlineData("telegram", "https://api.telegram.example", "123:token", "456", "/bot123:token/sendMessage", "chat_id")]
    [InlineData("serverchan", "https://sctapi.example", "send-key", null, "/send-key.send", "desp=")]
    [InlineData("pushplus", "https://pushplus.example/send", "push-token", "topic-a", "/send", "push-token")]
    public async Task UsesProviderNativeRequestShape(
        string provider,
        string endpoint,
        string? secret,
        string? target,
        string expectedPath,
        string expectedBody)
    {
        var handler = new CaptureHandler();
        using var sender = new WebhookNotificationSender(new HttpClient(handler));
        var options = provider switch
        {
            "bark" => "{\"group\":\"AnimeGoNet\",\"sound\":\"birdsong\",\"level\":\"timeSensitive\",\"auto_copy\":true}",
            "generic" => "{\"headers\":{\"X-Test\":\"yes\"},\"body_template\":\"{\\\"event\\\":{{event_json}}}\"}",
            _ => "{}",
        };
        var channel = new NotificationChannel(
            "channel", "Test", provider, true, endpoint, secret, target, options,
            ["metadata_failed"], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var value = new NotificationEvent(
            "event", "metadata_failed", "task", "Anime", "Metadata failed", "{}", DateTimeOffset.UtcNow);

        var result = await sender.SendAsync(channel, value);

        Assert.True(result.Succeeded);
        Assert.Equal(HttpStatusCode.OK, handler.StatusCode);
        Assert.Contains(expectedPath, handler.RequestUri!.AbsolutePath, StringComparison.Ordinal);
        Assert.Contains(expectedBody, handler.Body, StringComparison.Ordinal);
        if (provider == "generic") Assert.Equal("yes", handler.Header);
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string Body { get; private set; } = "";
        public string? Header { get; private set; }
        public HttpStatusCode StatusCode { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            Body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Header = request.Headers.TryGetValues("X-Test", out var values) ? values.Single() : null;
            StatusCode = HttpStatusCode.OK;
            return new HttpResponseMessage(StatusCode)
            {
                Content = new StringContent("{\"ok\":true}"),
            };
        }
    }
}
