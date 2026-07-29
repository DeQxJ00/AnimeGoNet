using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace AnimeGoNet.App.Logging;

public sealed class WebSocketLogHub : ILoggerProvider
{
    private readonly ConcurrentDictionary<long, WebSocketLogSubscription> _subscriptions = [];
    private long _nextSubscriptionId;
    private int _disposed;

    public ILogger CreateLogger(string categoryName)
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);
        return new FanoutLogger(this, categoryName);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        foreach (var subscription in _subscriptions.Values)
        {
            subscription.Dispose();
        }
        _subscriptions.Clear();
    }

    internal WebSocketLogSubscription Subscribe()
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);
        var id = Interlocked.Increment(ref _nextSubscriptionId);
        var subscription = new WebSocketLogSubscription(
            id,
            RemoveSubscription);
        if (!_subscriptions.TryAdd(id, subscription))
        {
            subscription.Dispose();
            throw new InvalidOperationException(
                "Could not register the WebSocket log subscription.");
        }
        return subscription;
    }

    private void RemoveSubscription(long id) =>
        _subscriptions.TryRemove(id, out _);

    private void Publish(
        string category,
        LogLevel level,
        EventId eventId,
        string message,
        Exception? exception)
    {
        if (Volatile.Read(ref _disposed) != 0 || _subscriptions.IsEmpty)
        {
            return;
        }

        var line = WebSocketLogFormatter.Format(
            category,
            level,
            eventId,
            message,
            exception);
        foreach (var subscription in _subscriptions.Values)
        {
            subscription.Publish(line);
        }
    }

    private sealed class FanoutLogger(
        WebSocketLogHub hub,
        string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull =>
            NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) =>
            logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            if (!IsEnabled(logLevel))
            {
                return;
            }

            string message;
            try
            {
                message = formatter(state, exception);
            }
            catch
            {
                message = "Log formatter failed.";
            }
            hub.Publish(category, logLevel, eventId, message, exception);
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}

internal sealed class WebSocketLogSubscription(
    long id,
    Action<long> remove) : IDisposable
{
    internal const int PauseBufferCapacity = 1000;
    private const int OutboundCapacity = 256;
    private readonly Lock _gate = new();
    private readonly Queue<string> _pausedLines = new(PauseBufferCapacity);
    private readonly Channel<string> _outbound = Channel.CreateBounded<string>(
        new BoundedChannelOptions(OutboundCapacity)
        {
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });
    private bool _paused;
    private bool _disposed;

    internal ChannelReader<string> Outbound => _outbound.Reader;

    internal void Pause()
    {
        lock (_gate)
        {
            if (!_disposed)
            {
                _paused = true;
            }
        }
    }

    internal void Resume()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _paused = false;
            if (_pausedLines.Count == 0)
            {
                return;
            }

            _outbound.Writer.TryWrite(
                WebSocketLogFormatter.Frame(_pausedLines));
            _pausedLines.Clear();
        }
    }

    internal void EnqueueControl(string payload)
    {
        lock (_gate)
        {
            if (!_disposed)
            {
                _outbound.Writer.TryWrite(payload);
            }
        }
    }

    internal void Publish(string line)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (!_paused)
            {
                _outbound.Writer.TryWrite(
                    WebSocketLogFormatter.Frame([line]));
                return;
            }

            if (_pausedLines.Count == PauseBufferCapacity)
            {
                _pausedLines.Dequeue();
            }
            _pausedLines.Enqueue(line);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _pausedLines.Clear();
            _outbound.Writer.TryComplete();
        }
        remove(id);
    }
}

internal static partial class WebSocketLogFormatter
{
    private const int MaximumLineLength = 2048;

    internal static string Frame(IEnumerable<string> lines)
    {
        var snapshot = lines.ToArray();
        var builder = new StringBuilder(
            32 + snapshot.Sum(line => line.Length + 2));
        builder.Append(
            CultureInfo.InvariantCulture,
            $"{{\"type\":\"log\",\"count\":{snapshot.Length}}}");
        foreach (var line in snapshot)
        {
            builder.Append("\n\n");
            builder.Append(line);
        }
        return builder.ToString();
    }

    internal static string Format(
        string category,
        LogLevel level,
        EventId eventId,
        string message,
        Exception? exception)
    {
        var levelName = level switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "NON",
        };
        var safeCategory = Redact(category);
        var safeMessage = Redact(message);
        var builder = new StringBuilder(256);
        builder.Append(DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        builder.Append(" [");
        builder.Append(levelName);
        builder.Append("] ");
        builder.Append(safeCategory);
        if (eventId.Id != 0)
        {
            builder.Append(
                CultureInfo.InvariantCulture,
                $" ({eventId.Id})");
        }
        builder.Append(": ");
        builder.Append(safeMessage);
        if (exception is not null)
        {
            builder.Append(" | ");
            builder.Append(exception.GetType().Name);
            builder.Append(": ");
            builder.Append(Redact(exception.Message));
        }

        if (builder.Length <= MaximumLineLength)
        {
            return builder.ToString();
        }
        return string.Concat(
            builder.ToString(0, MaximumLineLength - 14),
            "…[truncated]");
    }

    internal static string Redact(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var normalized = WhitespaceRegex().Replace(value, " ").Trim();
        normalized = UrlRegex().Replace(
            normalized,
            static match => RedactUrl(match.Value));
        normalized = JsonSecretRegex().Replace(
            normalized,
            "$1<redacted>$2");
        normalized = BearerRegex().Replace(
            normalized,
            "$1<redacted>");
        return SecretAssignmentRegex().Replace(
            normalized,
            "$1<redacted>");
    }

    private static string RedactUrl(string raw)
    {
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
        {
            return "<redacted-url>";
        }

        var origin = uri.GetLeftPart(UriPartial.Authority);
        return uri.AbsolutePath == "/"
            && string.IsNullOrEmpty(uri.Query)
            && string.IsNullOrEmpty(uri.Fragment)
            ? origin
            : string.Concat(origin, "/<redacted>");
    }

    [GeneratedRegex(
        @"\s+",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(
        @"https?://[^\s<>""']+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex UrlRegex();

    [GeneratedRegex(
        @"(?i)(\bBearer\s+)[A-Za-z0-9._~+/=-]+",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex BearerRegex();

    [GeneratedRegex(
        """(?i)(["'](?:access[_-]?key|passkey|api[_-]?key|token|password|cookie|authorization)["']\s*:\s*["'])[^"']*(["'])""",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex JsonSecretRegex();

    [GeneratedRegex(
        @"(?i)(\b(?:access[_-]?key|passkey|api[_-]?key|token|password|cookie|authorization)\s*[=:]\s*)[^\s,;]+",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex SecretAssignmentRegex();
}
