using System.Net;
using System.Text;
using System.Text.Json;
using AnimeGoNet.App.Torrents;

namespace AnimeGoNet.App.Tests.Api;

public sealed class LegacyRssApiTests
{
    private const string FeedXml = """
        <rss><channel><link>https://mikanani.me/RSS?bangumiId=3951</link>
          <item><title>Show [03] [1080p]</title><link>https://mikanani.me/Home/Episode/a</link>
            <enclosure type="application/x-bittorrent" length="42" url="https://mikanani.me/Download/a.torrent" /></item>
          <item><title>Show [04] [1080p]</title><link>https://mikanani.me/Home/Episode/b</link>
            <enclosure type="application/x-bittorrent" length="42" url="https://mikanani.me/Download/b.torrent" /></item>
        </channel></rss>
        """;

    [Fact]
    public async Task LegacyContractFetchesUrlSelectsExactEpisodeLinkAndIngestsWinner()
    {
        var transport = new StaticTransport(_ => Response(HttpStatusCode.OK, FeedXml));
        await using var app = await RunningApp.StartAsync(
            rssDnsResolver: new PublicDnsResolver(), rssHttpTransport: transport);
        const string request = """
            {
              "source": "mikan",
              "rss": { "url": "https://mikanani.me/RSS?bangumiId=3951" },
              "is_select_ep": true,
              "ep_links": ["https://mikanani.me/Home/Episode/b"]
            }
            """;

        using var response = await app.Client.PostAsync(
            "/api/rss", new StringContent(request, Encoding.UTF8, "application/json"));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(200, json.RootElement.GetProperty("code").GetInt32());
        Assert.Equal("开始处理1个下载项", json.RootElement.GetProperty("msg").GetString());
        var data = json.RootElement.GetProperty("data");
        Assert.Equal(3951, data.GetProperty("mikanid").GetInt32());
        Assert.Equal("staged", data.GetProperty("items")[0].GetProperty("status").GetString());
        Assert.Single(transport.Requests);
    }

    [Fact]
    public async Task RedirectToUnlistedHostReturnsLegacyFailureWithoutFollowingIt()
    {
        var transport = new StaticTransport(_ => new TorrentHttpResponse(
            HttpStatusCode.Redirect,
            new Uri("http://127.0.0.1/private"),
            0,
            Stream.Null));
        await using var app = await RunningApp.StartAsync(
            rssDnsResolver: new PublicDnsResolver(), rssHttpTransport: transport);
        const string request = """
            { "source": "mikan", "rss": { "url": "https://mikanani.me/RSS?bangumiId=3951" } }
            """;

        using var response = await app.Client.PostAsync(
            "/api/rss", new StringContent(request, Encoding.UTF8, "application/json"));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(300, json.RootElement.GetProperty("code").GetInt32());
        Assert.Equal("RSS processing failed: rss_redirect_rejected", json.RootElement.GetProperty("msg").GetString());
        Assert.Single(transport.Requests);
    }

    [Fact]
    public async Task AnimeGoHelperConfigUploadImmediatelyFiltersLegacyRss()
    {
        var transport = new StaticTransport(_ => Response(HttpStatusCode.OK, FeedXml));
        await using var app = await RunningApp.StartAsync(
            rssDnsResolver: new PublicDnsResolver(), rssHttpTransport: transport);
        const string legacyConfig = """
            {
              "Filiter0": {
                "drop-04": {
                  "is_enable_whitelist": false,
                  "whitelist": [],
                  "is_enable_blacklist": true,
                  "blacklist": ["[04]"]
                }
              },
              "Filiter1": {}, "Filiter2": {}, "Filiter3": {}, "Filiter4": {}
            }
            """;
        var upload = JsonSerializer.Serialize(new
        {
            name = "filter/mikan_tool.py",
            data = Convert.ToBase64String(Encoding.UTF8.GetBytes(legacyConfig)),
        });
        using var uploadResponse = await app.Client.PostAsync(
            "/api/plugin/config",
            new StringContent(upload, Encoding.UTF8, "application/json"));
        using var uploadJson = JsonDocument.Parse(await uploadResponse.Content.ReadAsStreamAsync());
        Assert.Equal(200, uploadJson.RootElement.GetProperty("code").GetInt32());
        const string request = """
            { "source": "mikan", "rss": { "url": "https://mikanani.me/RSS?bangumiId=3951" } }
            """;

        using var response = await app.Client.PostAsync(
            "/api/rss", new StringContent(request, Encoding.UTF8, "application/json"));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(200, json.RootElement.GetProperty("code").GetInt32());
        var data = json.RootElement.GetProperty("data");
        Assert.True(data.TryGetProperty("legacy_filter_revision", out var legacyRevision), data.GetRawText());
        Assert.Equal(2, legacyRevision.GetInt64());
        Assert.True(data.GetProperty("legacy_filter_enabled").GetBoolean());
        Assert.Equal("staged", data.GetProperty("items")[0].GetProperty("status").GetString());
        Assert.Equal("blocked", data.GetProperty("items")[1].GetProperty("status").GetString());
        Assert.Equal(
            "RejectedByLegacyFilter",
            data.GetProperty("items")[1].GetProperty("decision_kind").GetString());
        Assert.Equal(
            "RejectedByLegacyMikanTool",
            data.GetProperty("items")[1].GetProperty("legacy_filter_reason").GetString());
        Assert.Equal("Filiter0", data.GetProperty("items")[1].GetProperty("legacy_filter_scope").GetString());
        Assert.Single(transport.Requests);
    }

    private static TorrentHttpResponse Response(HttpStatusCode status, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        return new TorrentHttpResponse(status, null, bytes.Length, new MemoryStream(bytes, writable: false));
    }

    private sealed class PublicDnsResolver : ITorrentDnsResolver
    {
        public ValueTask<IReadOnlyList<IPAddress>> ResolveAsync(
            string host,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<IPAddress>>([IPAddress.Parse("1.1.1.1")]);
    }

    private sealed class StaticTransport(Func<Uri, TorrentHttpResponse> responseFactory) : ITorrentHttpTransport
    {
        public List<Uri> Requests { get; } = [];

        public ValueTask<TorrentHttpResponse> SendAsync(
            Uri uri,
            IReadOnlyList<IPAddress> validatedAddresses,
            CancellationToken cancellationToken)
        {
            Requests.Add(uri);
            return ValueTask.FromResult(responseFactory(uri));
        }
    }
}
