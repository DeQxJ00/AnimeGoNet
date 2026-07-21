using AnimeGoNet.Core.Rules;

namespace AnimeGoNet.Core.Tests.Rules;

public sealed class MikanRssRuleSetNormalizerTests
{
    [Fact]
    public void NormalizesIdsAndValuesWhilePreservingNamesAndOrder()
    {
        var normalized = MikanRssRuleSetNormalizer.Normalize(new MikanRssRuleSet(
            [new NamedMatchArray(" LANG ", " 简体优先 ", true, [" HEVC ", "hevc", " 简体 "])],
            [],
            [new PriorityGroup(
                " CODEC ", " 编码 ",
                [new NamedMatchArray(" H265 ", "H.265", true, [" H265 ", " HEVC "])])]
        ));

        var whitelist = Assert.Single(normalized.Whitelist);
        Assert.Equal("lang", whitelist.Id);
        Assert.Equal("简体优先", whitelist.Name);
        Assert.Equal(["hevc", "简体"], whitelist.Values);
        var group = Assert.Single(normalized.PriorityGroups);
        Assert.Equal("codec", group.Id);
        Assert.Equal(["h265", "hevc"], Assert.Single(group.Arrays).Values);
    }

    [Fact]
    public void RejectsDuplicateArrayIdsAcrossAllScopes()
    {
        var rules = new MikanRssRuleSet(
            [new NamedMatchArray("same", "one", true, ["a"])],
            [new NamedMatchArray("same", "two", true, ["b"])],
            []);

        Assert.Throws<ArgumentException>(() => MikanRssRuleSetNormalizer.Normalize(rules));
    }

    [Theory]
    [InlineData("")]
    [InlineData("contains space")]
    [InlineData("中文-id")]
    public void RejectsUnstableIds(string id)
    {
        var rules = new MikanRssRuleSet(
            [new NamedMatchArray(id, "name", true, ["value"])], [], []);
        Assert.Throws<ArgumentException>(() => MikanRssRuleSetNormalizer.Normalize(rules));
    }
}
