using System.Text;
using AnimeGoNet.App.Feeds;
using AnimeGoNet.Core.Feeds;
using AnimeGoNet.Data.Cache;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeGoNet.App.Tests.Feeds;

public sealed class MikanBangumiIdentityCacheTests
{
    [Fact]
    public async Task SuccessfulDiscoveryIsSharedAcrossResolversUntilConfiguredExpiry()
    {
        await using var app = await RunningApp.StartAsync();
        var store = app.App.Services.GetRequiredService<SqliteJsonCacheStore>();
        var now = new DateTimeOffset(2026, 8, 13, 0, 0, 0, TimeSpan.Zero);
        var firstHttp = new FakeHttpClient(WorkHtml(547888));
        var firstCache = new MikanBangumiIdentityCache(
            store,
            TimeSpan.FromHours(8760),
            new FixedTimeProvider(now));

        var first = await new MikanBangumiSubjectResolver(firstHttp, firstCache)
            .ResolveAsync(Feed(3951));

        Assert.Equal(547888, first.BangumiSubjectId);
        Assert.Single(firstHttp.Requests);
        var stored = Assert.IsType<CacheJsonValue>(await store.GetJsonAsync(
            MikanBangumiIdentityCache.DatabaseName,
            MikanBangumiIdentityCache.BucketName,
            "3951",
            now));
        Assert.Equal(now.AddHours(8760), stored.ExpiresAtUtc);
        Assert.Equal(
            "{\"schema_version\":1,\"mikanid\":3951,\"bgmid\":547888}",
            stored.ValueJson);

        var cachedHttp = new FakeHttpClient(_ => throw new InvalidOperationException(
            "The mikanid to bgmid cache should satisfy the new resolver."));
        var cached = await new MikanBangumiSubjectResolver(
                cachedHttp,
                new MikanBangumiIdentityCache(
                    store,
                    TimeSpan.FromHours(8760),
                    new FixedTimeProvider(now.AddDays(364))))
            .ResolveAsync(Feed(3951));

        Assert.Equal(547888, cached.BangumiSubjectId);
        Assert.Empty(cachedHttp.Requests);

        var refreshedHttp = new FakeHttpClient(WorkHtml(590786));
        var refreshed = await new MikanBangumiSubjectResolver(
                refreshedHttp,
                new MikanBangumiIdentityCache(
                    store,
                    TimeSpan.FromHours(8760),
                    new FixedTimeProvider(now.AddDays(366))))
            .ResolveAsync(Feed(3951));

        Assert.Equal(590786, refreshed.BangumiSubjectId);
        Assert.Single(refreshedHttp.Requests);
    }

    [Fact]
    public async Task FailedDiscoveryIsNotCachedAndNextAttemptCanRecover()
    {
        await using var app = await RunningApp.StartAsync();
        var store = app.App.Services.GetRequiredService<SqliteJsonCacheStore>();
        var cache = new MikanBangumiIdentityCache(store, TimeSpan.FromHours(8760));
        var failedHttp = new FakeHttpClient("<html>missing subject</html>");

        var failed = await new MikanBangumiSubjectResolver(failedHttp, cache)
            .ResolveAsync(Feed(3951));

        Assert.Equal(MikanBangumiDiscoveryStates.NotFound, failed.State);
        Assert.Null(await cache.GetAsync(3951));

        var recoveredHttp = new FakeHttpClient(WorkHtml(547888));
        var recovered = await new MikanBangumiSubjectResolver(recoveredHttp, cache)
            .ResolveAsync(Feed(3951));

        Assert.Equal(547888, recovered.BangumiSubjectId);
        Assert.Single(recoveredHttp.Requests);
        Assert.Equal(547888, await cache.GetAsync(3951));
    }

    [Fact]
    public async Task ZeroHoursMeansPermanentAndEntryRemainsVisibleAndDeletable()
    {
        await using var app = await RunningApp.StartAsync();
        var store = app.App.Services.GetRequiredService<SqliteJsonCacheStore>();
        var now = new DateTimeOffset(2026, 8, 13, 0, 0, 0, TimeSpan.Zero);
        var cache = new MikanBangumiIdentityCache(
            store,
            TimeSpan.Zero,
            new FixedTimeProvider(now));

        await cache.PutAsync(3951, 547888);

        var stored = Assert.IsType<CacheJsonValue>(await store.GetJsonAsync(
            MikanBangumiIdentityCache.DatabaseName,
            MikanBangumiIdentityCache.BucketName,
            "3951",
            now.AddYears(20)));
        Assert.Null(stored.ExpiresAtUtc);
        var bucket = Assert.Single(
            await store.ListBrowserBucketsAsync("bolt", now.AddYears(20)),
            item => item.BucketName == MikanBangumiIdentityCache.BucketName);
        var entry = Assert.Single((await store.ListBrowserEntriesAsync(
            "bolt", bucket.BucketId, 1, 10, now.AddYears(20)))!.Items);
        var detail = Assert.IsType<CacheBrowserEntryDetail>(await store.GetBrowserEntryAsync(
            "bolt", bucket.BucketId, entry.EntryId, now.AddYears(20)));
        Assert.Equal("3951", detail.Key);
        Assert.Contains("\"bgmid\":547888", detail.ValueJson, StringComparison.Ordinal);
        Assert.Equal(
            CacheBrowserDeleteResult.Deleted,
            await store.DeleteBrowserEntryAsync(
                "bolt", bucket.BucketId, entry.EntryId, entry.DeleteToken));
        Assert.Null(await cache.GetAsync(3951));
    }

    private static RssFeedDocument Feed(int mikanId) => new(
        [new RssFeedItem(
            "Example [01]",
            "https://mikanime.tv/Home/Episode/0123456789abcdef0123456789abcdef01234567",
            "https://mikanime.tv/Download/example.torrent",
            "application/x-bittorrent",
            42,
            "2026-08-13")],
        mikanId);

    private static string WorkHtml(int bangumiId) =>
        $"<p class='bangumi-info'><a href='https://bgm.tv/subject/{bangumiId}'>Bangumi</a></p>";

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
