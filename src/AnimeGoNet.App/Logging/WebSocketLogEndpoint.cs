using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace AnimeGoNet.App.Logging;

internal static partial class WebSocketLogEndpoint
{
    private const int MaximumCommandBytes = 4096;
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(2);

    internal static void Map(WebApplication app) =>
        app.MapGet("/websocket/log", HandleAsync);

    private static async Task HandleAsync(
        HttpContext context,
        WebSocketLogHub hub,
        ILoggerFactory loggerFactory,
        IHostApplicationLifetime applicationLifetime)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentLength = 0;
            return;
        }

        using var subscription = hub.Subscribe();
        using var socket = await context.WebSockets
            .AcceptWebSocketAsync()
            .ConfigureAwait(false);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            context.RequestAborted,
            applicationLifetime.ApplicationStopping);
        var logger = loggerFactory.CreateLogger("AnimeGoNet.WebSocketLog");
        var sender = SendAsync(socket, subscription, linked.Token);
        var receiver = ReceiveAsync(
            socket,
            subscription,
            logger,
            linked.Token);
        try
        {
            await Task.WhenAny(sender, receiver).ConfigureAwait(false);
        }
        finally
        {
            linked.Cancel();
            await IgnoreCancellationAsync(sender).ConfigureAwait(false);
            await IgnoreCancellationAsync(receiver).ConfigureAwait(false);
            await CloseAsync(socket).ConfigureAwait(false);
        }
    }

    private static async Task SendAsync(
        WebSocket socket,
        WebSocketLogSubscription subscription,
        CancellationToken cancellationToken)
    {
        await foreach (var payload in subscription.Outbound.ReadAllAsync(
            cancellationToken).ConfigureAwait(false))
        {
            var bytes = Encoding.UTF8.GetBytes(payload);
            await socket.SendAsync(
                bytes,
                WebSocketMessageType.Text,
                endOfMessage: true,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task ReceiveAsync(
        WebSocket socket,
        WebSocketLogSubscription subscription,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[1024];
        using var command = new MemoryStream(MaximumCommandBytes);
        while (!cancellationToken.IsCancellationRequested
            && socket.State == WebSocketState.Open)
        {
            command.SetLength(0);
            ValueWebSocketReceiveResult receive;
            do
            {
                receive = await socket.ReceiveAsync(
                    buffer.AsMemory(),
                    cancellationToken).ConfigureAwait(false);
                if (receive.MessageType == WebSocketMessageType.Close)
                {
                    return;
                }
                if (receive.MessageType != WebSocketMessageType.Text)
                {
                    subscription.EnqueueControl(Error("text_command_required"));
                    return;
                }
                if (command.Length + receive.Count > MaximumCommandBytes)
                {
                    subscription.EnqueueControl(Error("command_too_large"));
                    return;
                }
                command.Write(buffer, 0, receive.Count);
            }
            while (!receive.EndOfMessage);

            var action = ParseAction(command.GetBuffer().AsMemory(0, (int)command.Length));
            switch (action)
            {
                case "pause":
                    subscription.Pause();
                    subscription.EnqueueControl(Success("pause"));
                    LogPaused(logger);
                    break;
                case "resume":
                    subscription.EnqueueControl(Success("resume"));
                    subscription.Resume();
                    LogResumed(logger);
                    break;
                case "terminate":
                    subscription.EnqueueControl(Success("terminate"));
                    return;
                case null:
                    subscription.EnqueueControl(Error("invalid_command"));
                    break;
                default:
                    subscription.EnqueueControl(Error("unknown_action"));
                    break;
            }
        }
    }

    private static string? ParseAction(ReadOnlyMemory<byte> payload)
    {
        try
        {
            using var document = JsonDocument.Parse(
                payload,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 4,
                });
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("action", out var action)
                || action.ValueKind != JsonValueKind.String)
            {
                return null;
            }
            return action.GetString()?.Trim().ToLowerInvariant();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Success(string action) =>
        string.Concat(
            "{\"type\":\"control\",\"action\":\"",
            action,
            "\",\"status\":\"ok\"}");

    private static string Error(string code) =>
        string.Concat(
            "{\"type\":\"control\",\"action\":\"invalid\",\"status\":\"error\",\"code\":\"",
            code,
            "\"}");

    [LoggerMessage(
        EventId = 1801,
        Level = LogLevel.Information,
        Message = "WebSocket log stream paused.")]
    private static partial void LogPaused(ILogger logger);

    [LoggerMessage(
        EventId = 1802,
        Level = LogLevel.Information,
        Message = "WebSocket log stream resumed.")]
    private static partial void LogResumed(ILogger logger);

    private static async Task IgnoreCancellationAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (WebSocketException)
        {
        }
    }

    private static async Task CloseAsync(WebSocket socket)
    {
        if (socket.State is not WebSocketState.Open
            and not WebSocketState.CloseReceived)
        {
            return;
        }

        using var timeout = new CancellationTokenSource(CloseTimeout);
        try
        {
            await socket.CloseOutputAsync(
                WebSocketCloseStatus.NormalClosure,
                "log stream closed",
                timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (WebSocketException)
        {
        }
    }
}
