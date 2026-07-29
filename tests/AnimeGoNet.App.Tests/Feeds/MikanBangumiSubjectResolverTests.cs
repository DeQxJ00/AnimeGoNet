using System.Text;
using AnimeGoNet.App.Feeds;
using AnimeGoNet.Core.Feeds;

namespace AnimeGoNet.App.Tests.Feeds;

public sealed class MikanBangumiSubjectResolverTests
{
    [Fact]
    public async Task FetchesCanonicalWorkPageAndParsesSubject()
    {
        var http = new FakeHttpClient("""
            <p class="bangumi-info">
              <a href="https://bgm.tv/subject/547888">Bangumi</a>
            </p>
            """);
        var resolver = new MikanBangumiSubjectResolver(http);

        var result = await resolver.ResolveAsync(Feed(3951));

        Assert.True(result.IsResolved);
        Assert.Equal(547888, result.BangumiSubjectId);
        Assert.Null(result.FailureCode);
        Assert.Equal(
            new Uri("https://mikanani.me/Home/Bangumi/3951"),
            Assert.Single(http.Requests));
    }

    [Fact]
    public async Task MissingMikanIdAndPageOriginDoNotPerformNetwork()
    {
        var http = new FakeHttpClient("<html></html>");
        var resolver = new MikanBangumiSubjectResolver(http);

        var missingId = await resolver.ResolveAsync(Feed(null));
        var missingOrigin = await resolver.ResolveAsync(new RssFeedDocument(
            [new RssFeedItem("Show", "not-a-url", "https://example.test/a", "x", 1, null)],
            3951));

        Assert.Empty(http.Requests);
        Assert.Equal(MikanBangumiDiscoveryStates.NotApplicable, missingId.State);
        Assert.Equal("mikan_bgmid_mikanid_missing", missingId.FailureCode);
        Assert.Equal(MikanBangumiDiscoveryStates.Failed, missingOrigin.State);
        Assert.Equal("mikan_bgmid_page_origin_missing", missingOrigin.FailureCode);
    }

    [Fact]
    public async Task ClassifiesMissingLinkAndTransportFailureWithoutLeakingMessages()
    {
        var missing = await new MikanBangumiSubjectResolver(
            new FakeHttpClient("<p class=\"bangumi-info\">none</p>")).ResolveAsync(Feed(3951));
        var failed = await new MikanBangumiSubjectResolver(
            new FakeHttpClient(new RssFeedException(
                "rss_request_failed",
                "secret passkey should never be returned"))).ResolveAsync(Feed(3951));

        Assert.Equal(MikanBangumiDiscoveryStates.NotFound, missing.State);
        Assert.Equal("mikan_bgmid_link_missing", missing.FailureCode);
        Assert.Equal(MikanBangumiDiscoveryStates.Failed, failed.State);
        Assert.Equal("rss_request_failed", failed.FailureCode);
    }

    private static RssFeedDocument Feed(int? mikanId) => new(
    [
        new RssFeedItem(
            "Show",
            "https://mikanani.me/Home/Episode/a",
            "https://mikanani.me/Download/a.torrent",
            "application/x-bittorrent",
            1,
            null),
    ], mikanId);

    private sealed class FakeHttpClient : IRssFeedHttpClient
    {
        private readonly ReadOnlyMemory<byte> _response;
        private readonly Exception? _exception;

        public FakeHttpClient(string response) => _response = Encoding.UTF8.GetBytes(response);

        public FakeHttpClient(Exception exception) => _exception = exception;

        public List<Uri> Requests { get; } = [];

        public Task<ReadOnlyMemory<byte>> GetAsync(
            Uri uri,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(uri);
            return _exception is null
                ? Task.FromResult(_response)
                : Task.FromException<ReadOnlyMemory<byte>>(_exception);
        }
    }
}
