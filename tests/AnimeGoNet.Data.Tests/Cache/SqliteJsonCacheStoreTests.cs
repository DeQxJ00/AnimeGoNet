using AnimeGoNet.Data.Cache;

namespace AnimeGoNet.Data.Tests.Cache;

public sealed class SqliteJsonCacheStoreTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task PutAndGetPreserveJsonAndAbsoluteExpiry()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var store = new SqliteJsonCacheStore(fixture.Database);

        await store.PutJsonAsync(
            "BOLT",
            "tmdb",
            "series:42",
            """{ "name": "Example", "season": 2 }""",
            TimeSpan.FromMinutes(5),
            Now);

        var value = await store.GetJsonAsync(
            "bolt", "tmdb", "series:42", Now.AddMinutes(4));
        Assert.NotNull(value);
        Assert.Equal("bolt", value.DatabaseName);
        Assert.Equal("tmdb", value.Bucket);
        Assert.Equal("""{ "name": "Example", "season": 2 }""", value.ValueJson);
        Assert.Equal(Now.AddMinutes(5), value.ExpiresAtUtc);
        Assert.Equal(Now, value.UpdatedAtUtc);
    }

    [Fact]
    public async Task ExpiryBoundaryDeletesEntryButPreservesBucket()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var store = new SqliteJsonCacheStore(fixture.Database);
        await store.PutJsonAsync(
            "bolt", "short", "key", "\"value\"", TimeSpan.FromSeconds(30), Now);

        Assert.NotNull(await store.GetJsonAsync(
            "bolt", "short", "key", Now.AddSeconds(29)));
        Assert.Null(await store.GetJsonAsync(
            "bolt", "short", "key", Now.AddSeconds(30)));
        Assert.Empty(await store.ListKeysAsync(
            "bolt", "short", Now.AddSeconds(30)));
        Assert.Equal(["short"], await store.ListBucketsAsync("bolt"));
    }

    [Fact]
    public async Task BatchIsValidatedBeforeAnyRowsAreWritten()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var store = new SqliteJsonCacheStore(fixture.Database);

        await Assert.ThrowsAnyAsync<System.Text.Json.JsonException>(() =>
            store.PutBatchJsonAsync(
                "bolt",
                "batch",
                [
                    new CacheEntryWrite("valid", """{"ok":true}"""),
                    new CacheEntryWrite("invalid", "{"),
                ],
                null,
                Now));

        Assert.Empty(await store.ListBucketsAsync("bolt"));
    }

    [Fact]
    public async Task BatchOverwriteUsesOneExpiryAndLastDuplicateWins()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var store = new SqliteJsonCacheStore(fixture.Database);
        await store.PutBatchJsonAsync(
            "bolt",
            "batch",
            [
                new CacheEntryWrite("b", "1"),
                new CacheEntryWrite("a", "2"),
                new CacheEntryWrite("a", "3"),
            ],
            TimeSpan.FromHours(1),
            Now);

        Assert.Equal(["a", "b"], await store.ListKeysAsync("bolt", "batch", Now));
        var value = await store.GetJsonAsync("bolt", "batch", "a", Now);
        Assert.NotNull(value);
        Assert.Equal("3", value.ValueJson);
        Assert.Equal(Now.AddHours(1), value.ExpiresAtUtc);
    }

    [Fact]
    public async Task DatabaseNamespacesAreIsolatedAndDeleteIsIdempotent()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var store = new SqliteJsonCacheStore(fixture.Database);
        await store.PutJsonAsync("bolt", "shared", "key", "\"mutable\"", null, Now);
        await store.PutJsonAsync("bolt_sub", "shared", "key", "\"archive\"", null, Now);

        Assert.True(await store.DeleteAsync("bolt", "shared", "key"));
        Assert.False(await store.DeleteAsync("bolt", "shared", "key"));
        Assert.Null(await store.GetJsonAsync("bolt", "shared", "key", Now));
        Assert.Equal(
            "\"archive\"",
            (await store.GetJsonAsync("bolt_sub", "shared", "key", Now))?.ValueJson);
    }

    [Fact]
    public async Task PurgeExpiredRemovesOnlyExpiredRowsAcrossNamespaces()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var store = new SqliteJsonCacheStore(fixture.Database);
        await store.PutJsonAsync(
            "bolt", "one", "expired", "1", TimeSpan.FromSeconds(1), Now);
        await store.PutJsonAsync(
            "bolt_sub", "two", "alive", "2", TimeSpan.Zero, Now);

        Assert.Equal(1, await store.PurgeExpiredAsync(Now.AddSeconds(1)));
        Assert.Null(await store.GetJsonAsync(
            "bolt", "one", "expired", Now.AddSeconds(1)));
        Assert.NotNull(await store.GetJsonAsync(
            "bolt_sub", "two", "alive", Now.AddYears(1)));
    }

    [Fact]
    public async Task ConcurrentWritersShareBucketWithoutLosingEntries()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var store = new SqliteJsonCacheStore(fixture.Database);

        await Task.WhenAll(Enumerable.Range(0, 12).Select(index =>
            store.PutJsonAsync(
                "bolt",
                "concurrent",
                $"key-{index:D2}",
                index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                null,
                Now)));

        var keys = await store.ListKeysAsync("bolt", "concurrent", Now);
        Assert.Equal(12, keys.Count);
        Assert.Equal("key-00", keys[0]);
        Assert.Equal("key-11", keys[^1]);
    }

    [Fact]
    public async Task BrowserProjectionUsesOpaqueIdsAndNeverReturnsKeysOrValues()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var store = new SqliteJsonCacheStore(fixture.Database);
        const string secretKey = "https://tracker.invalid/private-passkey/file.torrent";
        const string secretValue = "{\"password\":\"never-return-this\"}";
        await store.PutJsonAsync("bolt", "private-cache", secretKey, secretValue, null, Now);

        var bucket = Assert.Single(await store.ListBrowserBucketsAsync("bolt", Now));
        Assert.Equal(64, bucket.BucketId.Length);
        Assert.Equal(1, bucket.EntryCount);
        Assert.DoesNotContain("private-cache", bucket.ToString(), StringComparison.Ordinal);

        var page = await store.ListBrowserEntriesAsync("bolt", bucket.BucketId, 1, 25, Now);
        Assert.NotNull(page);
        var entry = Assert.Single(page.Items);
        Assert.Equal(64, entry.EntryId.Length);
        Assert.Equal(64, entry.DeleteToken.Length);
        Assert.Equal(System.Text.Encoding.UTF8.GetByteCount(secretValue), entry.ValueBytes);
        Assert.DoesNotContain(secretKey, page.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("never-return-this", page.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task BrowserPagingPurgesExpiredAndKeepsStableBinaryOrder()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var store = new SqliteJsonCacheStore(fixture.Database);
        await store.PutBatchJsonAsync(
            "bolt",
            "page",
            [
                new CacheEntryWrite("c", "3"),
                new CacheEntryWrite("a", "1"),
                new CacheEntryWrite("b", "2"),
            ],
            null,
            Now);
        await store.PutJsonAsync("bolt", "expired", "gone", "0", TimeSpan.FromSeconds(1), Now);

        var buckets = await store.ListBrowserBucketsAsync("bolt", Now.AddSeconds(1));
        Assert.Equal(2, buckets.Count);
        Assert.Contains(buckets, bucket => bucket.EntryCount == 0);
        var pageBucket = Assert.Single(buckets, bucket => bucket.EntryCount == 3);
        var first = await store.ListBrowserEntriesAsync("bolt", pageBucket.BucketId, 1, 2, Now.AddSeconds(1));
        var second = await store.ListBrowserEntriesAsync("bolt", pageBucket.BucketId, 2, 2, Now.AddSeconds(1));

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(3, first.TotalCount);
        Assert.Equal(2, first.Items.Count);
        Assert.Single(second.Items);
        Assert.DoesNotContain(first.Items[0].EntryId, second.Items.Select(item => item.EntryId));
    }

    [Fact]
    public async Task BrowserDeleteRequiresFreshTokenAndMutableNamespace()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var store = new SqliteJsonCacheStore(fixture.Database);
        await store.PutJsonAsync("bolt", "mutable", "key", "1", null, Now);
        await store.PutJsonAsync("bolt_sub", "archive", "key", "1", null, Now);
        var mutableBucket = Assert.Single(await store.ListBrowserBucketsAsync("bolt", Now));
        var archiveBucket = Assert.Single(await store.ListBrowserBucketsAsync("bolt_sub", Now));
        var mutable = Assert.Single((await store.ListBrowserEntriesAsync(
            "bolt", mutableBucket.BucketId, 1, 10, Now))!.Items);
        var archive = Assert.Single((await store.ListBrowserEntriesAsync(
            "bolt_sub", archiveBucket.BucketId, 1, 10, Now))!.Items);

        await store.PutJsonAsync("bolt", "mutable", "key", "2", null, Now.AddSeconds(1));
        Assert.Equal(
            CacheBrowserDeleteResult.Changed,
            await store.DeleteBrowserEntryAsync(
                "bolt", mutableBucket.BucketId, mutable.EntryId, mutable.DeleteToken));
        Assert.Equal(
            CacheBrowserDeleteResult.ReadOnly,
            await store.DeleteBrowserEntryAsync(
                "bolt_sub", archiveBucket.BucketId, archive.EntryId, archive.DeleteToken));

        var refreshed = Assert.Single((await store.ListBrowserEntriesAsync(
            "bolt", mutableBucket.BucketId, 1, 10, Now.AddSeconds(1)))!.Items);
        Assert.Equal(
            CacheBrowserDeleteResult.Deleted,
            await store.DeleteBrowserEntryAsync(
                "bolt", mutableBucket.BucketId, refreshed.EntryId, refreshed.DeleteToken));
        Assert.Equal(
            CacheBrowserDeleteResult.NotFound,
            await store.DeleteBrowserEntryAsync(
                "bolt", mutableBucket.BucketId, refreshed.EntryId, refreshed.DeleteToken));
    }
}
