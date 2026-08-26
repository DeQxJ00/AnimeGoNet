using System.IO.Compression;
using System.Text;
using AnimeGoNet.Data.Metadata;
using AnimeGoNet.Data.Sqlite;

namespace AnimeGoNet.Data.Tests.Metadata;

public sealed class AnidbTitleCacheStoreTests
{
    [Fact]
    public async Task ImportsAndQueriesTitlesByAidAndNormalizedText()
    {
        var root = Path.Combine(Path.GetTempPath(), $"animegonet-anidb-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var database = new AnimeGoSqliteDatabase(Path.Combine(root, "test.db"));
            await database.InitializeAsync();
            var store = new AnidbTitleCacheStore(database);
            var now = new DateTimeOffset(2026, 8, 26, 1, 2, 3, TimeSpan.Zero);
            await using var gzip = Archive("""
                <?xml version="1.0" encoding="UTF-8"?>
                <animetitles>
                  <anime aid="1">
                    <title xml:lang="x-jat" type="main">Seikai no Monshou</title>
                    <title xml:lang="ja" type="official">星界の紋章</title>
                  </anime>
                  <anime aid="2">
                    <title xml:lang="en" type="main">Cowboy Bebop</title>
                  </anime>
                </animetitles>
                """);

            var imported = await store.ImportGzipAsync(
                gzip, "https://anidb.net/api/anime-titles.xml.gz", "\"etag\"", null,
                gzip.Length, now, now.AddHours(24));
            var byAid = await store.ListAsync(1, 25, null, 1);
            var byName = await store.ListAsync(1, 25, "  COWBOY   BEBOP ", null);
            var status = await store.GetStatusAsync();

            Assert.Equal(2, imported.AnimeCount);
            Assert.Equal(3, imported.TitleCount);
            Assert.Equal(2, byAid.TotalItems);
            Assert.All(byAid.Items, item => Assert.Equal(1, item.Aid));
            Assert.Equal("Cowboy Bebop", Assert.Single(byName.Items).Title);
            Assert.Equal("completed", status.LastStatus);
            Assert.Equal(2, status.AnimeCount);
            Assert.Equal(3, status.TitleCount);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task InvalidArchiveRollsBackAndPreservesPreviousTitles()
    {
        var root = Path.Combine(Path.GetTempPath(), $"animegonet-anidb-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var database = new AnimeGoSqliteDatabase(Path.Combine(root, "test.db"));
            await database.InitializeAsync();
            var store = new AnidbTitleCacheStore(database);
            await using (var valid = Archive("""
                <animetitles><anime aid="7"><title xml:lang="en" type="main">Seven</title></anime></animetitles>
                """))
            {
                await store.ImportGzipAsync(
                    valid, "https://anidb.net/api/anime-titles.xml.gz", null, null,
                    valid.Length, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(24));
            }
            await using var invalid = Archive("<animetitles><anime aid=\"bad\"></anime></animetitles>");

            await Assert.ThrowsAsync<InvalidDataException>(() => store.ImportGzipAsync(
                invalid, "https://anidb.net/api/anime-titles.xml.gz", null, null,
                invalid.Length, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(24)));

            var retained = await store.ListAsync(1, 25, null, 7);
            Assert.Equal("Seven", Assert.Single(retained.Items).Title);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static MemoryStream Archive(string xml)
    {
        var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(Encoding.UTF8.GetBytes(xml));
        }
        output.Position = 0;
        return output;
    }
}
