using System.Net;
using System.Text;
using AnimeGoNet.App.Feeds;
using AnimeGoNet.App.Torrents;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Feeds;
using AnimeGoNet.Core.Rules;
using AnimeGoNet.Core.Torrents;
using AnimeGoNet.Data.Feeds;
using AnimeGoNet.Data.Rules;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeGoNet.App.Tests.Feeds;

public sealed class MikanLegacyFilterProcessorTests
{
    [Fact]
    public async Task Filiter0And4RunWithoutFetchingEpisodePages()
    {
        await using var staging = new CountingStagingService();
        var transport = new StaticTransport(_ => throw new InvalidOperationException("Page fetch was not expected."));
        await using var app = await RunningApp.StartAsync(
            stagingService: staging,
            rssDnsResolver: new PublicDnsResolver(),
            rssHttpTransport: transport);
        await SaveAsync(app, new LegacyMikanFilterConfig(
            [Pair("global", Rule(blacklist: ["720p"], blacklistEnabled: true))],
            EmptyTier(), EmptyTier(), EmptyTier(),
            Tier(("Group", Rule(whitelist: ["1080p"], whitelistEnabled: true)))));

        var result = await Processor(app).ProcessAsync(Feed(
            Item("[Other] Show [03] [720p]", "a", "a"),
            Item("[Group] Show [04] [1080p]", "b", "b")));

        Assert.Empty(transport.EpisodeRequests);
        Assert.Equal(1, staging.StageCount);
        Assert.Equal(MikanRssDecisionKind.RejectedByLegacyFilter, result.Items[0].DecisionKind);
        Assert.Equal("blocked", result.Items[0].Status);
        Assert.Equal("staged", result.Items[1].Status);
        var stored = Assert.IsType<MikanRssBatchRecord>(await BatchStore(app).GetAsync(result.BatchId));
        Assert.True(stored.LegacyFilterEnabled);
        Assert.Equal(2, stored.LegacyFilterRevision);
        Assert.Equal("Filiter0", stored.Entries[0].LegacyFilterAudit.MatchedScope);
        Assert.Equal("Filiter4", stored.Entries[1].LegacyFilterAudit.MatchedScope);
    }

    [Fact]
    public async Task IdentityTiersFetchEachUniqueEpisodeUrlOncePerBatch()
    {
        await using var staging = new CountingStagingService();
        var transport = new StaticTransport(_ => Html("""
            <a href="/RSS/Bangumi?bangumiId=3951&amp;subgroupid=370" class="mikan-rss">RSS</a>
            """));
        await using var app = await RunningApp.StartAsync(
            stagingService: staging,
            rssDnsResolver: new PublicDnsResolver(),
            rssHttpTransport: transport);
        await SaveAsync(app, new LegacyMikanFilterConfig(
            [],
            Tier(("key_3951_370", Rule(blacklist: ["bad"], blacklistEnabled: true))),
            EmptyTier(), EmptyTier(), EmptyTier()));

        var result = await Processor(app).ProcessAsync(Feed(
            Item("[Group] Show [03] bad", "shared", "a"),
            Item("[Group] Show [04] good", "shared", "b")));

        Assert.Single(transport.EpisodeRequests);
        Assert.Equal(1, staging.StageCount);
        Assert.Equal(2, result.LegacyFilterRevision);
        Assert.True(result.LegacyFilterEnabled);
        Assert.Equal(MikanRssDecisionKind.RejectedByLegacyFilter, result.Items[0].DecisionKind);
        Assert.Equal(MikanLegacyFilterState.Rejected, result.Items[0].LegacyFilterState);
        Assert.Equal("key_3951_370", result.Items[0].LegacyFilterKey);
        Assert.Equal(3951, result.Items[0].IdentityMikanId);
        Assert.Equal(370, result.Items[0].IdentityGroupId);
        Assert.Equal("staged", result.Items[1].Status);
        var stored = Assert.IsType<MikanRssBatchRecord>(await BatchStore(app).GetAsync(result.BatchId));
        Assert.All(stored.Entries, entry =>
        {
            Assert.Equal(3951, entry.LegacyFilterAudit.IdentityMikanId);
            Assert.Equal(370, entry.LegacyFilterAudit.IdentityGroupId);
            Assert.Equal("Filiter1", entry.LegacyFilterAudit.MatchedScope);
            Assert.Equal("key_3951_370", entry.LegacyFilterAudit.MatchedKey);
        });
    }

    [Fact]
    public async Task SuccessfulIdentityIsReusedAcrossLaterRssBatches()
    {
        await using var staging = new CountingStagingService();
        var transport = new StaticTransport(_ => Html("""
            <a href="/RSS/Bangumi?bangumiId=3951&amp;subgroupid=370" class="mikan-rss">RSS</a>
            """));
        await using var app = await RunningApp.StartAsync(
            stagingService: staging,
            rssDnsResolver: new PublicDnsResolver(),
            rssHttpTransport: transport);
        await SaveAsync(app, new LegacyMikanFilterConfig(
            [],
            Tier(("key_3951_370", Rule())),
            EmptyTier(), EmptyTier(), EmptyTier()));

        var first = await Processor(app).ProcessAsync(Feed(
            Item("[Group] Show [03]", "shared", "first")));
        var second = await Processor(app).ProcessAsync(Feed(
            Item("[Group] Show [04]", "shared", "second")));

        Assert.Equal("staged", Assert.Single(first.Items).Status);
        Assert.Equal("already_ingested", Assert.Single(second.Items).Status);
        Assert.Single(transport.EpisodeRequests);
        Assert.Equal(1, staging.StageCount);
        var cached = await app.App.Services
            .GetRequiredService<MikanEpisodeIdentityCache>()
            .GetAsync(new Uri("https://mikanani.me/Home/Episode/shared"));
        Assert.Equal((3951, 370), (cached?.MikanId, cached?.SubGroupId));
    }

    [Fact]
    public async Task PageParseFailureIsAuditedPerCandidateWithoutBlockingOthers()
    {
        await using var staging = new CountingStagingService();
        var transport = new StaticTransport(uri => uri.AbsolutePath.EndsWith("/a", StringComparison.Ordinal)
            ? Html("<html><body>missing identity</body></html>")
            : Html("<a href='/RSS/Bangumi?bangumiId=3951&amp;subgroupid=370' class='mikan-rss'>RSS</a>"));
        await using var app = await RunningApp.StartAsync(
            stagingService: staging,
            rssDnsResolver: new PublicDnsResolver(),
            rssHttpTransport: transport);
        await SaveAsync(app, new LegacyMikanFilterConfig(
            [], EmptyTier(), Tier(("3951", Rule())), EmptyTier(), EmptyTier()));

        var result = await Processor(app).ProcessAsync(Feed(
            Item("[Group] Show [03]", "a", "a"),
            Item("[Group] Show [04]", "b", "b")));

        var item = result.Items[0];
        Assert.Equal(MikanRssDecisionKind.FilterEvaluationFailed, item.DecisionKind);
        Assert.Equal("mikan_identity_link_missing", item.DecisionReason);
        Assert.Equal("blocked", item.Status);
        Assert.Equal("staged", result.Items[1].Status);
        Assert.Equal(1, staging.StageCount);
        Assert.Equal(2, transport.EpisodeRequests.Length);
        var stored = Assert.IsType<MikanRssBatchRecord>(await BatchStore(app).GetAsync(result.BatchId));
        Assert.Equal(
            MikanLegacyFilterState.FilterEvaluationFailed,
            stored.Entries[0].LegacyFilterAudit.State);
    }

    [Fact]
    public async Task UnsafeIdentityRedirectIsRejectedWithoutFollowingSecondRequest()
    {
        await using var staging = new CountingStagingService();
        var transport = new StaticTransport(_ => new TorrentHttpResponse(
            HttpStatusCode.Redirect,
            new Uri("http://127.0.0.1/private"),
            0,
            Stream.Null));
        await using var app = await RunningApp.StartAsync(
            stagingService: staging,
            rssDnsResolver: new PublicDnsResolver(),
            rssHttpTransport: transport);
        await SaveAsync(app, new LegacyMikanFilterConfig(
            [], EmptyTier(), Tier(("3951", Rule())), EmptyTier(), EmptyTier()));

        var result = await Processor(app).ProcessAsync(Feed(Item("[Group] Show [03]", "a", "a")));

        Assert.Single(transport.Requests);
        Assert.Equal("rss_redirect_rejected", result.Items[0].DecisionReason);
        Assert.Equal(MikanRssDecisionKind.FilterEvaluationFailed, result.Items[0].DecisionKind);
        Assert.Equal(0, staging.StageCount);
    }

    [Fact]
    public async Task DisabledProfileSkipsFilterWithoutNetworkAndPreservesRules()
    {
        await using var staging = new CountingStagingService();
        var transport = new StaticTransport(_ => throw new InvalidOperationException("Page fetch was not expected."));
        await using var app = await RunningApp.StartAsync(
            configure: options => options with
            {
                InitialSourceProfiles = options.InitialSourceProfiles.Select(seed =>
                    seed.Id == "mikan" ? seed with { RssFilterEnabled = false } : seed).ToArray(),
            },
            stagingService: staging,
            rssDnsResolver: new PublicDnsResolver(),
            rssHttpTransport: transport);
        await SaveAsync(app, new LegacyMikanFilterConfig(
            [], Tier(("key_3951_370", Rule(blacklist: ["Show"], blacklistEnabled: true))),
            EmptyTier(), EmptyTier(), EmptyTier()));

        var result = await Processor(app).ProcessAsync(Feed(Item("[Group] Show [03]", "a", "a")));

        Assert.Empty(transport.EpisodeRequests);
        Assert.Equal(1, staging.StageCount);
        Assert.Equal("staged", result.Items[0].Status);
        var stored = Assert.IsType<MikanRssBatchRecord>(await BatchStore(app).GetAsync(result.BatchId));
        Assert.False(stored.LegacyFilterEnabled);
        Assert.Equal(MikanLegacyFilterState.SkippedByConfiguration, stored.Entries[0].LegacyFilterAudit.State);
        var snapshot = await app.App.Services.GetRequiredService<LegacyMikanFilterStore>().GetAsync("mikan");
        Assert.Single(snapshot!.Config.Filiter1);
    }

    private static MikanRssIngestProcessor Processor(RunningApp app) =>
        app.App.Services.GetRequiredService<MikanRssIngestProcessor>();

    private static MikanRssBatchStore BatchStore(RunningApp app) =>
        app.App.Services.GetRequiredService<MikanRssBatchStore>();

    private static async Task SaveAsync(RunningApp app, LegacyMikanFilterConfig config) =>
        _ = await app.App.Services.GetRequiredService<LegacyMikanFilterStore>()
            .SaveLegacyAsync("mikan", config, DateTimeOffset.UtcNow);

    private static RssFeedDocument Feed(params RssFeedItem[] items) => new(items, 3951);

    private static RssFeedItem Item(string title, string pageId, string torrentId) => new(
        title,
        $"https://mikanani.me/Home/Episode/{pageId}",
        $"https://mikanani.me/Download/{torrentId}.torrent",
        "application/x-bittorrent",
        42,
        "2026-07-22");

    private static KeyValuePair<string, LegacyMikanFilterRule> Pair(
        string key, LegacyMikanFilterRule rule) => new(key, rule);

    private static Dictionary<string, LegacyMikanFilterRule> Tier(
        params (string Key, LegacyMikanFilterRule Rule)[] values) =>
        values.ToDictionary(value => value.Key, value => value.Rule, StringComparer.Ordinal);

    private static Dictionary<string, LegacyMikanFilterRule> EmptyTier() =>
        new(StringComparer.Ordinal);

    private static LegacyMikanFilterRule Rule(
        IReadOnlyList<string>? whitelist = null,
        bool whitelistEnabled = false,
        IReadOnlyList<string>? blacklist = null,
        bool blacklistEnabled = false) =>
        new(whitelistEnabled, blacklistEnabled, whitelist ?? [], blacklist ?? []);

    private static TorrentHttpResponse Html(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return new TorrentHttpResponse(
            HttpStatusCode.OK, null, bytes.Length, new MemoryStream(bytes, writable: false));
    }

    private sealed class PublicDnsResolver : ITorrentDnsResolver
    {
        public ValueTask<IReadOnlyList<IPAddress>> ResolveAsync(
            string host,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<IPAddress>>([IPAddress.Parse("1.1.1.1")]);
    }

    private sealed class StaticTransport(Func<Uri, TorrentHttpResponse> responseFactory) : ITorrentHttpTransport
    {
        public List<Uri> Requests { get; } = [];
        public Uri[] EpisodeRequests =>
            Requests.Where(uri => uri.AbsolutePath.StartsWith(
                "/Home/Episode/", StringComparison.OrdinalIgnoreCase)).ToArray();

        public ValueTask<TorrentHttpResponse> SendAsync(
            Uri uri,
            IReadOnlyList<IPAddress> validatedAddresses,
            CancellationToken cancellationToken)
        {
            Requests.Add(uri);
            if (uri.AbsolutePath.StartsWith("/Home/Bangumi/", StringComparison.OrdinalIgnoreCase))
            {
                return ValueTask.FromResult(Html("""
                    <p class="bangumi-info">
                      <a href="https://bgm.tv/subject/547888">Bangumi</a>
                    </p>
                    """));
            }
            return ValueTask.FromResult(responseFactory(uri));
        }
    }

    private sealed class CountingStagingService : ITorrentStagingService, IAsyncDisposable
    {
        private static readonly byte[] TorrentBytes = Encoding.UTF8.GetBytes(
            "d8:announce20:https://secret/token4:infod6:lengthi5e4:name11:episode.mkv12:piece lengthi16384e6:pieces20:aaaaaaaaaaaaaaaaaaaaee");
        private readonly string _root = Path.Combine(
            Path.GetTempPath(), "animegonet-legacy-filter-tests", Guid.NewGuid().ToString("N"));

        public int StageCount { get; private set; }

        public async Task<StagedTorrent> StageAsync(
            Uri secretUrl,
            TorrentSourcePolicy sourcePolicy,
            CancellationToken cancellationToken = default)
        {
            _ = secretUrl;
            _ = sourcePolicy;
            StageCount++;
            Directory.CreateDirectory(_root);
            var path = Path.Combine(_root, $"filter-{Guid.NewGuid():N}.torrent");
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
}
