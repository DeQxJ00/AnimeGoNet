using AnimeGoNet.App.Ingest;
using Microsoft.Extensions.Logging;

namespace AnimeGoNet.App.Tests.Ingest;

public sealed class DuplicateHitNotifierTests
{
    [Fact]
    public void EnabledNotificationWritesStableRedactedDuplicateEvent()
    {
        var logger = new CollectingLogger();
        var notifier = new DuplicateHitNotifier(logger);

        notifier.Notify(
            enabled: true,
            sourceProfileId: "mikan-main",
            sourceId: "mikan",
            scope: "tmdb:72517:s2:e4",
            reason: "episode_already_completed");

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal(4301, entry.EventId.Id);
        Assert.Equal("LogDuplicateHit", entry.EventId.Name);
        Assert.Equal(
            "Duplicate media hit for source profile mikan-main (mikan), scope "
            + "tmdb:72517:s2:e4, reason episode_already_completed; download was skipped.",
            entry.Message);
    }

    [Fact]
    public void DisabledNotificationWritesNothing()
    {
        var logger = new CollectingLogger();
        var notifier = new DuplicateHitNotifier(logger);

        notifier.Notify(
            enabled: false,
            sourceProfileId: "mikan-main",
            sourceId: "mikan",
            scope: "source-work:3951:ep:3:batch:safe-id",
            reason: "rss_completion_alias");

        Assert.Empty(logger.Entries);
    }

    private sealed record LogEntry(LogLevel Level, EventId EventId, string Message);

    private sealed class CollectingLogger : ILogger<DuplicateHitNotifier>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add(new LogEntry(logLevel, eventId, formatter(state, exception)));
    }
}
