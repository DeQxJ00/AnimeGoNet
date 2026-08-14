using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AnimeGoNet.App.Api;
using AnimeGoNet.App.Serialization;
using AnimeGoNet.Data.Cache;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeGoNet.App.Tests.Api;

public sealed class CacheBrowserApiTests
{
    [Fact]
    public async Task ListsPlainBucketAndKeyAndLoadsFullValueOnDemand()
    {
        await using var app = await RunningApp.StartAsync();
        var store = app.App.Services.GetRequiredService<SqliteJsonCacheStore>();
        const string secretBucket = "private-passkey-bucket";
        const string secretKey = "https://tracker.invalid/passkey/private.torrent";
        const string secretValue = "{\"password\":\"not-for-the-browser\"}";
        await store.PutJsonAsync(
            "bolt", secretBucket, secretKey, secretValue, null, DateTimeOffset.UtcNow);

        using var bucketResponse = await app.Client.GetAsync("/api/v1/cache/buckets?database=bolt");
        var bucketJson = await bucketResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, bucketResponse.StatusCode);
        using var bucketDocument = JsonDocument.Parse(bucketJson);
        var bucket = Assert.Single(bucketDocument.RootElement.GetProperty("items").EnumerateArray());
        var bucketId = bucket.GetProperty("bucket_id").GetString();
        Assert.Equal(64, bucketId?.Length);
        Assert.Equal(secretBucket, bucket.GetProperty("bucket_name").GetString());
        Assert.Equal(1, bucket.GetProperty("entry_count").GetInt32());

        using var entriesResponse = await app.Client.GetAsync(
            $"/api/v1/cache/entries?database=bolt&bucket_id={bucketId}&page=1&page_size=25");
        var entriesJson = await entriesResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, entriesResponse.StatusCode);
        Assert.DoesNotContain("not-for-the-browser", entriesJson, StringComparison.Ordinal);
        using var entriesDocument = JsonDocument.Parse(entriesJson);
        var entry = Assert.Single(entriesDocument.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(64, entry.GetProperty("entry_id").GetString()?.Length);
        Assert.Equal(64, entry.GetProperty("delete_token").GetString()?.Length);
        Assert.Equal(secretKey, entry.GetProperty("key").GetString());
        Assert.Equal(System.Text.Encoding.UTF8.GetByteCount(secretValue), entry.GetProperty("value_bytes").GetInt32());

        var entryId = entry.GetProperty("entry_id").GetString();
        using var detailResponse = await app.Client.GetAsync(
            $"/api/v1/cache/entries/{entryId}?database=bolt&bucket_id={bucketId}");
        using var detail = JsonDocument.Parse(
            await detailResponse.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        Assert.Equal(secretBucket, detail.RootElement.GetProperty("bucket_name").GetString());
        Assert.Equal(secretKey, detail.RootElement.GetProperty("key").GetString());
        Assert.Equal(secretValue, detail.RootElement.GetProperty("value_json").GetString());
        Assert.Equal(
            System.Text.Encoding.UTF8.GetByteCount(secretValue),
            detail.RootElement.GetProperty("value_bytes").GetInt32());
    }

    [Fact]
    public async Task DeleteRequiresFreshOpaqueTokenAndRejectsReadOnlyNamespace()
    {
        await using var app = await RunningApp.StartAsync();
        var store = app.App.Services.GetRequiredService<SqliteJsonCacheStore>();
        var now = DateTimeOffset.UtcNow;
        await store.PutJsonAsync("bolt", "mutable", "key", "1", null, now);
        await store.PutJsonAsync("bolt_sub", "archive", "key", "1", null, now);
        var mutable = await ReadSingleEntryAsync(app, "bolt");
        var archive = await ReadSingleEntryAsync(app, "bolt_sub");

        using var archiveDetailResponse = await app.Client.GetAsync(
            $"/api/v1/cache/entries/{archive.EntryId}"
            + $"?database=bolt_sub&bucket_id={archive.BucketId}");
        using var archiveDetail = JsonDocument.Parse(
            await archiveDetailResponse.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, archiveDetailResponse.StatusCode);
        Assert.True(archiveDetail.RootElement.GetProperty("read_only").GetBoolean());
        Assert.Equal("archive", archiveDetail.RootElement.GetProperty("bucket_name").GetString());
        Assert.Equal("key", archiveDetail.RootElement.GetProperty("key").GetString());
        Assert.Equal("1", archiveDetail.RootElement.GetProperty("value_json").GetString());

        await store.PutJsonAsync("bolt", "mutable", "key", "2", null, now.AddSeconds(1));
        using var staleResponse = await DeleteAsync(app, "bolt", mutable);
        Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);
        Assert.Equal("cache_entry_changed", await ReadErrorCodeAsync(staleResponse));

        using var readOnlyResponse = await DeleteAsync(app, "bolt_sub", archive);
        Assert.Equal(HttpStatusCode.Conflict, readOnlyResponse.StatusCode);
        Assert.Equal("cache_namespace_read_only", await ReadErrorCodeAsync(readOnlyResponse));

        var refreshed = await ReadSingleEntryAsync(app, "bolt");
        using var deleted = await DeleteAsync(app, "bolt", refreshed);
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        using var missing = await DeleteAsync(app, "bolt", refreshed);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task ValidatesQueriesAndUsesAccessKeyMiddleware()
    {
        await using var app = await RunningApp.StartAsync(accessKey: "cache-test-key");
        using var unauthorized = await app.Client.GetAsync("/api/v1/cache/buckets");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        using var invalid = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/cache/entries?database=other&bucket_id=bad&page=-1&page_size=500");
        invalid.Headers.Add("X-AnimeGo-WebUI-Access-Key", "cache-test-key");
        using var invalidResponse = await app.Client.SendAsync(invalid);
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
        Assert.Equal("cache_query_invalid", await ReadErrorCodeAsync(invalidResponse));
    }

    private static async Task<(string BucketId, string EntryId, string DeleteToken)> ReadSingleEntryAsync(
        RunningApp app,
        string database)
    {
        using var bucketsResponse = await app.Client.GetAsync($"/api/v1/cache/buckets?database={database}");
        using var buckets = JsonDocument.Parse(await bucketsResponse.Content.ReadAsStringAsync());
        var bucketId = Assert.Single(buckets.RootElement.GetProperty("items").EnumerateArray())
            .GetProperty("bucket_id").GetString()!;
        using var entriesResponse = await app.Client.GetAsync(
            $"/api/v1/cache/entries?database={database}&bucket_id={bucketId}");
        using var entries = JsonDocument.Parse(await entriesResponse.Content.ReadAsStringAsync());
        var entry = Assert.Single(entries.RootElement.GetProperty("items").EnumerateArray());
        return (
            bucketId,
            entry.GetProperty("entry_id").GetString()!,
            entry.GetProperty("delete_token").GetString()!);
    }

    private static Task<HttpResponseMessage> DeleteAsync(
        RunningApp app,
        string database,
        (string BucketId, string EntryId, string DeleteToken) entry) =>
        app.Client.SendAsync(new HttpRequestMessage(
            HttpMethod.Delete,
            $"/api/v1/cache/entries/{entry.EntryId}")
        {
            Content = JsonContent.Create(
                new CacheBrowserDeleteRequest(database, entry.BucketId, entry.DeleteToken),
                ApiJsonContext.Default.CacheBrowserDeleteRequest),
        });

    private static async Task<string?> ReadErrorCodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("code").GetString();
    }
}
