using System.Text;
using AnimeGoNet.Core.Feeds;

namespace AnimeGoNet.Core.Tests.Feeds;

public sealed class RssFeedParserTests
{
    private const string Sample = """
        <?xml version="1.0" encoding="utf-8"?>
        <rss version="2.0">
          <channel>
            <title>Mikan</title>
            <link>https://mikanani.me/RSS/Bangumi?bangumiId=3951&amp;subgroupid=370</link>
            <item><title>missing enclosure</title></item>
            <item>
              <link>https://mikanani.me/Home/Episode/hash</link>
              <title>[Group] Show 03 1080P</title>
              <torrent xmlns="https://mikanani.me/0.1/">
                <pubDate>2026-07-22T12:34:56.123</pubDate>
              </torrent>
              <enclosure type="application/x-bittorrent" length="invalid"
                         url="https://mikanani.me/Download/token/show.torrent" />
            </item>
          </channel>
        </rss>
        """;

    [Fact]
    public void ParsesUpstreamFieldsSkipsMissingEnclosureAndDefaultsInvalidLength()
    {
        var feed = RssFeedParser.Parse(Encoding.UTF8.GetBytes(Sample));

        Assert.Equal(3951, feed.MikanId);
        var item = Assert.Single(feed.Items);
        Assert.Equal("[Group] Show 03 1080P", item.Title);
        Assert.Equal("https://mikanani.me/Home/Episode/hash", item.MikanUrl);
        Assert.Equal("https://mikanani.me/Download/token/show.torrent", item.TorrentUrl);
        Assert.Equal("application/x-bittorrent", item.ContentType);
        Assert.Equal(0, item.Length);
        Assert.Equal("2026-07-22T12:34:56.123", item.PublishedDate);
    }

    [Theory]
    [InlineData("https://mikanani.me/Home/Bangumi/3951", 3951)]
    [InlineData("https://mikanani.me/rss?BANGUMIID=228", 228)]
    [InlineData("https://mikanani.me/Home/Episode/hash", null)]
    [InlineData("https://example.com/Home/Bangumi/0", null)]
    public void ParsesOnlyPositiveMikanWorkIds(string value, int? expected)
    {
        Assert.Equal(expected, MikanIdentityParser.TryParseMikanId(value));
    }

    [Fact]
    public void ExplicitSourceUrlMikanIdOverridesChannelLink()
    {
        var feed = RssFeedParser.Parse(
            Encoding.UTF8.GetBytes(Sample),
            "https://mikanani.me/RSS/Bangumi?bangumiId=8849");
        Assert.Equal(8849, feed.MikanId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not xml")]
    [InlineData("<rss><channel>")]
    [InlineData("<feed></feed>")]
    public void RejectsEmptyMalformedAndNonRssContent(string raw)
    {
        var exception = Assert.Throws<RssFeedException>(() =>
            RssFeedParser.Parse(Encoding.UTF8.GetBytes(raw)));
        Assert.True(exception.Code is "rss_empty" or "rss_parse_failed");
    }

    [Fact]
    public void ProhibitsDtdAndExternalEntities()
    {
        const string raw = "<!DOCTYPE rss [<!ENTITY xxe SYSTEM 'file:///secret'>]><rss><channel><item><title>&xxe;</title></item></channel></rss>";
        var exception = Assert.Throws<RssFeedException>(() =>
            RssFeedParser.Parse(Encoding.UTF8.GetBytes(raw)));
        Assert.Equal("rss_parse_failed", exception.Code);
    }
}
