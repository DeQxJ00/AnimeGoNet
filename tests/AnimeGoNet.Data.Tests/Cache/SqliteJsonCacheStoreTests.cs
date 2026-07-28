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
}
