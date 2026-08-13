using System.Text;
using AnimeGoNet.App.Feeds;
using AnimeGoNet.Core.Feeds;

namespace AnimeGoNet.App.Tests.Feeds;

public sealed class MikanFeedIdentityResolverTests
{
    [Fact]
    public async Task ResolvesEveryAggregateItemAndCachesRepeatedEpisodePages()
    {
        var http = new FakeHttpClient(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["https://mikanani.me/Home/Episode/shared"] =
                "<a class='mikan-rss' href='/RSS/Bangumi?bangumiId=3951&amp;subgroupid=370'>RSS</a>",
            ["https://mikanani.me/Home/Episode/second"] =
                "<a class='mikan-rss' href='/RSS/Bangumi?bangumiId=4028&amp;subgroupid=44'>RSS</a>",
        });
        var feed = new RssFeedDocument(
            [Item("shared"), Item("shared"), Item("second")],
            null);

        var result = await new MikanFeedIdentityResolver(http).ResolveAsync(feed, "mikan");

        Assert.Equal(3, result.Count);
        Assert.Equal([3951, 3951, 4028], result.Select(item => item.Identity?.MikanId).ToArray());
        Assert.Equal([370, 370, 44], result.Select(item => item.Identity?.SubGroupId).ToArray());
        Assert.All(result, item => Assert.Null(item.FailureCode));
        Assert.Equal(2, http.Requests.Count);
    }

    [Fact]
    public async Task KeepsIdentityParseFailureScopedToItsItem()
    {
        var http = new FakeHttpClient(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["https://mikanani.me/Home/Episode/missing"] = "<html>no RSS identity</html>",
            ["https://mikanani.me/Home/Episode/good"] =
                "<a class='mikan-rss' href='/RSS/Bangumi?bangumiId=3951&amp;subgroupid=370'>RSS</a>",
        });
        var feed = new RssFeedDocument([Item("missing"), Item("good")], null);

        var result = await new MikanFeedIdentityResolver(http).ResolveAsync(feed, "mikan");

        Assert.Null(result[0].Identity);
        Assert.Equal("mikan_identity_link_missing", result[0].FailureCode);
        Assert.Equal(3951, result[1].Identity?.MikanId);
        Assert.Null(result[1].FailureCode);
    }

    private static RssFeedItem Item(string id) => new(
        $"Show {id} [03]",
        $"https://mikanani.me/Home/Episode/{id}",
        $"https://mikanani.me/Download/{id}.torrent",
        "application/x-bittorrent",
        42,
        "2026-08-13");

    private sealed class FakeHttpClient(IReadOnlyDictionary<string, string> responses) : IRssFeedHttpClient
    {
        public List<Uri> Requests { get; } = [];

        public Task<ReadOnlyMemory<byte>> GetAsync(
            Uri uri,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            Requests.Add(uri);
            return Task.FromResult<ReadOnlyMemory<byte>>(
                Encoding.UTF8.GetBytes(responses[uri.AbsoluteUri]));
        }
    }
}
