using System.Net;
using System.Text;
using System.Text.Json;
using AnimeGo.Plugin.Abstractions;
using AnimeGoNet.App.Torrents;
using AnimeGoNet.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeGoNet.App.Tests.Feeds;

public sealed class ProfileBoundRssCredentialTests
{
    [Fact]
    public async Task CustomMikanProfileUsesItsCookieOnlyOnOriginalHost()
    {
        const string secret = "rss-private-cookie";
        var transport = new CredentialRedirectTransport();
        await using var app = await RunningApp.StartAsync(
            configure: WithMikanTestOrigin,
            rssDnsResolver: new PublicDnsResolver(),
            rssHttpTransport: transport);
        using var create = await app.Client.PostAsync(
            "/api/v1/sources",
            Json(new
            {
                id = "mikan-private",
                display_name = "Mikan Private",
                adapter = "mikan",
                downloader_id = "bt",
                file_strategy = "move",
                allowed_torrent_hosts = new List<string>
                {
                    "mikan.example",
                    "cdn.example",
                },
                enabled = true,
                mikan_identity_cookie = secret,
            }));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var plugin = app.App.Services
            .GetRequiredService<PluginCatalog>()
            .Require<IFeedPlugin>("mikan-rss");

        var result = await plugin.FetchAsync(
            new FeedContext(
                "mikan-private",
                "https://mikan.example/RSS",
                new Dictionary<string, string>()),
            CancellationToken.None);

        Assert.Empty(result.Errors);
        Assert.Single(result.Items);
        Assert.Collection(
            transport.Requests,
            first =>
            {
                Assert.Equal("mikan.example", first.Host);
                Assert.True(first.CredentialsConfigured);
            },
            second =>
            {
                Assert.Equal("cdn.example", second.Host);
                Assert.False(second.CredentialsConfigured);
            });
    }

    private static StringContent Json(object value) =>
        new(
            JsonSerializer.Serialize(value),
            Encoding.UTF8,
            "application/json");

    private static AnimeGoOptions WithMikanTestOrigin(AnimeGoOptions options) =>
        options with
        {
            Metadata = options.Metadata with
            {
                Mikan = new MikanClientOptions
                {
                    BaseUrl = new Uri("https://mikan.example/"),
                },
            },
        };

    private sealed class PublicDnsResolver : ITorrentDnsResolver
    {
        public ValueTask<IReadOnlyList<IPAddress>> ResolveAsync(
            string host,
            CancellationToken cancellationToken)
        {
            _ = host;
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<IReadOnlyList<IPAddress>>(
                [IPAddress.Parse("1.1.1.1")]);
        }
    }

    private sealed class CredentialRedirectTransport : ITorrentHttpTransport
    {
        private static readonly byte[] Feed = Encoding.UTF8.GetBytes("""
            <rss><channel><item>
              <title>Private episode [01]</title>
              <link>https://mikan.example/Home/Episode/private-1</link>
              <enclosure
                type="application/x-bittorrent"
                length="5"
                url="https://mikan.example/Home/Episode/private-1.torrent" />
            </item></channel></rss>
            """);

        public List<(string Host, bool CredentialsConfigured)> Requests
        {
            get;
        } = [];

        public ValueTask<TorrentHttpResponse> SendAsync(
            Uri uri,
            IReadOnlyList<IPAddress> validatedAddresses,
            CancellationToken cancellationToken) =>
            SendAsync(
                uri,
                validatedAddresses,
                new TorrentHttpRequestOptions(),
                cancellationToken);

        public ValueTask<TorrentHttpResponse> SendAsync(
            Uri uri,
            IReadOnlyList<IPAddress> validatedAddresses,
            TorrentHttpRequestOptions requestOptions,
            CancellationToken cancellationToken)
        {
            _ = validatedAddresses;
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add((uri.IdnHost, requestOptions.CredentialsConfigured));
            return ValueTask.FromResult(
                uri.IdnHost == "mikan.example"
                    ? new TorrentHttpResponse(
                        HttpStatusCode.Redirect,
                        new Uri("https://cdn.example/RSS"),
                        0,
                        new MemoryStream())
                    : new TorrentHttpResponse(
                        HttpStatusCode.OK,
                        null,
                        Feed.Length,
                        new MemoryStream(Feed, writable: false)));
        }
    }
}
