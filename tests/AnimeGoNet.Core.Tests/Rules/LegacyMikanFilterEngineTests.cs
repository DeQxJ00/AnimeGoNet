using AnimeGoNet.Core.Rules;

namespace AnimeGoNet.Core.Tests.Rules;

public sealed class LegacyMikanFilterEngineTests
{
    [Theory]
    [InlineData(true, false, "CHS", "", "Show CHS", true)]
    [InlineData(true, false, "CHS", "", "Show chs", false)]
    [InlineData(false, true, "", "720p", "Show 720p", false)]
    [InlineData(false, true, "", "720p", "Show 1080p", true)]
    [InlineData(true, true, "CHS", "720p", "Show CHS 1080p", true)]
    [InlineData(true, true, "CHS", "720p", "Show CHS 720p", false)]
    [InlineData(false, false, "", "", "Show", true)]
    public void PreservesWhitelistBlacklistAndCaseSensitiveSubstringSemantics(
        bool whitelistEnabled,
        bool blacklistEnabled,
        string whitelist,
        string blacklist,
        string title,
        bool expected)
    {
        var rule = Rule(whitelistEnabled, blacklistEnabled, Split(whitelist), Split(blacklist));
        var result = LegacyMikanFilterEngine.Evaluate(
            new LegacyMikanFilterCandidate(title, null, null, string.Empty),
            Config(f0: [new("global", rule)]));
        Assert.Equal(expected, result.Accepted);
    }

    [Fact]
    public void MultipleFiliter0EntriesUseLastConfiguredResultInsteadOfAnd()
    {
        var result = LegacyMikanFilterEngine.Evaluate(
            new LegacyMikanFilterCandidate("Show 720p", null, null, string.Empty),
            Config(f0:
            [
                new("first", Rule(false, true, [], ["720p"])),
                new("second", Rule(true, false, ["Show"], [])),
            ]));
        Assert.True(result.Accepted);
        Assert.Equal("second", result.MatchedKey);
    }

    [Fact]
    public void CombinedWorkGroupRulePrecedesWorkThenGroup()
    {
        var candidate = new LegacyMikanFilterCandidate("Show CHS", 3951, 370, "Group");
        var config = Config(
            f1: new Dictionary<string, LegacyMikanFilterRule> { ["key_3951_370"] = Rule(true, false, ["CHS"], []) },
            f2: new Dictionary<string, LegacyMikanFilterRule> { ["3951"] = Rule(false, true, [], ["CHS"]) },
            f3: new Dictionary<string, LegacyMikanFilterRule> { ["370"] = Rule(false, true, [], ["CHS"]) });
        var result = LegacyMikanFilterEngine.Evaluate(candidate, config);
        Assert.True(result.Accepted);
        Assert.Equal("Filiter1", result.MatchedScope);
    }

    [Fact]
    public void MissingMikanIdentityRejectsWhenAnyScopedConfigExists()
    {
        var result = LegacyMikanFilterEngine.Evaluate(
            new LegacyMikanFilterCandidate("Show", null, null, "Group"),
            Config(f2: new Dictionary<string, LegacyMikanFilterRule> { ["3951"] = Rule(false, false, [], []) }));
        Assert.False(result.Accepted);
        Assert.Equal("MikanIdentityRequired", result.Reason);
    }

    [Fact]
    public void PreviewExplainsTierPrecedenceAndCaseSensitiveMatches()
    {
        var preview = LegacyMikanFilterEngine.Preview(
            new LegacyMikanFilterCandidate("Show CHS 1080P", 3951, 370, "Group"),
            Config(
                f0:
                [
                    new("global", Rule(true, false, ["1080P"], [])),
                ],
                f1: new Dictionary<string, LegacyMikanFilterRule>
                {
                    ["key_3951_370"] = Rule(true, true, ["CHS", "chs"], ["合集"]),
                },
                f2: new Dictionary<string, LegacyMikanFilterRule>
                {
                    ["3951"] = Rule(false, true, [], ["CHS"]),
                }));

        Assert.True(preview.Result.Accepted);
        Assert.Collection(
            preview.Steps,
            step =>
            {
                Assert.Equal("Filiter0", step.Tier);
                Assert.Equal(["1080P"], step.WhitelistMatches);
            },
            step =>
            {
                Assert.Equal("Filiter1", step.Tier);
                Assert.Equal(["CHS"], step.WhitelistMatches);
                Assert.Empty(step.BlacklistMatches);
            },
            step =>
            {
                Assert.Equal("Filiter2", step.Tier);
                Assert.False(step.Applicable);
                Assert.Equal("HigherTierMatched", step.Reason);
            },
            step => Assert.Equal("Filiter3", step.Tier),
            step =>
            {
                Assert.Equal("Filiter4", step.Tier);
                Assert.Equal("NoMatchingRule", step.Reason);
            });
    }

    [Theory]
    [InlineData("[LoliHouse] Show - 03", "LoliHouse")]
    [InlineData("【字幕组】Show - 03", "字幕组")]
    [InlineData("Show - 03", "")]
    public void ParsesFirstBracketAsUpstreamGroupName(string title, string expected) =>
        Assert.Equal(expected, LegacyMikanFilterEngine.ParseGroupName(title));

    private static LegacyMikanFilterRule Rule(
        bool whitelistEnabled,
        bool blacklistEnabled,
        IReadOnlyList<string> whitelist,
        IReadOnlyList<string> blacklist) =>
        new(whitelistEnabled, blacklistEnabled, whitelist, blacklist);

    private static LegacyMikanFilterConfig Config(
        IReadOnlyList<KeyValuePair<string, LegacyMikanFilterRule>>? f0 = null,
        IReadOnlyDictionary<string, LegacyMikanFilterRule>? f1 = null,
        IReadOnlyDictionary<string, LegacyMikanFilterRule>? f2 = null,
        IReadOnlyDictionary<string, LegacyMikanFilterRule>? f3 = null,
        IReadOnlyDictionary<string, LegacyMikanFilterRule>? f4 = null) =>
        new(f0 ?? [], f1 ?? Empty(), f2 ?? Empty(), f3 ?? Empty(), f4 ?? Empty());

    private static Dictionary<string, LegacyMikanFilterRule> Empty() =>
        new Dictionary<string, LegacyMikanFilterRule>();

    private static string[] Split(string value) => value.Length == 0 ? [] : [value];
}
