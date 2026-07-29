using System.Text.Json;
using AnimeGoNet.Core.Metadata;

namespace AnimeGoNet.Core.Tests.Metadata;

public sealed class AutoBangumiRawParserTests
{
    [Fact]
    public void MatchesDevelopBranchPythonGoldenFixture()
    {
        var fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "auto-bangumi-raw-parser.json");
        using var document = JsonDocument.Parse(File.ReadAllText(fixturePath));

        foreach (var fixture in document.RootElement.EnumerateArray())
        {
            var input = fixture.GetProperty("input").GetString()!;
            var result = AutoBangumiRawParser.Parse(input);

            Assert.Equal(fixture.GetProperty("title_en").GetString(), result.EnglishTitle);
            Assert.Equal(fixture.GetProperty("title_zh").GetString(), result.ChineseTitle);
            Assert.Equal(fixture.GetProperty("title_jp").GetString(), result.JapaneseTitle);
            Assert.Equal(fixture.GetProperty("season").GetInt32(), result.Season);
            Assert.Equal(fixture.GetProperty("season_raw").GetString(), result.SeasonRaw);
            Assert.Equal(fixture.GetProperty("episode").GetInt32(), result.Episode);
            Assert.Equal(fixture.GetProperty("sub").GetString(), result.Subtitle);
            Assert.Equal(fixture.GetProperty("group").GetString(), result.Group);
            Assert.Equal(fixture.GetProperty("resolution").GetString(), result.Resolution);
            Assert.Equal(fixture.GetProperty("source").GetString(), result.Source);
        }
    }

    [Theory]
    [InlineData("[Group] Show [04][1080p]", 4)]
    [InlineData("[Group] Show -  7 [720P]", 7)]
    [InlineData("[Group] Show [12 END][4K]", 12)]
    public void CandidatePolicyAcceptsOneUnambiguousUpstreamInteger(string path, int expected)
    {
        var result = FileEpisodeCandidateResolver.Resolve("mikan", path);

        Assert.True(result.IsCandidate);
        Assert.Equal(expected, result.Episode);
        Assert.Equal("accepted", result.Reason);
    }

    [Theory]
    [InlineData("u2", "[Group] Show [04][1080p]", "source_not_mikan")]
    [InlineData("mikan", "Show [2024][1080p]", "year_like_episode")]
    [InlineData("mikan", "[Group] Show [03][1080][Baha]", "resolution_like_episode")]
    [InlineData("mikan", "[Group] Show [01][02][1080p]", "ambiguous_episode_markers")]
    [InlineData("mikan", "[Group] Show [SP01][1080p]", "upstream_episode_not_parsed")]
    [InlineData("mikan", "[Group] Show [48.5][1080p]", "upstream_episode_not_parsed")]
    [InlineData("mikan", "[Group] Show Menu [01][1080p]", "non_feature_episode")]
    [InlineData("mikan", "[Group] Show EP04 [1080p]", "upstream_episode_not_parsed")]
    [InlineData("mikan", "[Group(] Show [01]", "compatibility_parser_failed")]
    public void CandidatePolicyKeepsCompatibilityQuirksOutOfPersistedCandidate(
        string adapter,
        string path,
        string reason)
    {
        var result = FileEpisodeCandidateResolver.Resolve(adapter, path);

        Assert.False(result.IsCandidate);
        Assert.Null(result.Episode);
        Assert.Equal(reason, result.Reason);
    }
}
