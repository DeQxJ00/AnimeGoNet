using System.Text;
using AnimeGoNet.Core.Feeds;

namespace AnimeGoNet.Core.Tests.Feeds;

public sealed class MikanSubgroupListParserTests
{
    [Fact]
    public void ParsesSubgroupNamesAndIdsInPageOrder()
    {
        var html = """
            <div class="leftbar-nav">
              <div class="header">字幕组列表</div>
              <ul class="list-unstyled">
                <li><a class="subgroup-name subgroup-202" data-anchor="#202">生肉/不明字幕</a></li>
                <li><a data-anchor="#583" class="extra subgroup-583 subgroup-name">ANi &amp; Friends</a></li>
              </ul>
            </div>
            """;

        var groups = MikanSubgroupListParser.Parse(Encoding.UTF8.GetBytes(html));

        Assert.Equal(2, groups.Count);
        Assert.Equal(new MikanSubgroup(202, "生肉/不明字幕"), groups[0]);
        Assert.Equal(new MikanSubgroup(583, "ANi & Friends"), groups[1]);
    }

    [Fact]
    public void SupportsEitherTrustedIdAttributeAndDeduplicatesResponsiveMarkup()
    {
        var html = """
            <a class="subgroup-name subgroup-370">LoliHouse</a>
            <a class="subgroup-name" data-anchor="#370">LoliHouse</a>
            <a class="subgroup-name" data-anchor="357"><span>SweetSub</span></a>
            """;

        var groups = MikanSubgroupListParser.Parse(Encoding.UTF8.GetBytes(html));

        Assert.Equal([new(370, "LoliHouse"), new(357, "SweetSub")], groups);
    }

    [Fact]
    public void RejectsConflictingIdsAndMissingList()
    {
        var conflict = Assert.Throws<MikanSubgroupListException>(() =>
            MikanSubgroupListParser.Parse(Encoding.UTF8.GetBytes(
                "<a class='subgroup-name subgroup-1' data-anchor='#2'>Group</a>")));
        var missing = Assert.Throws<MikanSubgroupListException>(() =>
            MikanSubgroupListParser.Parse(Encoding.UTF8.GetBytes(
                "<a class='mikan-rss' href='/RSS/Bangumi?bangumiId=1&amp;subgroupid=2'>RSS</a>")));

        Assert.Equal("mikan_subgroups_id_conflict", conflict.Code);
        Assert.Equal("mikan_subgroups_missing", missing.Code);
    }
}
