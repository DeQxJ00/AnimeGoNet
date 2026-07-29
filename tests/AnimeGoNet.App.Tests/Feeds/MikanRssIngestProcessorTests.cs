using System.Text;
using System.Net;
using AnimeGoNet.App.Feeds;
using AnimeGoNet.App.Torrents;
using AnimeGoNet.Core.Feeds;
using AnimeGoNet.Core.Rules;
using AnimeGoNet.Core.Torrents;
using AnimeGoNet.Data.Feeds;
using AnimeGoNet.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeGoNet.App.Tests.Feeds;

public sealed class MikanRssIngestProcessorTests
{
    [Fact]
    public async Task OnlyWinnerStagesAndRepeatedBatchReturnsExistingTask()
    {
        await using var staging = new CountingStagingService();
        var transport = new WorkPageTransport();
        await using var app = await StartAsync(staging, transport);
        var processor = app.App.Services.GetRequiredService<MikanRssIngestProcessor>();
        var feed = Feed(
            Item("Show [03] [720p]", "loser"),
            Item("Show [03] [1080p]", "winner"));

        var first = await processor.ProcessAsync(feed);
        var second = await processor.ProcessAsync(feed);

        Assert.Equal(1, staging.StageCount);
        Assert.Equal("blocked", first.Items[0].Status);
        Assert.Equal("staged", first.Items[1].Status);
        Assert.Equal("already_ingested", second.Items[1].Status);
        Assert.Equal(first.Items[1].IngestTaskId, second.Items[1].IngestTaskId);
        Assert.Equal(547888, first.BangumiSubjectId);
        Assert.Equal(MikanBangumiDiscoveryStates.Resolved, first.BangumiDiscoveryState);
        Assert.Null(first.BangumiDiscoveryFailureCode);
        Assert.Single(transport.Requests);
        var stored = Assert.IsType<MikanRssBatchRecord>(
            await app.App.Services.GetRequiredService<MikanRssBatchStore>().GetAsync(first.BatchId));
        Assert.Equal(547888, stored.BangumiDiscovery.BangumiSubjectId);
        Assert.Equal("blocked", stored.Entries[0].EffectState);
        Assert.Equal("ingested", stored.Entries[1].EffectState);
        Assert.Equal(first.Items[1].IngestTaskId, stored.Entries[1].IngestTaskId);
        await using var connection = await app.App.Services.GetRequiredService<AnimeGoSqliteDatabase>().OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*), source_published_at_raw, source_published_at, bangumi_subject_id
            FROM ingest_tasks;
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1, reader.GetInt32(0));
        Assert.Equal("2026-07-22T12:34:56.123", reader.GetString(1));
        Assert.Equal(
            DateTimeOffset.Parse(
                "2026-07-22T12:34:56.123+08:00",
                System.Globalization.CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(
                reader.GetString(2),
                System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(547888, reader.GetInt32(3));
    }

    [Fact]
    public async Task DisabledPriorityStagesEveryLegacyEligibleCandidateAndPersistsBatchAudit()
    {
        await using var staging = new CountingStagingService();
        var transport = new WorkPageTransport();
        await using var app = await StartAsync(staging, transport);
        var database = app.App.Services.GetRequiredService<AnimeGoSqliteDatabase>();
        await using (var connection = await database.OpenConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                UPDATE source_profiles
                SET rss_priority_enabled = 0
                WHERE id = 'mikan';
                """;
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }

        var result = await app.App.Services
            .GetRequiredService<MikanRssIngestProcessor>()
            .ProcessAsync(Feed(
                Item("Show [03] [720p]", "first"),
                Item("Show [03] [1080p]", "second")));

        Assert.Equal(2, staging.StageCount);
        Assert.All(result.Items, item =>
        {
            Assert.Equal(MikanRssDecisionKind.Winner, item.DecisionKind);
            Assert.Equal("SkippedByConfiguration", item.DecisionReason);
            Assert.Equal("staged", item.Status);
            Assert.NotNull(item.IngestTaskId);
        });

        var stored = Assert.IsType<MikanRssBatchRecord>(
            await app.App.Services.GetRequiredService<MikanRssBatchStore>().GetAsync(result.BatchId));
        Assert.False(stored.PriorityEnabled);
        Assert.Equal(2, stored.Entries.Count);
        Assert.All(stored.Entries, entry =>
        {
            Assert.Equal(MikanRssDecisionKind.Winner, entry.Decision.Kind);
            Assert.Equal("SkippedByConfiguration", entry.Decision.Reason);
            Assert.Empty(entry.Decision.EvaluatedPriorityGroups);
            Assert.Equal("ingested", entry.EffectState);
            Assert.NotNull(entry.IngestTaskId);
        });
    }

    [Fact]
    public async Task StagingFailureReleasesWinnerForExplicitRetry()
    {
        await using var staging = new CountingStagingService(failuresBeforeSuccess: 1);
        await using var app = await StartAsync(staging, new WorkPageTransport());
        var processor = app.App.Services.GetRequiredService<MikanRssIngestProcessor>();
        var feed = Feed(Item("Show [03] [1080p]", "winner"));

        var failed = await processor.ProcessAsync(feed);
        var retried = await processor.ProcessAsync(feed);

        Assert.Equal("rejected", failed.Items[0].Status);
        Assert.Contains("NetworkFailure", Assert.Single(failed.Items[0].Errors), StringComparison.Ordinal);
        Assert.Equal("staged", retried.Items[0].Status);
        Assert.Equal(2, staging.StageCount);
    }

    [Fact]
    public async Task UnexpectedFailureAlsoReleasesWinnerBeforeRethrow()
    {
        await using var staging = new CountingStagingService(failuresBeforeSuccess: 1, unexpectedFailure: true);
        await using var app = await StartAsync(staging, new WorkPageTransport());
        var processor = app.App.Services.GetRequiredService<MikanRssIngestProcessor>();
        var feed = Feed(Item("Show [03] [1080p]", "winner"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => processor.ProcessAsync(feed));
        var retried = await processor.ProcessAsync(feed);

        Assert.Equal("staged", retried.Items[0].Status);
        Assert.Equal(2, staging.StageCount);
    }

    [Fact]
    public async Task DiscoveryFailureBlocksStagingAndCanRetrySameBatch()
    {
        await using var staging = new CountingStagingService();
        var transport = new WorkPageTransport(failuresBeforeSuccess: 1);
        await using var app = await StartAsync(staging, transport);
        var processor = app.App.Services.GetRequiredService<MikanRssIngestProcessor>();
        var feed = Feed(Item("Show [03] [1080p]", "winner"));

        var failed = await processor.ProcessAsync(feed);
        var retried = await processor.ProcessAsync(feed);

        Assert.Equal("bgmid_discovery_failed", failed.Items[0].Status);
        Assert.Equal("rss_request_failed", Assert.Single(failed.Items[0].Errors));
        Assert.Equal(MikanBangumiDiscoveryStates.Failed, failed.BangumiDiscoveryState);
        Assert.Equal("staged", retried.Items[0].Status);
        Assert.Equal(547888, retried.BangumiSubjectId);
        Assert.Equal(1, staging.StageCount);
        Assert.Equal(2, transport.Requests.Count);
        Assert.Equal(failed.BatchId, retried.BatchId);
    }

    private static Task<RunningApp> StartAsync(
        ITorrentStagingService staging,
        ITorrentHttpTransport transport) =>
        RunningApp.StartAsync(
            stagingService: staging,
            rssDnsResolver: new PublicDnsResolver(),
            rssHttpTransport: transport);

    private static RssFeedDocument Feed(params RssFeedItem[] items) => new(items, 3951);

    private static RssFeedItem Item(string title, string id) => new(
        title,
        $"https://mikanani.me/Home/Episode/{id}",
        $"https://mikanani.me/Download/{id}.torrent",
        "application/x-bittorrent",
        42,
        "2026-07-22T12:34:56.123");

    private sealed class CountingStagingService(
        int failuresBeforeSuccess = 0,
        bool unexpectedFailure = false) : ITorrentStagingService, IAsyncDisposable
    {
        private static readonly byte[] TorrentBytes = Encoding.UTF8.GetBytes(
            "d8:announce20:https://secret/token4:infod6:lengthi5e4:name11:episode.mkv12:piece lengthi16384e6:pieces20:aaaaaaaaaaaaaaaaaaaaee");
        private readonly string _root = Path.Combine(
            Path.GetTempPath(), "animegonet-rss-ingest-tests", Guid.NewGuid().ToString("N"));
        private int _remainingFailures = failuresBeforeSuccess;

        public int StageCount { get; private set; }

        public async Task<StagedTorrent> StageAsync(
            Uri secretUrl,
            TorrentSourcePolicy sourcePolicy,
            CancellationToken cancellationToken = default)
        {
            _ = secretUrl;
            _ = sourcePolicy;
            StageCount++;
            if (_remainingFailures-- > 0)
            {
                if (unexpectedFailure)
                {
                    throw new InvalidOperationException("Synthetic unexpected failure.");
                }

                throw new TorrentStagingException(
                    TorrentStagingFailureCode.NetworkFailure, "Synthetic network failure.");
            }

            Directory.CreateDirectory(_root);
            var path = Path.Combine(_root, $"rss-{Guid.NewGuid():N}.torrent");
            await File.WriteAllBytesAsync(path, TorrentBytes, cancellationToken);
            return new StagedTorrent(path, TorrentMetainfoParser.Parse(TorrentBytes));
        }

        public Task<bool> DeleteAsync(string stagingFileName, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public FileStream OpenRead(string stagingFileName) => throw new FileNotFoundException();

        public Task<int> CleanupExpiredAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class PublicDnsResolver : ITorrentDnsResolver
    {
        public ValueTask<IReadOnlyList<IPAddress>> ResolveAsync(
            string host,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<IPAddress>>([IPAddress.Parse("1.1.1.1")]);
    }

    private sealed class WorkPageTransport(int failuresBeforeSuccess = 0) : ITorrentHttpTransport
    {
        private int _remainingFailures = failuresBeforeSuccess;

        public List<Uri> Requests { get; } = [];

        public ValueTask<TorrentHttpResponse> SendAsync(
            Uri uri,
            IReadOnlyList<IPAddress> validatedAddresses,
            CancellationToken cancellationToken)
        {
            Requests.Add(uri);
            if (_remainingFailures-- > 0)
            {
                throw new InvalidOperationException("Synthetic transport failure.");
            }
            var bytes = Encoding.UTF8.GetBytes("""
                <p class="bangumi-info">
                  <a href="https://bgm.tv/subject/547888">Bangumi</a>
                </p>
                """);
            return ValueTask.FromResult(new TorrentHttpResponse(
                HttpStatusCode.OK,
                null,
                bytes.Length,
                new MemoryStream(bytes, writable: false)));
        }
    }
}
