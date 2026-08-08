using System.Text;
using AnimeGoNet.Data.Cache;

namespace AnimeGoNet.Data.Tests.Cache;

public sealed class LegacyCacheImporterTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ImportPreservesKnownBucketsJsonKeysValuesAndAbsoluteExpiry()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var importer = new LegacyCacheImporter(fixture.Database);

        var report = await importer.ImportAsync(Stream(Package()), Now);

        Assert.Equal("imported", report.Status);
        Assert.Equal(64, report.PackageSha256.Length);
        Assert.Equal(6, report.BucketCount);
        Assert.Equal(3, report.EntryCount);
        Assert.Equal(2, report.ImportedEntryCount);
        Assert.Equal(1, report.SkippedExpiredEntryCount);
        Assert.Equal(0, report.RepeatCount);
        var store = new SqliteJsonCacheStore(fixture.Database);
        Assert.Equal(
            ["bangumi", "hash2entity", "mikan", "name2hash", "themoviedb"],
            await store.ListBucketsAsync("bolt"));
        Assert.Equal(["bangumi_sub"], await store.ListBucketsAsync("bolt_sub"));

        var mikan = await store.GetJsonAsync("bolt", "mikan", "[\"url\",42]", Now);
        Assert.NotNull(mikan);
        Assert.Equal("{\"Params\":{\"Values\":[42]}}", mikan.ValueJson);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(2_000_000_000), mikan.ExpiresAtUtc);
        Assert.Null(await store.GetJsonAsync("bolt", "mikan", "\"expired\"", Now));
        var tmdb = await store.GetJsonAsync("bolt", "themoviedb", "[\"series\"]", Now);
        Assert.NotNull(tmdb);
        Assert.Equal("[1,2,3]", tmdb.ValueJson);
        Assert.Null(tmdb.ExpiresAtUtc);
    }

    [Fact]
    public async Task SameSemanticPackageIsIdempotentAndDoesNotOverwriteNewerCacheData()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var importer = new LegacyCacheImporter(fixture.Database);
        var first = await importer.ImportAsync(Stream(Package()), Now);
        var store = new SqliteJsonCacheStore(fixture.Database);
        await store.PutJsonAsync(
            "bolt",
            "mikan",
            "[\"url\",42]",
            "{\"fresh\":true}",
            null,
            Now.AddMinutes(1));

        var reformatted = Package()
            .Replace(
                "2026-08-08T11:00:00.0000000Z",
                "2026-08-08T11:30:00.0000000Z",
                StringComparison.Ordinal)
            .Replace("{\"format\"", "{\n  \"format\"", StringComparison.Ordinal);
        var second = await importer.ImportAsync(Stream(reformatted), Now.AddMinutes(2));

        Assert.Equal(first.PackageSha256, second.PackageSha256);
        Assert.Equal("already_imported", second.Status);
        Assert.Equal(1, second.RepeatCount);
        Assert.Equal(first.ImportedAtUtc, second.ImportedAtUtc);
        Assert.Equal(Now.AddMinutes(2), second.LastSeenAtUtc);
        var current = await store.GetJsonAsync(
            "bolt", "mikan", "[\"url\",42]", Now.AddMinutes(2));
        Assert.NotNull(current);
        Assert.Equal("{\"fresh\":true}", current.ValueJson);
        Assert.Null(current.ExpiresAtUtc);
    }

    [Theory]
    [InlineData("private", "legacy_cache_bucket_invalid")]
    [InlineData("bangumi_sub", "legacy_cache_bucket_invalid")]
    public async Task UnknownOrCrossNamespaceBucketRejectsWholePackage(
        string bucket,
        string expectedCode)
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var importer = new LegacyCacheImporter(fixture.Database);
        var package = $$"""
            {
              "format":"animego-legacy-cache",
              "version":1,
              "source_commit":"develop@c7475dfc55a374cd0dd08821bf17125dab1e3145",
              "exported_at_utc":"2026-08-08T11:00:00.0000000Z",
              "databases":[{
                "name":"bolt",
                "buckets":[{"name":"{{bucket}}","entries":[{
                  "key_json":"key",
                  "value_json":{"secret":"must-not-be-written"},
                  "expires_at_unix_seconds":0
                }]}]
              }]
            }
            """;

        var exception = await Assert.ThrowsAsync<LegacyCacheImportException>(
            () => importer.ImportAsync(Stream(package), Now));

        Assert.Equal(expectedCode, exception.Code);
        Assert.DoesNotContain("must-not-be-written", exception.Message, StringComparison.Ordinal);
        var store = new SqliteJsonCacheStore(fixture.Database);
        Assert.Empty(await store.ListBucketsAsync("bolt"));
        Assert.Equal(0L, await AuditCountAsync(fixture));
    }

    [Fact]
    public async Task DuplicateKeyRejectsWholePackageBeforeOpeningWriteTransaction()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var importer = new LegacyCacheImporter(fixture.Database);
        const string package = """
            {
              "format":"animego-legacy-cache",
              "version":1,
              "source_commit":"develop@c7475dfc55a374cd0dd08821bf17125dab1e3145",
              "exported_at_utc":"2026-08-08T11:00:00.0000000Z",
              "databases":[{
                "name":"bolt",
                "buckets":[{"name":"mikan","entries":[
                  {"key_json":[1],"value_json":1,"expires_at_unix_seconds":0},
                  {"key_json":[1],"value_json":2,"expires_at_unix_seconds":0}
                ]}]
              }]
            }
            """;

        var exception = await Assert.ThrowsAsync<LegacyCacheImportException>(
            () => importer.ImportAsync(Stream(package), Now));

        Assert.Equal("legacy_cache_key_invalid", exception.Code);
        Assert.Empty(await new SqliteJsonCacheStore(fixture.Database).ListBucketsAsync("bolt"));
        Assert.Equal(0L, await AuditCountAsync(fixture));
    }

    [Fact]
    public async Task UnknownJsonMemberFailsClosedWithoutEchoingPackageValues()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var importer = new LegacyCacheImporter(fixture.Database);
        var package = Package().Replace(
            "\"version\":1",
            "\"version\":1,\"unexpected\":\"local-secret\"",
            StringComparison.Ordinal);

        var exception = await Assert.ThrowsAsync<LegacyCacheImportException>(
            () => importer.ImportAsync(Stream(package), Now));

        Assert.Equal("legacy_cache_package_invalid_json", exception.Code);
        Assert.DoesNotContain("local-secret", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0L, await AuditCountAsync(fixture));
    }

    private static string Package() => File.ReadAllText(Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..",
        "..",
        "..",
        "..",
        "..",
        "eng",
        "fixtures",
        "legacy-cache-export-v1.json")));

    private static MemoryStream Stream(string value) =>
        new(Encoding.UTF8.GetBytes(value), writable: false);

    private static async Task<long> AuditCountAsync(SqliteDatabaseFixture fixture)
    {
        await using var connection = await fixture.Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM legacy_cache_imports;";
        return (long)(await command.ExecuteScalarAsync() ?? -1L);
    }
}
