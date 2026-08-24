using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AnimeGoNet.App.Metadata;
using AnimeGoNet.App.Torrents;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeGoNet.App.Tests.Api;

public sealed class MikanPublishGroupApiTests
{
    [Fact]
    public void ParserRemovesDirectorySuffixAndDecodesHtml()
    {
        var value = MikanPublishGroupNameParser.Parse(Encoding.UTF8.GetBytes(
            "<html><title>Kirara &amp; Fantasia作品年表</title></html>"));
        Assert.Equal("Kirara & Fantasia", value);
    }

    [Fact]
    public async Task ConfirmedTaskGroupIsFetchedListedAndEditable()
    {
        var transport = new PublishGroupTransport();
        await using var app = await RunningApp.StartAsync(
            rssDnsResolver: new PublicDnsResolver(), rssHttpTransport: transport);
        using var ingest = await app.Client.PostAsJsonAsync("/api/download/manager", new
        {
            source = "mikan",
            data = new[]
            {
                new
                {
                    torrent = "https://mikanani.me/Download/group-test.torrent",
                    info = new
                    {
                        title = "字幕组测试 01",
                        source_item_id = "group-test",
                        source_work_id = "3981",
                        url = "https://mikanani.me/Home/Bangumi/3981",
                        mikanid = 3981,
                        groupid = 392,
                    },
                },
            },
        });
        Assert.Equal(HttpStatusCode.OK, ingest.StatusCode);
        using (var ingestJson = JsonDocument.Parse(await ingest.Content.ReadAsStreamAsync()))
            Assert.Equal(200, ingestJson.RootElement.GetProperty("code").GetInt32());
        Assert.True(await app.App.Services.GetRequiredService<MikanPublishGroupResolver>().RunOnceAsync());

        using var listed = await app.Client.GetAsync("/api/v1/mikan/publish-groups");
        using var json = JsonDocument.Parse(await listed.Content.ReadAsStreamAsync());
        var item = Assert.Single(json.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(392, item.GetProperty("groupid").GetInt32());
        Assert.Equal("Kirara Fantasia", item.GetProperty("group_name").GetString());
        Assert.Equal("automatic", item.GetProperty("name_source").GetString());
        Assert.Equal("/Home/PublishGroup/392", Assert.Single(transport.Requests).AbsolutePath);

        using var updated = await app.Client.PutAsJsonAsync(
            "/api/v1/mikan/publish-groups/392",
            new { group_name = "人工名称", expected_revision = item.GetProperty("revision").GetInt64() });
        Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);
    }

    private sealed class PublicDnsResolver : ITorrentDnsResolver
    {
        public ValueTask<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<IPAddress>>([IPAddress.Parse("1.1.1.1")]);
    }

    private sealed class PublishGroupTransport : ITorrentHttpTransport
    {
        public List<Uri> Requests { get; } = [];
        public ValueTask<TorrentHttpResponse> SendAsync(
            Uri uri,
            IReadOnlyList<IPAddress> validatedAddresses,
            CancellationToken cancellationToken)
        {
            Requests.Add(uri);
            var bytes = Encoding.UTF8.GetBytes("<h1>Kirara Fantasia作品年表</h1>");
            return ValueTask.FromResult(new TorrentHttpResponse(
                HttpStatusCode.OK, null, bytes.Length, new MemoryStream(bytes, writable: false)));
        }
    }
}
