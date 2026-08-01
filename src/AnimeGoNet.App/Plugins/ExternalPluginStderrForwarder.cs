using System.Buffers;
using System.Text;
using System.Text.RegularExpressions;
using AnimeGoNet.App.Logging;

namespace AnimeGoNet.App.Plugins;

internal sealed partial class ExternalPluginStderrForwarder(
    ILogger logger,
    TimeProvider timeProvider)
{
    private static readonly Encoding Utf8 = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: false);

    public async Task DrainAsync(
        string pluginId,
        Stream stderr,
        ExternalPluginSessionOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentNullException.ThrowIfNull(stderr);
        ArgumentNullException.ThrowIfNull(options);

        var maximumLineBytes = options.StderrBufferBytes;
        var readBuffer = ArrayPool<byte>.Shared.Rent(Math.Min(4096, maximumLineBytes));
        var lineBuffer = ArrayPool<byte>.Shared.Rent(maximumLineBytes);
        var lineLength = 0;
        var truncated = false;
        var window = new RateWindow(timeProvider.GetUtcNow());

        try
        {
            while (true)
            {
                var read = await stderr.ReadAsync(
                    readBuffer.AsMemory(0, Math.Min(readBuffer.Length, maximumLineBytes)),
                    cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                for (var index = 0; index < read; index++)
                {
                    var value = readBuffer[index];
                    if (value == (byte)'\n')
                    {
                        ForwardLine(
                            pluginId,
                            lineBuffer.AsSpan(0, lineLength),
                            truncated,
                            options,
                            window);
                        lineLength = 0;
                        truncated = false;
                        continue;
                    }

                    if (lineLength < maximumLineBytes)
                    {
                        lineBuffer[lineLength++] = value;
                    }
                    else
                    {
                        truncated = true;
                    }
                }
            }
        }
        finally
        {
            if (lineLength > 0 || truncated)
            {
                ForwardLine(
                    pluginId,
                    lineBuffer.AsSpan(0, lineLength),
                    truncated,
                    options,
                    window);
            }
            FlushSuppressed(pluginId, window);
            ArrayPool<byte>.Shared.Return(lineBuffer, clearArray: true);
            ArrayPool<byte>.Shared.Return(readBuffer, clearArray: true);
        }
    }

    private void ForwardLine(
        string pluginId,
        ReadOnlySpan<byte> bytes,
        bool truncated,
        ExternalPluginSessionOptions options,
        RateWindow window)
    {
        if (!bytes.IsEmpty && bytes[^1] == (byte)'\r')
        {
            bytes = bytes[..^1];
        }
        if (bytes.IsEmpty && !truncated)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        var elapsed = now - window.StartedAtUtc;
        if (elapsed < TimeSpan.Zero || elapsed >= options.StderrLogWindow)
        {
            FlushSuppressed(pluginId, window);
            window.Reset(now);
        }

        if (window.EmittedLines >= options.StderrLogLinesPerWindow)
        {
            window.Suppress();
            return;
        }

        var decoded = NormalizeControls(Utf8.GetString(bytes));
        string safe;
        try
        {
            safe = WebSocketLogFormatter.Redact(decoded);
        }
        catch (RegexMatchTimeoutException)
        {
            safe = "[stderr redaction timed out]";
        }
        if (truncated)
        {
            safe = string.Concat(safe, "…[truncated]");
        }
        if (safe.Length == 0)
        {
            return;
        }

        window.EmittedLines++;
        TryLogLine(logger, pluginId, safe);
    }

    private void FlushSuppressed(string pluginId, RateWindow window)
    {
        if (window.SuppressedLines == 0)
        {
            return;
        }
        TryLogSuppressed(logger, pluginId, window.SuppressedLines);
        window.SuppressedLines = 0;
    }

    private static string NormalizeControls(string value)
    {
        StringBuilder? builder = null;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (!char.IsControl(character) || character == '\t')
            {
                builder?.Append(character);
                continue;
            }

            builder ??= new StringBuilder(value.Length).Append(value, 0, index);
            builder.Append(' ');
        }
        return builder?.ToString() ?? value;
    }

    private static void TryLogLine(ILogger logger, string pluginId, string message)
    {
        try
        {
            LogStderrLine(logger, pluginId, message);
        }
        catch (Exception)
        {
            // A logging provider must never break or back-pressure the plugin protocol.
        }
    }

    private static void TryLogSuppressed(ILogger logger, string pluginId, long count)
    {
        try
        {
            LogStderrSuppressed(logger, pluginId, count);
        }
        catch (Exception)
        {
            // A logging provider must never break or back-pressure the plugin protocol.
        }
    }

    [LoggerMessage(
        EventId = 1901,
        Level = LogLevel.Warning,
        Message = "External plugin {PluginId} stderr: {PluginMessage}")]
    private static partial void LogStderrLine(
        ILogger logger,
        string pluginId,
        string pluginMessage);

    [LoggerMessage(
        EventId = 1902,
        Level = LogLevel.Warning,
        Message = "External plugin {PluginId} stderr rate limit suppressed {SuppressedLineCount} lines.")]
    private static partial void LogStderrSuppressed(
        ILogger logger,
        string pluginId,
        long suppressedLineCount);

    private sealed class RateWindow(DateTimeOffset startedAtUtc)
    {
        public DateTimeOffset StartedAtUtc { get; private set; } = startedAtUtc;

        public int EmittedLines { get; set; }

        public long SuppressedLines { get; set; }

        public void Reset(DateTimeOffset now)
        {
            StartedAtUtc = now;
            EmittedLines = 0;
            SuppressedLines = 0;
        }

        public void Suppress()
        {
            if (SuppressedLines < long.MaxValue)
            {
                SuppressedLines++;
            }
        }
    }
}
