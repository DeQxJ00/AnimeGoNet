using System.Text;
using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using AnimeGoNet.App.Feeds;
using AnimeGoNet.App.Serialization;
using AnimeGoNet.App.Torrents;
using AnimeGoNet.Core.Feeds;
using AnimeGoNet.Core.Library;
using AnimeGoNet.Core.Rules;
using AnimeGoNet.Core.Torrents;
using AnimeGoNet.Data.Feeds;
using AnimeGoNet.Data.Library;
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
    public async Task CompletedSourceEpisodeStopsBeforeTorrentStagingAndDeletionAllowsReentry()
    {
        await using var staging = new CountingStagingService();
        var transport = new WorkPageTransport();
        await using var app = await StartAsync(staging, transport);
        var completions = app.App.Services.GetRequiredService<CompletionRecordStore>();
        Assert.True(await completions.TryAddAsync(new CompletionRecord
        {
            Id = "completed-episode-3",
            Episode = new TmdbEpisodeIdentity(72517, 4, 3),
            SourceId = "mikan",
            SourceItemId = "previous",
            CompletedAtUtc = new DateTimeOffset(2026, 7, 20, 1, 2, 3, TimeSpan.Zero),
        }));
        Assert.True(await completions.TryAddAliasAsync(new CompletionAlias
        {
            Id = "completed-episode-3-alias",
            CompletionId = "completed-episode-3",
            SourceId = "mikan",
            SourceWorkId = "3951",
            SourceEpisode = "3",
            InfoHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            CreatedAtUtc = new DateTimeOffset(2026, 7, 20, 1, 2, 4, TimeSpan.Zero),
        }));

        var processor = app.App.Services.GetRequiredService<MikanRssIngestProcessor>();
        var feed = Feed(Item("Show [03] [1080p]", "winner"));
        using var logSocket = new ClientWebSocket();
        await logSocket.ConnectAsync(WebSocketUri(app), CancellationToken.None);
        var skipped = await processor.ProcessAsync(feed);

        var duplicateLog = await ReceiveUntilAsync(
            logSocket,
            value => value.Contains("(4301)", StringComparison.Ordinal));
        Assert.Contains("source-work:3951:ep:3:batch:", duplicateLog, StringComparison.Ordinal);
        Assert.Contains("rss_completion_alias", duplicateLog, StringComparison.Ordinal);
        Assert.DoesNotContain("/Download/winner.torrent", duplicateLog, StringComparison.Ordinal);

        var skippedItem = Assert.Single(skipped.Items);
        Assert.Equal("already_completed", skippedItem.Status);
        Assert.Null(skippedItem.IngestTaskId);
        Assert.Empty(skippedItem.Errors);
        Assert.Equal(0, staging.StageCount);
        Assert.Empty(transport.Requests);
        Assert.Equal(MikanBangumiDiscoveryStates.NotApplicable, skipped.BangumiDiscoveryState);
        Assert.Equal("mikan_bgmid_no_pending_winner", skipped.BangumiDiscoveryFailureCode);
        var storedSkip = Assert.IsType<MikanRssBatchRecord>(
            await app.App.Services.GetRequiredService<MikanRssBatchStore>().GetAsync(skipped.BatchId));
        var storedEntry = Assert.Single(storedSkip.Entries);
        Assert.Equal("ready", storedEntry.EffectState);
        Assert.Equal("completed-episode-3", storedEntry.EarlyCompletionId);
        Assert.Equal("completed-episode-3-alias", storedEntry.EarlyCompletionAliasId);
        Assert.NotNull(storedEntry.EarlyCompletionCheckedAtUtc);

        var database = app.App.Services.GetRequiredService<AnimeGoSqliteDatabase>();
        await using (var connection = await database.OpenConnectionAsync())
        await using (var delete = connection.CreateCommand())
        {
            delete.CommandText = "DELETE FROM completion_records WHERE id = 'completed-episode-3';";
            Assert.Equal(1, await delete.ExecuteNonQueryAsync());
        }

        var retried = await processor.ProcessAsync(feed);
        var retriedItem = Assert.Single(retried.Items);
        Assert.Equal("staged", retriedItem.Status);
        Assert.NotNull(retriedItem.IngestTaskId);
        Assert.Equal(1, staging.StageCount);
        Assert.Single(transport.Requests);
        Assert.Equal(MikanBangumiDiscoveryStates.Resolved, retried.BangumiDiscoveryState);
        Assert.Equal(skipped.BatchId, retried.BatchId);
        var storedRetry = Assert.IsType<MikanRssBatchRecord>(
            await app.App.Services.GetRequiredService<MikanRssBatchStore>().GetAsync(retried.BatchId));
        var retriedEntry = Assert.Single(storedRetry.Entries);
        Assert.Equal("ingested", retriedEntry.EffectState);
        Assert.Null(retriedEntry.EarlyCompletionId);
        Assert.Null(retriedEntry.EarlyCompletionAliasId);
        Assert.Null(retriedEntry.EarlyCompletionCheckedAtUtc);
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
    public async Task ConcurrentProfileUpdateOnlyAffectsTheNextRssRequest()
    {
        await using var staging = new CountingStagingService();
        var transport = new BlockingWorkPageTransport();
        await using var app = await StartAsync(staging, transport);
        var processor = app.App.Services.GetRequiredService<MikanRssIngestProcessor>();

        var processing = processor.ProcessAsync(Feed(Item("Show [03] [1080p]", "winner")));
        await transport.WaitUntilRequestedAsync();
        var database = app.App.Services.GetRequiredService<AnimeGoSqliteDatabase>();
        try
        {
            await using var connection = await database.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                    UPDATE source_profiles
                    SET downloader_id = 'changed',
                        category = 'changed',
                        rss_filter_enabled = 0,
                        rss_priority_enabled = 0,
                        revision = revision + 1
                    WHERE id = 'mikan';
                    """;
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }
        finally
        {
            transport.Release();
        }

        var result = await processing;
        var item = Assert.Single(result.Items);
        Assert.Equal("staged", item.Status);
        Assert.Equal(1, staging.StageCount);
        var taskId = Assert.IsType<string>(item.IngestTaskId);
        await using (var connection = await database.OpenConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT source_profile_revision, downloader_id, route_snapshot_json
                FROM ingest_tasks
                WHERE id = $id;
                """;
            command.Parameters.AddWithValue("$id", taskId);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(1, reader.GetInt64(0));
            Assert.Equal("bt", reader.GetString(1));
            using var route = JsonDocument.Parse(reader.GetString(2));
            Assert.Equal(1, route.RootElement.GetProperty("revision").GetInt64());
            Assert.Equal("animegonet", route.RootElement.GetProperty("category").GetString());
            Assert.True(route.RootElement.GetProperty("rss_filter_enabled").GetBoolean());
            Assert.True(route.RootElement.GetProperty("rss_priority_enabled").GetBoolean());
        }

        var stored = Assert.IsType<MikanRssBatchRecord>(
            await app.App.Services.GetRequiredService<MikanRssBatchStore>().GetAsync(result.BatchId));
        Assert.True(stored.PriorityEnabled);
        Assert.True(stored.LegacyFilterEnabled);
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

    [Fact]
    public async Task MyBangumiFeedSplitsItemsByResolvedMikanWork()
    {
        await using var staging = new CountingStagingService();
        var transport = new AggregateWorkPageTransport();
        await using var app = await StartAsync(staging, transport);
        var feed = new RssFeedDocument(
            [
                Item("First Show [03] [1080p]", "work-a"),
                Item("Second Show [03] [1080p]", "work-b"),
            ],
            null);

        var result = await app.App.Services
            .GetRequiredService<MikanRssIngestProcessor>()
            .ProcessAsync(feed);

        Assert.Equal("Multiple", result.BangumiDiscoveryState);
        Assert.Null(result.MikanId);
        Assert.Null(result.BangumiSubjectId);
        Assert.Equal(2, staging.StageCount);
        Assert.Equal(2, result.Items.Count);
        Assert.All(result.Items, item => Assert.Equal("staged", item.Status));
        var childBatches = Assert.IsAssignableFrom<IReadOnlyList<MikanRssIngestResult>>(result.Batches);
        Assert.Equal([3951, 4028], childBatches.Select(batch => batch.MikanId).ToArray());
        Assert.Equal([547888, 556677], childBatches.Select(batch => batch.BangumiSubjectId).ToArray());
        Assert.All(childBatches, batch => Assert.Single(batch.Items));
        Assert.Equal(2, transport.EpisodeRequests.Count);
        Assert.Equal(2, transport.BangumiRequests.Count);

        var json = JsonSerializer.Serialize(result, ApiJsonContext.Default.MikanRssIngestResult);
        using (var document = JsonDocument.Parse(json))
        {
            Assert.Equal(2, document.RootElement.GetProperty("batches").GetArrayLength());
            Assert.Equal("Multiple", document.RootElement.GetProperty("bgmid_discovery_state").GetString());
        }

        var database = app.App.Services.GetRequiredService<AnimeGoSqliteDatabase>();
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT source_work_id FROM ingest_tasks ORDER BY source_work_id;";
        await using var reader = await command.ExecuteReaderAsync();
        var sourceWorkIds = new List<string>();
        while (await reader.ReadAsync())
        {
            sourceWorkIds.Add(reader.GetString(0));
        }
        Assert.Equal(["3951", "4028"], sourceWorkIds);
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

    private static Uri WebSocketUri(RunningApp app) => new UriBuilder(app.Client.BaseAddress!)
    {
        Scheme = "ws",
        Path = "/websocket/log",
    }.Uri;

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
            "Expected duplicate notification WebSocket frame was not received.");
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
            result = await socket.ReceiveAsync(buffer.AsMemory(), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new Xunit.Sdk.XunitException(
                    "WebSocket closed before the duplicate notification frame.");
            }
            Assert.Equal(WebSocketMessageType.Text, result.MessageType);
            payload.Write(buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);
        return Encoding.UTF8.GetString(payload.GetBuffer(), 0, (int)payload.Length);
    }

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

    private sealed class AggregateWorkPageTransport : ITorrentHttpTransport
    {
        public List<Uri> EpisodeRequests { get; } = [];
        public List<Uri> BangumiRequests { get; } = [];

        public ValueTask<TorrentHttpResponse> SendAsync(
            Uri uri,
            IReadOnlyList<IPAddress> validatedAddresses,
            CancellationToken cancellationToken)
        {
            _ = validatedAddresses;
            _ = cancellationToken;
            string html;
            if (uri.AbsolutePath.StartsWith("/Home/Episode/", StringComparison.OrdinalIgnoreCase))
            {
                EpisodeRequests.Add(uri);
                var mikanId = uri.AbsolutePath.EndsWith("work-a", StringComparison.Ordinal) ? 3951 : 4028;
                html = $"<a class='mikan-rss' href='/RSS/Bangumi?bangumiId={mikanId}&amp;subgroupid=370'>RSS</a>";
            }
            else
            {
                BangumiRequests.Add(uri);
                var bgmId = uri.AbsolutePath.EndsWith("/3951", StringComparison.Ordinal) ? 547888 : 556677;
                html = $"<p class='bangumi-info'><a href='https://bgm.tv/subject/{bgmId}'>Bangumi</a></p>";
            }

            var bytes = Encoding.UTF8.GetBytes(html);
            return ValueTask.FromResult(new TorrentHttpResponse(
                HttpStatusCode.OK,
                null,
                bytes.Length,
                new MemoryStream(bytes, writable: false)));
        }
    }

    private sealed class BlockingWorkPageTransport : ITorrentHttpTransport
    {
        private readonly TaskCompletionSource _requested =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task WaitUntilRequestedAsync() => _requested.Task;

        public void Release() => _release.TrySetResult();

        public async ValueTask<TorrentHttpResponse> SendAsync(
            Uri uri,
            IReadOnlyList<IPAddress> validatedAddresses,
            CancellationToken cancellationToken)
        {
            _ = uri;
            _ = validatedAddresses;
            _requested.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            var bytes = Encoding.UTF8.GetBytes("""
                <p class="bangumi-info">
                  <a href="https://bgm.tv/subject/547888">Bangumi</a>
                </p>
                """);
            return new TorrentHttpResponse(
                HttpStatusCode.OK,
                null,
                bytes.Length,
                new MemoryStream(bytes, writable: false));
        }
    }
}
