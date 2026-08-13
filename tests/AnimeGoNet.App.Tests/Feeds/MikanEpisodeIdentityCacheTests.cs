using System.Text;
using AnimeGoNet.App.Feeds;
using AnimeGoNet.Core.Feeds;
using AnimeGoNet.Data.Cache;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeGoNet.App.Tests.Feeds;

public sealed class MikanEpisodeIdentityCacheTests
{
    private static readonly Uri EpisodeUri = new(
        "https://mikanime.tv/Home/Episode/63d1e1c6ff6bd66323ad2c11e9deb772875b8e61");

    [Fact]
    public async Task PermanentSettingPersistsWithoutExpiryAndSurvivesNewResolverInstances()
    {
        await using var app = await RunningApp.StartAsync();
        var store = app.App.Services.GetRequiredService<SqliteJsonCacheStore>();
        var now = new DateTimeOffset(2026, 8, 13, 0, 0, 0, TimeSpan.Zero);
        var firstHttp = new FakeHttpClient(IdentityHtml(3951, 370));
        var firstCache = new MikanEpisodeIdentityCache(
            store,
            TimeSpan.Zero,
            new FixedTimeProvider(now));
        var feed = Feed(EpisodeUri);

        var first = await new MikanFeedIdentityResolver(firstHttp, firstCache)
            .ResolveAsync(feed, "mikan");

        Assert.Equal(3951, Assert.Single(first).Identity?.MikanId);
        Assert.Single(firstHttp.Requests);
        var stored = Assert.IsType<CacheJsonValue>(await store.GetJsonAsync(
            MikanEpisodeIdentityCache.DatabaseName,
            MikanEpisodeIdentityCache.BucketName,
            EpisodeUri.AbsoluteUri,
            now));
        Assert.Null(stored.ExpiresAtUtc);
        Assert.Equal(
            "{\"schema_version\":1,\"mikanid\":3951,\"groupid\":370}",
            stored.ValueJson);

        var restartedHttp = new FakeHttpClient(_ => throw new InvalidOperationException(
            "Persistent cache should satisfy the restarted resolver."));
        var restartedCache = new MikanEpisodeIdentityCache(
            new SqliteJsonCacheStore(
                app.App.Services.GetRequiredService<AnimeGoNet.Data.Sqlite.AnimeGoSqliteDatabase>()),
            TimeSpan.Zero,
            new FixedTimeProvider(now.AddYears(20)));
        var restarted = await new MikanFeedIdentityResolver(restartedHttp, restartedCache)
            .ResolveAsync(feed, "mikan");

        var identity = Assert.Single(restarted).Identity;
        Assert.Equal((3951, 370), (identity?.MikanId, identity?.SubGroupId));
        Assert.Empty(restartedHttp.Requests);

        var bucket = Assert.Single(
            await store.ListBrowserBucketsAsync("bolt", now.AddYears(20)),
            item => item.BucketName == MikanEpisodeIdentityCache.BucketName);
        var entry = Assert.Single((await store.ListBrowserEntriesAsync(
            "bolt", bucket.BucketId, 1, 10, now.AddYears(20)))!.Items);
        var detail = Assert.IsType<CacheBrowserEntryDetail>(await store.GetBrowserEntryAsync(
            "bolt", bucket.BucketId, entry.EntryId, now.AddYears(20)));
        Assert.Equal(EpisodeUri.AbsoluteUri, detail.Key);
        Assert.Contains("\"mikanid\":3951", detail.ValueJson, StringComparison.Ordinal);
        Assert.Equal(
            CacheBrowserDeleteResult.Deleted,
            await store.DeleteBrowserEntryAsync(
                "bolt", bucket.BucketId, entry.EntryId, entry.DeleteToken));
        Assert.Null(await restartedCache.GetAsync(EpisodeUri));
    }

    [Fact]
    public async Task ConfiguredTtlExpiresAndAllowsAuthoritativeRefresh()
    {
        await using var app = await RunningApp.StartAsync();
        var store = app.App.Services.GetRequiredService<SqliteJsonCacheStore>();
        var now = new DateTimeOffset(2026, 8, 13, 0, 0, 0, TimeSpan.Zero);
        var cache = new MikanEpisodeIdentityCache(
            store,
            TimeSpan.FromHours(12),
            new FixedTimeProvider(now));

        await cache.PutAsync(EpisodeUri, new MikanEpisodeIdentity(3951, 370));

        var stored = Assert.IsType<CacheJsonValue>(await store.GetJsonAsync(
            MikanEpisodeIdentityCache.DatabaseName,
            MikanEpisodeIdentityCache.BucketName,
            EpisodeUri.AbsoluteUri,
            now));
        Assert.Equal(now.AddHours(12), stored.ExpiresAtUtc);
        Assert.Equal(3951, (await cache.GetAsync(EpisodeUri))?.MikanId);

        var expired = new MikanEpisodeIdentityCache(
            store,
            TimeSpan.FromHours(12),
            new FixedTimeProvider(now.AddHours(13)));
        Assert.Null(await expired.GetAsync(EpisodeUri));

        var refreshedHttp = new FakeHttpClient(IdentityHtml(4028, 123));
        var refreshed = await new MikanFeedIdentityResolver(refreshedHttp, expired)
            .ResolveAsync(Feed(EpisodeUri), "mikan");
        Assert.Equal((4028, 123), (
            Assert.Single(refreshed).Identity?.MikanId,
            refreshed[0].Identity?.SubGroupId));
        Assert.Single(refreshedHttp.Requests);
    }

    [Fact]
    public async Task FailedPageIsNotPersistedAndCanSucceedOnNextRefresh()
    {
        await using var app = await RunningApp.StartAsync();
        var store = app.App.Services.GetRequiredService<SqliteJsonCacheStore>();
        var cache = new MikanEpisodeIdentityCache(store);
        var failedHttp = new FakeHttpClient("<html>no identity</html>");

        var failed = await new MikanFeedIdentityResolver(failedHttp, cache)
            .ResolveAsync(Feed(EpisodeUri), "mikan");

        Assert.Null(Assert.Single(failed).Identity);
        Assert.Equal("mikan_identity_link_missing", failed[0].FailureCode);
        Assert.Null(await store.GetJsonAsync(
            MikanEpisodeIdentityCache.DatabaseName,
            MikanEpisodeIdentityCache.BucketName,
            EpisodeUri.AbsoluteUri,
            DateTimeOffset.UtcNow));

        var recoveredHttp = new FakeHttpClient(IdentityHtml(227, 370));
        var recovered = await new MikanFeedIdentityResolver(recoveredHttp, cache)
            .ResolveAsync(Feed(EpisodeUri), "mikan");

        Assert.Equal(227, Assert.Single(recovered).Identity?.MikanId);
        Assert.Single(recoveredHttp.Requests);
        Assert.NotNull(await cache.GetAsync(EpisodeUri));
    }

    [Fact]
    public async Task IdentityWithoutGroupIdIsNotLongTermCached()
    {
        await using var app = await RunningApp.StartAsync();
        var store = app.App.Services.GetRequiredService<SqliteJsonCacheStore>();
        var cache = new MikanEpisodeIdentityCache(store);
        var firstHttp = new FakeHttpClient(
            "<a class='mikan-rss' href='/RSS/Bangumi?bangumiId=3951'>RSS</a>");

        var first = await new MikanFeedIdentityResolver(firstHttp, cache)
            .ResolveAsync(Feed(EpisodeUri), "mikan");

        Assert.Equal((3951, 0), (
            Assert.Single(first).Identity?.MikanId,
            first[0].Identity?.SubGroupId));
        Assert.Null(await cache.GetAsync(EpisodeUri));

        var secondHttp = new FakeHttpClient(IdentityHtml(3951, 370));
        var second = await new MikanFeedIdentityResolver(secondHttp, cache)
            .ResolveAsync(Feed(EpisodeUri), "mikan");

        Assert.Equal(370, Assert.Single(second).Identity?.SubGroupId);
        Assert.Single(secondHttp.Requests);
        Assert.NotNull(await cache.GetAsync(EpisodeUri));
    }

    [Fact]
    public async Task UrlWithQueryBypassesPersistentCacheWithoutBlockingResolution()
    {
        await using var app = await RunningApp.StartAsync();
        var cache = new MikanEpisodeIdentityCache(
            app.App.Services.GetRequiredService<SqliteJsonCacheStore>());
        var uri = new Uri(EpisodeUri.AbsoluteUri + "?temporary=1");
        var firstHttp = new FakeHttpClient(IdentityHtml(3951, 370));

        var first = await new MikanFeedIdentityResolver(firstHttp, cache)
            .ResolveAsync(Feed(uri), "mikan");

        Assert.Equal((3951, 370), (
            Assert.Single(first).Identity?.MikanId,
            first[0].Identity?.SubGroupId));
        Assert.Single(firstHttp.Requests);

        var secondHttp = new FakeHttpClient(IdentityHtml(3951, 370));
        var second = await new MikanFeedIdentityResolver(secondHttp, cache)
            .ResolveAsync(Feed(uri), "mikan");

        Assert.Equal(3951, Assert.Single(second).Identity?.MikanId);
        Assert.Single(secondHttp.Requests);
    }

    private static RssFeedDocument Feed(Uri episodeUri) => new(
        [new RssFeedItem(
            "Example [01]",
            episodeUri.AbsoluteUri,
            "https://mikanime.tv/Download/example.torrent",
            "application/x-bittorrent",
            42,
            "2026-08-13")],
        null);

    private static string IdentityHtml(int mikanId, int groupId) =>
        $"<a class='mikan-rss' href='/RSS/Bangumi?bangumiId={mikanId}&amp;subgroupid={groupId}'>RSS</a>";

    private sealed class FakeHttpClient : IRssFeedHttpClient
    {
        private readonly Func<Uri, string> _response;

        public FakeHttpClient(string response) : this(_ => response)
        {
        }

        public FakeHttpClient(Func<Uri, string> response)
        {
            _response = response;
        }

        public List<Uri> Requests { get; } = [];

        public Task<ReadOnlyMemory<byte>> GetAsync(
            Uri uri,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(uri);
            return Task.FromResult<ReadOnlyMemory<byte>>(Encoding.UTF8.GetBytes(_response(uri)));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
