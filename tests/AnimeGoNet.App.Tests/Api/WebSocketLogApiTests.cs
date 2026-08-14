using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using AnimeGoNet.App.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AnimeGoNet.App.Tests.Api;

public sealed class WebSocketLogApiTests
{
    [Fact]
    public async Task NonUpgradeRequestKeepsUpstreamEmptyResponseContract()
    {
        await using var app = await RunningApp.StartAsync();

        using var response = await app.Client.GetAsync("/websocket/log");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task StaticWebUiProvidesSafeLiveLogControls()
    {
        await using var app = await RunningApp.StartAsync();

        var html = await app.Client.GetStringAsync("/");
        var script = await app.Client.GetStringAsync("/app.js");

        Assert.Contains("id=\"live-log-stream\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"live-log-level\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"live-log-pause\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"live-log-reconnect\"", html, StringComparison.Ordinal);
        Assert.Contains("""new WebSocket(liveLogWebSocketUrl())""", script, StringComparison.Ordinal);
        Assert.Contains("\"/websocket/log\"", script, StringComparison.Ordinal);
        Assert.Contains("{ action: \"pause\" }", script, StringComparison.Ordinal);
        Assert.Contains(
            "liveLogPaused ? \"resume\" : \"pause\"",
            script,
            StringComparison.Ordinal);
        Assert.Contains("message.textContent = entry.message", script, StringComparison.Ordinal);
        Assert.Contains("description.textContent = value", script, StringComparison.Ordinal);
        Assert.DoesNotContain(
            """stream.innerHTML""",
            script,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task AccessKeyProtectsUpgradeAndAcceptsDirectOrLegacyHash()
    {
        const string accessKey = "websocket-test-access-key";
        await using var app = await RunningApp.StartAsync(accessKey);
        var endpoint = WebSocketUri(app);

        using (var unauthorized = new ClientWebSocket())
        {
            await Assert.ThrowsAsync<WebSocketException>(
                () => unauthorized.ConnectAsync(endpoint, CancellationToken.None));
        }

        using (var direct = new ClientWebSocket())
        {
            direct.Options.SetRequestHeader("X-AnimeGo-WebUI-Access-Key", accessKey);
            await direct.ConnectAsync(endpoint, CancellationToken.None);
            Assert.Equal(WebSocketState.Open, direct.State);
            await direct.CloseAsync(
                WebSocketCloseStatus.NormalClosure,
                "test complete",
                CancellationToken.None);
        }

        var hash = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(accessKey)));
        using var legacy = new ClientWebSocket();
        await legacy.ConnectAsync(
            WebSocketUri(app, $"webui_access_key={hash}"),
            CancellationToken.None);
        Assert.Equal(WebSocketState.Open, legacy.State);
        await legacy.CloseAsync(
            WebSocketCloseStatus.NormalClosure,
            "test complete",
            CancellationToken.None);
    }

    [Fact]
    public async Task StreamsRedactedLogsAndKeepsLegacyFrameEnvelope()
    {
        await using var app = await RunningApp.StartAsync();
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(WebSocketUri(app), CancellationToken.None);
        var logger = app.App.Services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("AnimeGoNet.Tests.Secret");

        logger.Log(
            LogLevel.Information,
            new EventId(77),
            "marker-ws-log https://tracker.invalid/private-passkey/file.torrent?token=query-secret Bearer bearer-secret password=plain-secret "
            + """{"api_key":"json-secret","cookie":"session-secret"}""",
            exception: null,
            static (state, _) => state);

        var frame = await ReceiveUntilAsync(
            socket,
            value => value.Contains("marker-ws-log", StringComparison.Ordinal));

        Assert.StartsWith(
            """{"type":"log","count":1}""",
            frame,
            StringComparison.Ordinal);
        Assert.Contains("[INF]", frame, StringComparison.Ordinal);
        Assert.Contains("(77)", frame, StringComparison.Ordinal);
        Assert.Contains(
            "https://tracker.invalid/<redacted>",
            frame,
            StringComparison.Ordinal);
        Assert.DoesNotContain("private-passkey", frame, StringComparison.Ordinal);
        Assert.DoesNotContain("query-secret", frame, StringComparison.Ordinal);
        Assert.DoesNotContain("bearer-secret", frame, StringComparison.Ordinal);
        Assert.DoesNotContain("plain-secret", frame, StringComparison.Ordinal);
        Assert.DoesNotContain("json-secret", frame, StringComparison.Ordinal);
        Assert.DoesNotContain("session-secret", frame, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PauseBuffersPerConnectionAndResumeFlushesInOrder()
    {
        await using var app = await RunningApp.StartAsync();
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(WebSocketUri(app), CancellationToken.None);
        var logger = app.App.Services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("AnimeGoNet.Tests.Pause");

        await SendAsync(socket, """{"action":"pause"}""");
        var pauseAck = await ReceiveUntilAsync(
            socket,
            value => value.Contains(
                "\"type\":\"control\"",
                StringComparison.Ordinal));
        Assert.Contains("\"action\":\"pause\"", pauseAck, StringComparison.Ordinal);

        logger.Log(
            LogLevel.Warning,
            default,
            "buffered-marker-one",
            exception: null,
            static (state, _) => state);
        logger.Log(
            LogLevel.Error,
            default,
            "buffered-marker-two",
            exception: null,
            static (state, _) => state);
        var pendingReceive = ReceiveTextAsync(socket, CancellationToken.None);
        var early = await Task.WhenAny(
            pendingReceive,
            Task.Delay(TimeSpan.FromMilliseconds(200)));
        Assert.NotSame(pendingReceive, early);

        await SendAsync(socket, """{"action":"resume"}""");
        var resumeAck = await pendingReceive;
        Assert.Contains("\"action\":\"resume\"", resumeAck, StringComparison.Ordinal);
        var buffered = await ReceiveUntilAsync(
            socket,
            value => value.Contains(
                "buffered-marker-one",
                StringComparison.Ordinal));
        Assert.StartsWith(
            """{"type":"log","count":3}""",
            buffered,
            StringComparison.Ordinal);
        Assert.Contains("buffered-marker-one", buffered, StringComparison.Ordinal);
        Assert.Contains("buffered-marker-two", buffered, StringComparison.Ordinal);
        Assert.True(
            buffered.IndexOf(
                "buffered-marker-one",
                StringComparison.Ordinal)
            < buffered.IndexOf(
                "buffered-marker-two",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task InvalidCommandReturnsSafeControlErrorAndStreamRemainsUsable()
    {
        await using var app = await RunningApp.StartAsync();
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(WebSocketUri(app), CancellationToken.None);

        await SendAsync(socket, """{"action":"not-supported"}""");
        var response = await ReceiveTextAsync(socket, CancellationToken.None);
        Assert.Equal(
            """{"type":"control","action":"invalid","status":"error","code":"unknown_action"}""",
            response);

        var logger = app.App.Services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("AnimeGoNet.Tests.AfterInvalid");
        logger.Log(
            LogLevel.Information,
            default,
            "after-invalid-marker",
            exception: null,
            static (state, _) => state);
        var frame = await ReceiveUntilAsync(
            socket,
            value => value.Contains(
                "after-invalid-marker",
                StringComparison.Ordinal));
        Assert.Contains("after-invalid-marker", frame, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PausedBufferKeepsOnlyLatestOneThousandLines()
    {
        using var hub = new WebSocketLogHub();
        using var subscription = hub.Subscribe();
        subscription.Pause();
        for (var index = 0; index < 1005; index++)
        {
            subscription.Publish($"buffer-line-{index:D4}");
        }

        subscription.Resume();
        Assert.True(subscription.Outbound.TryRead(out var frame));
        Assert.StartsWith(
            """{"type":"log","count":1000}""",
            frame,
            StringComparison.Ordinal);
        Assert.DoesNotContain("buffer-line-0000", frame, StringComparison.Ordinal);
        Assert.DoesNotContain("buffer-line-0004", frame, StringComparison.Ordinal);
        Assert.Contains("buffer-line-0005", frame, StringComparison.Ordinal);
        Assert.Contains("buffer-line-1004", frame, StringComparison.Ordinal);
        Assert.False(subscription.Outbound.TryRead(out _));
        await Task.CompletedTask;
    }

    private static Uri WebSocketUri(RunningApp app, string? query = null)
    {
        var builder = new UriBuilder(app.Client.BaseAddress!)
        {
            Scheme = "ws",
            Path = "/websocket/log",
            Query = query ?? string.Empty,
        };
        return builder.Uri;
    }

    private static async Task SendAsync(
        ClientWebSocket socket,
        string payload)
    {
        await socket.SendAsync(
            Encoding.UTF8.GetBytes(payload),
            WebSocketMessageType.Text,
            endOfMessage: true,
            CancellationToken.None);
    }

    private static async Task<string> ReceiveUntilAsync(
        ClientWebSocket socket,
        Func<string, bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var frame = await ReceiveTextAsync(socket, timeout.Token);
            if (predicate(frame))
            {
                return frame;
            }
        }
        throw new Xunit.Sdk.XunitException(
            "Expected WebSocket frame was not received.");
    }

    private static async Task<string> ReceiveTextAsync(
        ClientWebSocket socket,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[1024];
        using var payload = new MemoryStream();
        ValueWebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(
                buffer.AsMemory(),
                cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new Xunit.Sdk.XunitException(
                    "WebSocket closed before the expected text frame.");
            }
            Assert.Equal(WebSocketMessageType.Text, result.MessageType);
            payload.Write(buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);
        return Encoding.UTF8.GetString(payload.GetBuffer(), 0, (int)payload.Length);
    }
}
