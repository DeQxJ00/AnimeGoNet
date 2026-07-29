using System.Text;
using AnimeGoNet.Core.Feeds;

namespace AnimeGoNet.Core.Tests.Feeds;

public sealed class MikanBangumiSubjectParserTests
{
    [Theory]
    [InlineData("https://bgm.tv/subject/547888", 547888)]
    [InlineData("https://www.bangumi.tv/subject/42?from=mikan#details", 42)]
    [InlineData("http://www.bgm.tv/SUBJECT/7/", 7)]
    public void ParsesSubjectLinkInsideBangumiInfo(string href, int expected)
    {
        var html = $$"""
            <html><body>
              <p data-x="1" class='muted bangumi-info other'>
                Bangumi：<a title="作品" href="{{href}}">查看</a>
              </p>
            </body></html>
            """;

        Assert.Equal(expected, MikanBangumiSubjectParser.Parse(Encoding.UTF8.GetBytes(html)));
    }

    [Fact]
    public void DecodesHtmlAttributeAndAllowsDuplicateSameSubject()
    {
        const string html = """
            <p class="bangumi-info"><a href="https://bgm.tv/subject/547888?x=1&amp;y=2">A</a></p>
            <p class="other bangumi-info"><a href=https://bangumi.tv/subject/547888>B</a></p>
            """;

        Assert.Equal(547888, MikanBangumiSubjectParser.Parse(Encoding.UTF8.GetBytes(html)));
    }

    [Theory]
    [InlineData("""<a href="https://bgm.tv/subject/1">outside</a>""")]
    [InlineData("""<p class="bangumi-info"><a href="https://bgm.tv.evil.test/subject/1">spoof</a></p>""")]
    [InlineData("""<p class="bangumi-info"><a href="https://bgm.tv/person/1">person</a></p>""")]
    [InlineData("""<p class="bangumi-info"><a href="https://bgm.tv/subject/0">zero</a></p>""")]
    [InlineData("""<p class="bangumi-information"><a href="https://bgm.tv/subject/1">wrong class</a></p>""")]
    public void RejectsUntrustedOrOutOfScopeLinks(string html)
    {
        var exception = Assert.Throws<MikanBangumiSubjectException>(
            () => MikanBangumiSubjectParser.Parse(Encoding.UTF8.GetBytes(html)));

        Assert.Equal("mikan_bgmid_link_missing", exception.Code);
    }

    [Fact]
    public void RejectsConflictingSubjectLinks()
    {
        const string html = """
            <p class="bangumi-info">
              <a href="https://bgm.tv/subject/1">A</a>
              <a href="https://bangumi.tv/subject/2">B</a>
            </p>
            """;

        var exception = Assert.Throws<MikanBangumiSubjectException>(
            () => MikanBangumiSubjectParser.Parse(Encoding.UTF8.GetBytes(html)));

        Assert.Equal("mikan_bgmid_link_ambiguous", exception.Code);
    }

    [Fact]
    public void RejectsInvalidPayloadsWithStableCodes()
    {
        Assert.Equal(
            "mikan_bgmid_html_empty",
            Assert.Throws<MikanBangumiSubjectException>(
                () => MikanBangumiSubjectParser.Parse(ReadOnlyMemory<byte>.Empty)).Code);
        Assert.Equal(
            "mikan_bgmid_html_too_large",
            Assert.Throws<MikanBangumiSubjectException>(
                () => MikanBangumiSubjectParser.Parse(
                    new byte[MikanBangumiSubjectParser.MaximumBytes + 1])).Code);
        Assert.Equal(
            "mikan_bgmid_html_invalid",
            Assert.Throws<MikanBangumiSubjectException>(
                () => MikanBangumiSubjectParser.Parse(new byte[] { 0xC3, 0x28 })).Code);
    }
}
