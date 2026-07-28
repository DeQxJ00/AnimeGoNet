using System.Net;
using System.Text.Json;
using AnimeGoNet.Data.Cache;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeGoNet.App.Tests.Api;

public sealed class LegacyBoltApiTests
{
    [Fact]
    public async Task ListAndGetPreserveLegacyEnvelopeAndJsonValue()
    {
        await using var app = await RunningApp.StartAsync();
        var store = app.App.Services.GetRequiredService<SqliteJsonCacheStore>();
        var now = DateTimeOffset.UtcNow;
        await store.AddBucketAsync("bolt", "empty", now);
        await store.PutBatchJsonAsync(
            "bolt",
            "items",
            [
                new CacheEntryWrite("z", "\"last\""),
                new CacheEntryWrite("a", """{"name":"first","count":2}"""),
            ],
            TimeSpan.FromHours(1),
            now);

        using var bucketsResponse = await app.Client.GetAsync("/api/bolt?type=bucket");
        using var buckets = JsonDocument.Parse(await bucketsResponse.Content.ReadAsStreamAsync());
        Assert.Equal(HttpStatusCode.OK, bucketsResponse.StatusCode);
        Assert.Equal(200, buckets.RootElement.GetProperty("code").GetInt32());
        Assert.False(buckets.RootElement.GetProperty("data").TryGetProperty("bucket", out _));
        Assert.Equal(
            ["empty", "items"],
            buckets.RootElement.GetProperty("data").GetProperty("data")
                .EnumerateArray().Select(value => value.GetString()!).ToArray());

        using var keysResponse = await app.Client.GetAsync(
            "/api/bolt?db=bolt&type=key&bucket=items");
        using var keys = JsonDocument.Parse(await keysResponse.Content.ReadAsStreamAsync());
        Assert.Equal(
            ["a", "z"],
            keys.RootElement.GetProperty("data").GetProperty("data")
                .EnumerateArray().Select(value => value.GetString()!).ToArray());

        using var valueResponse = await app.Client.GetAsync(
            "/api/bolt/value?bucket=items&key=a");
        using var value = JsonDocument.Parse(await valueResponse.Content.ReadAsStreamAsync());
        var data = value.RootElement.GetProperty("data");
        Assert.Equal(200, value.RootElement.GetProperty("code").GetInt32());
        Assert.Equal("first", data.GetProperty("value").GetProperty("name").GetString());
        Assert.Equal(2, data.GetProperty("value").GetProperty("count").GetInt32());
        Assert.Equal(now.AddHours(1).ToUnixTimeSeconds(), data.GetProperty("ttl").GetInt64());
    }

    [Fact]
    public async Task DeleteIsIdempotentAndMissingValueUsesLegacyFailureCode()
    {
        await using var app = await RunningApp.StartAsync();
        var store = app.App.Services.GetRequiredService<SqliteJsonCacheStore>();
        await store.PutJsonAsync(
            "bolt", "items", "key", "\"value\"", null, DateTimeOffset.UtcNow);

        using var delete = await app.Client.DeleteAsync(
            "/api/bolt/value?bucket=items&key=key");
        using var deleted = JsonDocument.Parse(await delete.Content.ReadAsStreamAsync());
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        Assert.Equal(200, deleted.RootElement.GetProperty("code").GetInt32());

        using var repeat = await app.Client.DeleteAsync(
            "/api/bolt/value?bucket=items&key=key");
        using var repeated = JsonDocument.Parse(await repeat.Content.ReadAsStreamAsync());
        Assert.Equal(200, repeated.RootElement.GetProperty("code").GetInt32());

        using var get = await app.Client.GetAsync(
            "/api/bolt/value?bucket=items&key=key");
        using var missing = JsonDocument.Parse(await get.Content.ReadAsStreamAsync());
        Assert.Equal(300, missing.RootElement.GetProperty("code").GetInt32());
        Assert.Equal(JsonValueKind.Null, missing.RootElement.GetProperty("data").ValueKind);
    }

    [Fact]
    public async Task ArchiveNamespaceIsReadableButCannotBeDeletedByCompatibilityApi()
    {
        await using var app = await RunningApp.StartAsync();
        var store = app.App.Services.GetRequiredService<SqliteJsonCacheStore>();
        await store.PutJsonAsync(
            "bolt_sub", "bangumi_sub", "51", """{"name":"CLANNAD"}""",
            null, DateTimeOffset.UtcNow);

        using var get = await app.Client.GetAsync(
            "/api/bolt/value?db=bolt_sub&bucket=bangumi_sub&key=51");
        using var found = JsonDocument.Parse(await get.Content.ReadAsStreamAsync());
        Assert.Equal(200, found.RootElement.GetProperty("code").GetInt32());
        Assert.Equal(
            "CLANNAD",
            found.RootElement.GetProperty("data").GetProperty("value")
                .GetProperty("name").GetString());

        using var delete = await app.Client.DeleteAsync(
            "/api/bolt/value?db=bolt_sub&bucket=bangumi_sub&key=51");
        using var rejected = JsonDocument.Parse(await delete.Content.ReadAsStreamAsync());
        Assert.Equal(300, rejected.RootElement.GetProperty("code").GetInt32());
        Assert.NotNull(await store.GetJsonAsync(
            "bolt_sub", "bangumi_sub", "51", DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task CompatibilityEndpointsRequireConfiguredAccessKey()
    {
        const string accessKey = "cache-test-access";
        await using var app = await RunningApp.StartAsync(accessKey);

        using var unauthorized = await app.Client.GetAsync("/api/bolt?type=bucket");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/bolt?type=bucket");
        request.Headers.Add("X-AnimeGo-Access-Key", accessKey);
        using var authorized = await app.Client.SendAsync(request);
        using var json = JsonDocument.Parse(await authorized.Content.ReadAsStreamAsync());
        Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);
        Assert.Equal(200, json.RootElement.GetProperty("code").GetInt32());
    }
}
