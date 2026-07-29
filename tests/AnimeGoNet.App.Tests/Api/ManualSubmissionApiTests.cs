using System.Net;
using System.Text;
using System.Text.Json;
using AnimeGoNet.App.Torrents;
using AnimeGoNet.Data.Feeds;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeGoNet.App.Tests.Api;

public sealed class ManualSubmissionApiTests
{
    private static readonly string[] AllowedHosts = ["mikanani.me"];
    private static readonly string[] TestTags = ["animegonet-manual-test"];

    private const string FeedXml = """
        <rss><channel><link>https://mikanani.me/RSS?bangumiId=3951</link>
          <item><title>Manual Show [03] [1080p]</title>
            <link>https://mikanani.me/Home/Episode/manual-03</link>
            <enclosure type="application/x-bittorrent" length="42"
              url="https://mikanani.me/Download/manual-03.torrent" />
          </item>
        </channel></rss>
        """;

    [Fact]
    public async Task ModernRssSubmissionUsesSelectedMikanProfileWithoutEchoingSecretUrl()
    {
        var transport = new StaticTransport(_ => Response(HttpStatusCode.OK, FeedXml));
        await using var app = await RunningApp.StartAsync(
            rssDnsResolver: new PublicDnsResolver(),
            rssHttpTransport: transport);
        await CreateSourceAsync(app, "mikan-alt", "mikan", "pt");
        const string secretUrl =
            "https://mikanani.me/RSS?bangumiId=3951&token=private-passkey";

        using var response = await PostJsonAsync(
            app,
            "/api/v1/rss/ingest",
            new { source_profile_id = "mikan-alt", url = secretUrl });
        var content = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("private-passkey", content, StringComparison.Ordinal);
        Assert.Equal(3951, json.RootElement.GetProperty("mikanid").GetInt32());
        Assert.Equal("staged", json.RootElement.GetProperty("items")[0].GetProperty("status").GetString());
        var batchId = json.RootElement.GetProperty("batch_id").GetString();
        var batch = await app.App.Services
            .GetRequiredService<MikanRssBatchStore>()
            .GetAsync(batchId!);
        Assert.NotNull(batch);
        Assert.Equal("mikan-alt", batch.SourceProfileId);
        Assert.Single(transport.Requests);
        Assert.Equal(secretUrl, transport.Requests[0].AbsoluteUri);
    }

    [Fact]
    public async Task ModernRssSubmissionRejectsNonMikanProfileBeforeFetchingSecretUrl()
    {
        var transport = new StaticTransport(_ => Response(HttpStatusCode.OK, FeedXml));
        await using var app = await RunningApp.StartAsync(
            rssDnsResolver: new PublicDnsResolver(),
            rssHttpTransport: transport);
        await CreateSourceAsync(app, "u2-manual", "u2", "pt");
        const string secretUrl = "https://mikanani.me/RSS?token=must-not-leave";

        using var response = await PostJsonAsync(
            app,
            "/api/v1/rss/ingest",
            new { source_profile_id = "u2-manual", url = secretUrl });
        var content = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "rss_source_profile_invalid",
            json.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("must-not-leave", content, StringComparison.Ordinal);
        Assert.Empty(transport.Requests);
    }

    private static async Task CreateSourceAsync(
        RunningApp app,
        string id,
        string adapter,
        string downloaderId)
    {
        using var response = await PostJsonAsync(
            app,
            "/api/v1/sources",
            new
            {
                id,
                display_name = id,
                adapter,
                downloader_id = downloaderId,
                file_strategy = "link",
                allowed_torrent_hosts = AllowedHosts,
                category = "animegonet-manual-test",
                tags = TestTags,
                seeding_time_minutes = 0,
                rss_filter_enabled = false,
                rss_priority_enabled = true,
                enabled = true,
            });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static Task<HttpResponseMessage> PostJsonAsync(
        RunningApp app,
        string path,
        object value) =>
        app.Client.PostAsync(
            path,
            new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json"));

    private static TorrentHttpResponse Response(HttpStatusCode status, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        return new TorrentHttpResponse(
            status,
            null,
            bytes.Length,
            new MemoryStream(bytes, writable: false));
    }

    private sealed class PublicDnsResolver : ITorrentDnsResolver
    {
        public ValueTask<IReadOnlyList<IPAddress>> ResolveAsync(
            string host,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<IPAddress>>([IPAddress.Parse("1.1.1.1")]);
    }

    private sealed class StaticTransport(
        Func<Uri, TorrentHttpResponse> responseFactory) : ITorrentHttpTransport
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
