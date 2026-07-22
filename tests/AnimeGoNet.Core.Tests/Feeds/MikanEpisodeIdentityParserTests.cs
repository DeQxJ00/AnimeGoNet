using System.Text;
using AnimeGoNet.Core.Feeds;

namespace AnimeGoNet.Core.Tests.Feeds;

public sealed class MikanEpisodeIdentityParserTests
{
    [Fact]
    public void ParsesUpstreamEpisodeIdentityFixture()
    {
        const string html = """
            <p class="bangumi-title">
              <a href="/Home/Bangumi/2822#370">想要成为影之实力者！</a>
              <a href="/RSS/Bangumi?bangumiId=2822&amp;subgroupid=370" class="mikan-rss" target="_blank">RSS</a>
            </p>
            """;

        var identity = MikanEpisodeIdentityParser.Parse(Encoding.UTF8.GetBytes(html));

        Assert.Equal(2822, identity.MikanId);
        Assert.Equal(370, identity.SubGroupId);
    }

    [Fact]
    public void HandlesAttributeOrderQuotesAndClassTokens()
    {
        const string html = """
            <A data-x='>' disabled CLASS='button mikan-rss active' HREF='/RSS/Bangumi?subgroupid=583&amp;bangumiId=2775'>RSS</A>
            """;

        var identity = MikanEpisodeIdentityParser.Parse(Encoding.UTF8.GetBytes(html));

        Assert.Equal(new MikanEpisodeIdentity(2775, 583), identity);
    }

    [Fact]
    public void MissingOrInvalidSubgroupMatchesUpstreamZeroFallback()
    {
        var missing = Parse("<a class='mikan-rss' href='/RSS/Bangumi?bangumiId=3015'>x</a>");
        var invalid = Parse("<a href='/RSS/Bangumi?bangumiId=3015&amp;subgroupid=bad' class='mikan-rss'>x</a>");

        Assert.Equal(0, missing.SubGroupId);
        Assert.Equal(0, invalid.SubGroupId);
    }

    [Theory]
    [InlineData("<a class='mikan-rss' href='/RSS/Bangumi?subgroupid=370'>x</a>", "mikan_identity_id_invalid")]
    [InlineData("<a class='mikan-rss' href='/RSS/Bangumi?bangumiId=bad&amp;subgroupid=370'>x</a>", "mikan_identity_id_invalid")]
    [InlineData("<a class='other' href='/RSS/Bangumi?bangumiId=2822&amp;subgroupid=370'>x</a>", "mikan_identity_link_missing")]
    [InlineData("<a class='mikan-rss'>x</a>", "mikan_identity_href_missing")]
    [InlineData("<a class='mikan-rss' href='/RSS/Bangumi?bangumiId=2822'", "mikan_identity_html_invalid")]
    public void ReportsStableFailureCodes(string html, string expectedCode)
    {
        var exception = Assert.Throws<MikanEpisodeIdentityException>(() => Parse(html));

        Assert.Equal(expectedCode, exception.Code);
    }

    [Fact]
    public void RejectsEmptyOversizedAndInvalidUtf8Input()
    {
        Assert.Equal(
            "mikan_identity_html_empty",
            Assert.Throws<MikanEpisodeIdentityException>(
                () => MikanEpisodeIdentityParser.Parse(ReadOnlyMemory<byte>.Empty)).Code);
        Assert.Equal(
            "mikan_identity_html_too_large",
            Assert.Throws<MikanEpisodeIdentityException>(
                () => MikanEpisodeIdentityParser.Parse(new byte[MikanEpisodeIdentityParser.MaximumBytes + 1])).Code);
        Assert.Equal(
            "mikan_identity_html_invalid",
            Assert.Throws<MikanEpisodeIdentityException>(
                () => MikanEpisodeIdentityParser.Parse(new byte[] { 0xC3, 0x28 })).Code);
    }

    [Fact]
    public void DoesNotConfusePublishGroupWithRssSubgroup()
    {
        const string html = """
            <a href="/RSS/Bangumi?bangumiId=228&amp;subgroupid=1" class="mikan-rss">RSS</a>
            <p class="bangumi-info">字幕组：<a class="magnet-link-wrap" href="/Home/PublishGroup/99">OPFans</a></p>
            """;

        var identity = MikanEpisodeIdentityParser.Parse(Encoding.UTF8.GetBytes(html));

        Assert.Equal(1, identity.SubGroupId);
    }

    private static MikanEpisodeIdentity Parse(string html) =>
        MikanEpisodeIdentityParser.Parse(Encoding.UTF8.GetBytes(html));
}
