using AnimeGoNet.Core.Metadata;

namespace AnimeGoNet.Core.Tests.Metadata;

public sealed class TmdbTitleHeuristicsTests
{
    [Theory]
    [InlineData(0, "测试番剧 10期", "测试番剧")]
    [InlineData(0, "测试番剧第2季", "测试番剧")]
    [InlineData(0, "测试番剧八篇", "测试番剧")]
    [InlineData(1, "测试番剧 2nd Season", "测试番剧")]
    [InlineData(1, "测试番剧10thSeason", "测试番剧")]
    [InlineData(1, "测试番剧Season 3", "测试番剧")]
    [InlineData(2, "魔法使いの嫁 詩篇.75 稲妻ジャックと妖精事件", "魔法使いの嫁")]
    [InlineData(2, "蟲師 特別篇 日蝕む翳", "蟲師")]
    [InlineData(2, "宇宙戦艦ヤマト2199 第二章「太陽圏の死闘」", "宇宙戦艦ヤマト2199")]
    [InlineData(3, "Baldur's Gate II: Shadow of Amn", "Baldur's Gate")]
    [InlineData(3, "提督の決断IV", "提督の決断")]
    [InlineData(3, "カードファイト!! ヴァンガード will+Dress", "カードファイト!! ヴァンガード")]
    [InlineData(3, "オーバーロードIV", "オーバーロード")]
    public void SuffixStepsMatchUpstreamFixtures(int step, string input, string expected)
    {
        Assert.Equal(expected, TmdbTitleHeuristics.ApplySuffixStep(input, step));
    }

    [Theory]
    [InlineData(0, "测试番剧 第2")]
    [InlineData(1, "测试番剧 2dn Season")]
    [InlineData(2, "水浒传之聚义篇")]
    public void NonMatchingStepPreservesTitle(int step, string input)
    {
        Assert.Equal(input, TmdbTitleHeuristics.ApplySuffixStep(input, step));
    }

    [Fact]
    public void SimilarityUsesUpstreamUtf8ByteAlgorithm()
    {
        var baseTitle = "ダンジョンに出会いを求めるのは間違っているだろうか";

        var similarity = TmdbTitleHeuristics.SimilarText(baseTitle, baseTitle + " IV");

        Assert.True(similarity >= TmdbTitleHeuristics.MinimumSimilarity);
        Assert.Equal(100, TmdbTitleHeuristics.SimilarText("same", "same"));
        Assert.Equal(0, TmdbTitleHeuristics.SimilarText(string.Empty, string.Empty));
    }
}
