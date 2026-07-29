using AnimeGoNet.App.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AnimeGoNet.App.Tests.Logging;

public sealed class RollingFileLoggerProviderTests
{
    [Fact]
    public void WritesInformationAndAboveWithSharedRedaction()
    {
        using var temporary = new TemporaryLogDirectory();
        using (var provider = CreateProvider(temporary.LogFile))
        {
            var logger = provider.CreateLogger("AnimeGoNet.Tests.File");
            WriteLog(
                logger,
                LogLevel.Debug,
                default,
                "debug-marker");
            WriteLog(
                logger,
                LogLevel.Information,
                new EventId(91),
                "safe-marker password=plain-secret "
                + "https://tracker.invalid/private/file.torrent?token=query-secret "
                + """{"api_key":"json-secret"}""");
        }

        var text = File.ReadAllText(temporary.LogFile);
        Assert.DoesNotContain("debug-marker", text, StringComparison.Ordinal);
        Assert.Contains("safe-marker", text, StringComparison.Ordinal);
        Assert.Contains("[INF]", text, StringComparison.Ordinal);
        Assert.Contains("(91)", text, StringComparison.Ordinal);
        Assert.Contains(
            "https://tracker.invalid/<redacted>",
            text,
            StringComparison.Ordinal);
        Assert.DoesNotContain("plain-secret", text, StringComparison.Ordinal);
        Assert.DoesNotContain("query-secret", text, StringComparison.Ordinal);
        Assert.DoesNotContain("json-secret", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RotatesAtSizeBoundaryAndRetainsConfiguredBackupCount()
    {
        using var temporary = new TemporaryLogDirectory();
        using (var provider = CreateProvider(
            temporary.LogFile,
            maximumFileBytes: 256,
            maximumBackups: 2))
        {
            var logger = provider.CreateLogger("AnimeGoNet.Tests.Rotate");
            for (var index = 0; index < 40; index++)
            {
                WriteLog(
                    logger,
                    LogLevel.Information,
                    default,
                    $"rotation-marker-{index:D2}-"
                    + new string((char)('a' + (index % 26)), 48));
            }
        }

        Assert.True(File.Exists(temporary.LogFile));
        Assert.True(File.Exists(string.Concat(temporary.LogFile, ".1")));
        Assert.True(File.Exists(string.Concat(temporary.LogFile, ".2")));
        Assert.False(File.Exists(string.Concat(temporary.LogFile, ".3")));
        Assert.Contains(
            "rotation-marker-39",
            File.ReadAllText(temporary.LogFile),
            StringComparison.Ordinal);
    }

    [Fact]
    public void DeletesExpiredAndOutOfRangeManagedBackupsOnly()
    {
        using var temporary = new TemporaryLogDirectory();
        var expired = string.Concat(temporary.LogFile, ".1");
        var outOfRange = string.Concat(temporary.LogFile, ".3");
        var unrelated = string.Concat(temporary.LogFile, ".notes");
        File.WriteAllText(expired, "expired");
        File.WriteAllText(outOfRange, "out-of-range");
        File.WriteAllText(unrelated, "keep");
        File.SetLastWriteTimeUtc(expired, DateTime.UtcNow.AddDays(-3));

        using var provider = CreateProvider(
            temporary.LogFile,
            maximumBackups: 2,
            maximumAge: TimeSpan.FromDays(1));

        Assert.False(File.Exists(expired));
        Assert.False(File.Exists(outOfRange));
        Assert.True(File.Exists(unrelated));
    }

    [Fact]
    public void SerializesConcurrentCompleteLines()
    {
        using var temporary = new TemporaryLogDirectory();
        using (var provider = CreateProvider(
            temporary.LogFile,
            maximumFileBytes: 1024 * 1024))
        {
            var logger = provider.CreateLogger("AnimeGoNet.Tests.Concurrent");
            Parallel.For(
                0,
                100,
                index => WriteLog(
                    logger,
                    LogLevel.Information,
                    default,
                    $"concurrent-marker-{index:D3}"));
        }

        var lines = File.ReadAllLines(temporary.LogFile);
        Assert.Equal(100, lines.Length);
        for (var index = 0; index < 100; index++)
        {
            Assert.Single(
                lines,
                line => line.Contains(
                    $"concurrent-marker-{index:D3}",
                    StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task RunningApplicationWritesToDataPathLogs()
    {
        string rootPath;
        await using (var app = await RunningApp.StartAsync())
        {
            rootPath = app.RootPath;
            var logger = app.App.Services
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("AnimeGoNet.Tests.Integration");
            WriteLog(
                logger,
                LogLevel.Warning,
                default,
                "application-file-log-marker");

            var logFile = Path.Combine(
                rootPath,
                "data",
                "logs",
                "animego.log");
            Assert.True(File.Exists(logFile));
            Assert.Contains(
                "application-file-log-marker",
                ReadLiveLog(logFile),
                StringComparison.Ordinal);
        }
        Assert.False(Directory.Exists(rootPath));
    }

    [Theory]
    [InlineData(127, 1, 1)]
    [InlineData(128, 0, 1)]
    [InlineData(128, 101, 1)]
    [InlineData(128, 1, 0)]
    public void RejectsInvalidRetentionBoundaries(
        long maximumFileBytes,
        int maximumBackups,
        long maximumAgeTicks)
    {
        using var temporary = new TemporaryLogDirectory();
        var options = new RollingFileLogOptions
        {
            FilePath = temporary.LogFile,
            MaximumFileBytes = maximumFileBytes,
            MaximumBackups = maximumBackups,
            MaximumAge = TimeSpan.FromTicks(maximumAgeTicks),
        };

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RollingFileLoggerProvider(options));
    }

    private static RollingFileLoggerProvider CreateProvider(
        string filePath,
        long maximumFileBytes = RollingFileLogOptions.DefaultMaximumFileBytes,
        int maximumBackups = RollingFileLogOptions.DefaultMaximumBackups,
        TimeSpan? maximumAge = null) =>
        new(
            new RollingFileLogOptions
            {
                FilePath = filePath,
                MaximumFileBytes = maximumFileBytes,
                MaximumBackups = maximumBackups,
                MaximumAge = maximumAge
                    ?? RollingFileLogOptions.DefaultMaximumAge,
            });

    private static void WriteLog(
        ILogger logger,
        LogLevel level,
        EventId eventId,
        string message) =>
        logger.Log(
            level,
            eventId,
            message,
            exception: null,
            static (state, _) => state);

    private static string ReadLiveLog(string filePath)
    {
        using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private sealed class TemporaryLogDirectory : IDisposable
    {
        public TemporaryLogDirectory()
        {
            RootPath = Path.Combine(
                Path.GetTempPath(),
                "animegonet-file-log-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootPath);
            LogFile = Path.Combine(RootPath, "animego.log");
        }

        public string RootPath { get; }

        public string LogFile { get; }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
