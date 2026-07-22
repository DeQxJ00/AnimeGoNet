using System.Text;
using AnimeGoNet.Core.Rules;

namespace AnimeGoNet.Core.Tests.Rules;

public sealed class LegacyMikanFilterCodecTests
{
    [Fact]
    public void RoundTripPreservesTierOrderEmptyDuplicateAndCaseSensitiveValues()
    {
        const string json = """
            {
              "Filiter0": {
                "first": {"is_enable_whitelist":true,"whitelist":["","CHS","CHS"],"is_enable_blacklist":false,"blacklist":[]},
                "second": {"is_enable_whitelist":false,"whitelist":[],"is_enable_blacklist":true,"blacklist":["720P","720p"]}
              },
              "Filiter1": {"key_3951_370":{"is_enable_whitelist":false,"whitelist":[],"is_enable_blacklist":false,"blacklist":[]}},
              "Filiter2": {}, "Filiter3": {}, "Filiter4": {}
            }
            """;
        var config = LegacyMikanFilterCodec.Parse(Encoding.UTF8.GetBytes(json));
        var roundTrip = LegacyMikanFilterCodec.Parse(LegacyMikanFilterCodec.Encode(config));

        Assert.Equal(["first", "second"], roundTrip.Filiter0.Select(pair => pair.Key));
        Assert.Equal(["", "CHS", "CHS"], roundTrip.Filiter0[0].Value.Whitelist);
        Assert.Equal(["720P", "720p"], roundTrip.Filiter0[1].Value.Blacklist);
        Assert.True(roundTrip.Filiter1.ContainsKey("key_3951_370"));
    }

    [Fact]
    public void MissingTiersAndRuleFieldsUseUpstreamEmptyDefaults()
    {
        var config = LegacyMikanFilterCodec.Parse(Encoding.UTF8.GetBytes("{\"Filiter0\":{\"x\":{}}}"));
        var rule = Assert.Single(config.Filiter0).Value;
        Assert.False(rule.IsEnableWhitelist);
        Assert.False(rule.IsEnableBlacklist);
        Assert.Empty(rule.Whitelist);
        Assert.Empty(config.Filiter4);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"Filiter0\":[]}")]
    [InlineData("{\"Filiter0\":{\"x\":{\"whitelist\":true}}}")]
    [InlineData("{\"Filiter0\":{\"x\":{\"blacklist\":[1]}}}")]
    public void RejectsStructurallyInvalidLegacyJson(string json) =>
        Assert.Throws<FormatException>(() => LegacyMikanFilterCodec.Parse(Encoding.UTF8.GetBytes(json)));
}
