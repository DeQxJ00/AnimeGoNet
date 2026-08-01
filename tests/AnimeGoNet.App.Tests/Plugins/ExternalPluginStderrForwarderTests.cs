using System.Text;
using AnimeGoNet.App.Plugins;
using Microsoft.Extensions.Logging;

namespace AnimeGoNet.App.Tests.Plugins;

public sealed class ExternalPluginStderrForwarderTests
{
    [Fact]
    public async Task ForwardsBoundedStructuredLinesAfterRedactionAndControlNormalization()
    {
        var logger = new CollectingLogger();
        var forwarder = new ExternalPluginStderrForwarder(logger, TimeProvider.System);
        var bytes = Encoding.UTF8.GetBytes(
            "ordinary diagnostic\r\npassword=local-secret "
            + "https://example.test/private?token=value\0\n");

        await forwarder.DrainAsync(
            "com.example.filter",
            new MemoryStream(bytes),
            Options(),
            CancellationToken.None);

        var lines = logger.Entries.Where(entry => entry.EventId.Id == 1901).ToArray();
        Assert.Equal(2, lines.Length);
        Assert.All(lines, line => Assert.Equal(LogLevel.Warning, line.Level));
        Assert.All(lines, line => Assert.Equal(
            "com.example.filter",
            line.Properties["PluginId"]));
        Assert.Contains("ordinary diagnostic", lines[0].Message, StringComparison.Ordinal);
        Assert.Contains("password=<redacted>", lines[1].Message, StringComparison.Ordinal);
        Assert.Contains("https://example.test/<redacted>", lines[1].Message, StringComparison.Ordinal);
        Assert.DoesNotContain("local-secret", lines[1].Message, StringComparison.Ordinal);
        Assert.DoesNotContain("/private", lines[1].Message, StringComparison.Ordinal);
        Assert.DoesNotContain('\0', lines[1].Message);
    }

    [Fact]
    public async Task OversizedLineIsTruncatedWithoutGrowingTheLineBuffer()
    {
        var logger = new CollectingLogger();
        var forwarder = new ExternalPluginStderrForwarder(logger, TimeProvider.System);

        await forwarder.DrainAsync(
            "com.example.filter",
            new MemoryStream(Encoding.UTF8.GetBytes(new string('x', 32 * 1024))),
            Options(),
            CancellationToken.None);

        var line = Assert.Single(logger.Entries);
        Assert.Equal(1901, line.EventId.Id);
        Assert.EndsWith("…[truncated]", (string)line.Properties["PluginMessage"]!,
            StringComparison.Ordinal);
        Assert.InRange(((string)line.Properties["PluginMessage"]!).Length, 1, 270);
    }

    [Fact]
    public async Task RateLimitEmitsOnlyAllowanceAndOneSuppressionSummary()
    {
        var logger = new CollectingLogger();
        var forwarder = new ExternalPluginStderrForwarder(logger, TimeProvider.System);
        var options = Options() with { StderrLogLinesPerWindow = 2 };

        await forwarder.DrainAsync(
            "com.example.filter",
            new MemoryStream(Encoding.UTF8.GetBytes("one\ntwo\nthree\nfour\nfive\n")),
            options,
            CancellationToken.None);

        Assert.Equal(2, logger.Entries.Count(entry => entry.EventId.Id == 1901));
        var summary = Assert.Single(logger.Entries, entry => entry.EventId.Id == 1902);
        Assert.Equal(3L, summary.Properties["SuppressedLineCount"]);
        Assert.Equal("com.example.filter", summary.Properties["PluginId"]);
        Assert.DoesNotContain(
            logger.Entries,
            entry => entry.Message.Contains("three", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NewWindowReportsPriorSuppressionBeforeForwardingAgain()
    {
        var logger = new CollectingLogger();
        var clock = new MutableTimeProvider(
            new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero));
        var forwarder = new ExternalPluginStderrForwarder(logger, clock);
        await using var stream = new AdvancingChunkStream(
            [Encoding.UTF8.GetBytes("one\ntwo\n"), Encoding.UTF8.GetBytes("three\n")],
            () => clock.Advance(TimeSpan.FromSeconds(2)));

        await forwarder.DrainAsync(
            "com.example.filter",
            stream,
            Options() with
            {
                StderrLogLinesPerWindow = 1,
                StderrLogWindow = TimeSpan.FromSeconds(1),
            },
            CancellationToken.None);

        Assert.Equal([1901, 1902, 1901], logger.Entries.Select(entry => entry.EventId.Id));
        Assert.Contains("one", logger.Entries[0].Message, StringComparison.Ordinal);
        Assert.Equal(1L, logger.Entries[1].Properties["SuppressedLineCount"]);
        Assert.Contains("three", logger.Entries[2].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoggingProviderFailureDoesNotBreakStderrDrain()
    {
        var forwarder = new ExternalPluginStderrForwarder(
            new ThrowingLogger(),
            TimeProvider.System);

        await forwarder.DrainAsync(
            "com.example.filter",
            new MemoryStream(Encoding.UTF8.GetBytes("diagnostic\n")),
            Options(),
            CancellationToken.None);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1001, 10)]
    [InlineData(20, 0)]
    [InlineData(20, 3601)]
    public void SessionOptionsRejectInvalidStderrRateLimits(int lines, int seconds)
    {
        var options = Options() with
        {
            StderrLogLinesPerWindow = lines,
            StderrLogWindow = TimeSpan.FromSeconds(seconds),
        };

        Assert.Throws<ArgumentOutOfRangeException>(options.Validate);
    }

    private static ExternalPluginSessionOptions Options() => new()
    {
        StderrBufferBytes = 256,
        StderrLogLinesPerWindow = 20,
        StderrLogWindow = TimeSpan.FromSeconds(10),
    };

    private sealed record LogEntry(
        LogLevel Level,
        EventId EventId,
        string Message,
        IReadOnlyDictionary<string, object?> Properties);

    private sealed class CollectingLogger : ILogger
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = state is IEnumerable<KeyValuePair<string, object?>> structured
                ? structured.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal)
                : new Dictionary<string, object?>(StringComparer.Ordinal);
            Entries.Add(new LogEntry(
                logLevel,
                eventId,
                formatter(state, exception),
                properties));
        }
    }

    private sealed class ThrowingLogger : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            throw new InvalidOperationException("fixture logging failure");
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan value) => _now += value;
    }

    private sealed class AdvancingChunkStream(
        IReadOnlyList<byte[]> chunks,
        Action beforeSecondRead) : Stream
    {
        private int _index;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_index >= chunks.Count)
            {
                return ValueTask.FromResult(0);
            }
            if (_index == 1)
            {
                beforeSecondRead();
            }
            var chunk = chunks[_index++];
            chunk.CopyTo(buffer);
            return ValueTask.FromResult(chunk.Length);
        }

        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
