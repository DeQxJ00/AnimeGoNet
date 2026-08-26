using System.IO.Compression;
using System.Net;
using System.Text;
using AnimeGoNet.App.Metadata;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Data.Metadata;
using AnimeGoNet.Data.Sqlite;

namespace AnimeGoNet.App.Tests.Metadata;

public sealed class AnidbTitleCacheServiceTests
{
    [Fact]
    public async Task RefreshDownloadsImportsAndPersistsOfficialArchive()
    {
        var root = Path.Combine(Path.GetTempPath(), $"animegonet-anidb-service-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var options = AnimeGoDefaults.CreateNative(root);
            var layout = DirectoryLayout.From(options.Paths);
            layout.CreateDataDirectories();
            var database = new AnimeGoSqliteDatabase(layout.DatabaseFile);
            await database.InitializeAsync();
            var store = new AnidbTitleCacheStore(database);
            var payload = Archive("""
                <animetitles><anime aid="42"><title xml:lang="en" type="main">The Answer</title></anime></animetitles>
                """);
            using var client = new HttpClient(new StaticHandler(payload));
            using var service = new AnidbTitleCacheService(client, layout, store);

            var result = await service.RefreshAsync(force: true);

            Assert.Equal("completed", result.Status);
            Assert.Equal(1, result.AnimeCount);
            Assert.Equal("The Answer", Assert.Single(
                (await store.ListAsync(1, 25, null, 42)).Items).Title);
            Assert.Equal(payload, await File.ReadAllBytesAsync(Path.Combine(
                layout.CachePath, "anidb", "anime-titles.xml.gz")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static byte[] Archive(string xml)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(Encoding.UTF8.GetBytes(xml));
        }
        return output.ToArray();
    }

    private sealed class StaticHandler(byte[] payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.Equal(AnidbTitleCacheService.DefaultSourceUrl, request.RequestUri?.ToString());
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload),
                RequestMessage = request,
            };
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"v1\"");
            return Task.FromResult(response);
        }
    }
}
