using System.Text;
using AnimeGoNet.App.Feeds;
using AnimeGoNet.Core.Feeds;

namespace AnimeGoNet.App.Tests.Feeds;

public sealed class RssFeedReaderTests : IDisposable
{
    private const string Sample = """
        <rss><channel><item><title>Show</title><link>https://mikanani.me/Home/Episode/hash</link>
        <enclosure type="application/x-bittorrent" length="42" url="https://mikanani.me/show.torrent" />
        </item></channel></rss>
        """;
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "animegonet-rss-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ParsesFileAndMapsOpenFailureToStableCode()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "feed.xml");
        await File.WriteAllTextAsync(path, Sample);
        var reader = new RssFeedReader(new FakeHttpClient(Encoding.UTF8.GetBytes(Sample)));

        Assert.Equal(42, Assert.Single((await reader.ParseFileAsync(path)).Items).Length);
        var exception = await Assert.ThrowsAsync<RssFeedException>(() =>
            reader.ParseFileAsync(Path.Combine(_root, "missing.xml")));
        Assert.Equal("rss_file_open_failed", exception.Code);
    }

    [Fact]
    public async Task ParsesInjectedUrlContentAndPreservesSourceMikanId()
    {
        var client = new FakeHttpClient(Encoding.UTF8.GetBytes(Sample));
        var feed = await new RssFeedReader(client).ParseUrlAsync(
            "https://mikanani.me/RSS/Bangumi?bangumiId=3951");

        Assert.Equal(3951, feed.MikanId);
        Assert.Equal("https://mikanani.me/RSS/Bangumi?bangumiId=3951", client.Requested?.AbsoluteUri);
    }

    [Theory]
    [InlineData("")]
    [InlineData("file:///tmp/feed.xml")]
    [InlineData("not-a-url")]
    public async Task RejectsInvalidNetworkUrls(string value)
    {
        var exception = await Assert.ThrowsAsync<RssFeedException>(() =>
            new RssFeedReader(new FakeHttpClient(ReadOnlyMemory<byte>.Empty)).ParseUrlAsync(value));
        Assert.Equal("rss_url_invalid", exception.Code);
    }

    [Fact]
    public async Task MapsHttpFailureWithoutLeakingUrl()
    {
        var exception = await Assert.ThrowsAsync<RssFeedException>(() =>
            new RssFeedReader(new FailingHttpClient()).ParseUrlAsync("https://example.com/private-token"));
        Assert.Equal("rss_request_failed", exception.Code);
        Assert.DoesNotContain("private-token", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HttpClientRejectsDeclaredOversizedResponse()
    {
        using var content = new ByteArrayContent(Array.Empty<byte>());
        content.Headers.ContentLength = RssFeedParser.MaximumBytes + 1L;
        using var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = content,
        };
        using var client = new HttpClient(new StaticResponseHandler(response));

        var exception = await Assert.ThrowsAsync<RssFeedException>(() =>
            new RssFeedHttpClient(client).GetAsync(new Uri("https://example.com/feed.xml")));

        Assert.Equal("rss_too_large", exception.Code);
    }

    [Fact]
    public async Task HttpClientRejectsStreamedOversizedResponseWithoutContentLength()
    {
        using var content = new StreamContent(
            new MemoryStream(new byte[RssFeedParser.MaximumBytes + 1], writable: false));
        content.Headers.ContentLength = null;
        using var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = content,
        };
        using var client = new HttpClient(new StaticResponseHandler(response));

        var exception = await Assert.ThrowsAsync<RssFeedException>(() =>
            new RssFeedHttpClient(client).GetAsync(new Uri("https://example.com/feed.xml")));

        Assert.Equal("rss_too_large", exception.Code);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class FakeHttpClient(ReadOnlyMemory<byte> response) : IRssFeedHttpClient
    {
        public Uri? Requested { get; private set; }
        public Task<ReadOnlyMemory<byte>> GetAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            Requested = uri;
            return Task.FromResult(response);
        }
    }

    private sealed class FailingHttpClient : IRssFeedHttpClient
    {
        public Task<ReadOnlyMemory<byte>> GetAsync(Uri uri, CancellationToken cancellationToken = default) =>
            Task.FromException<ReadOnlyMemory<byte>>(new HttpRequestException("secret transport detail"));
    }

    private sealed class StaticResponseHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(response);
    }
}
