using AnimeGoNet.Core.Feeds;
using AnimeGoNet.Core.Metadata;

namespace AnimeGoNet.Core.Tests.Feeds;

public sealed class MikanRssEpisodeParserTests
{
    [Theory]
    [InlineData("[orion origin] Benriya Saitou-san [04] [1080p]", 4)]
    [InlineData("[Group] Show [11v2] [1080p]", 11)]
    [InlineData("[Group] Show - 11 [WebRip 1080p]", 11)]
    [InlineData("[Group] Show 06 [WebRip 1080p]", 6)]
    [InlineData("[Group] Show EP12", 12)]
    [InlineData("[Group] Show E13", 13)]
    [InlineData("[Group] Show 第14話", 14)]
    [InlineData("[Group] Show【15 END】", 15)]
    [InlineData("[梦蓝字幕组]New Doraemon 哆啦A梦新番[716][2022.07.23][AVC][1080P]", 716)]
    public void PreservesUpstreamIntegerEpisodePatterns(string title, int expected)
    {
        var result = MikanRssEpisodeParser.Parse(title);

        Assert.Equal(TorrentEpisodeCandidateKind.Normal, result.Kind);
        Assert.Equal(expected, result.NormalEpisode);
        Assert.Equal(expected.ToString(System.Globalization.CultureInfo.InvariantCulture), result.SourceEpisode);
    }

    [Theory]
    [InlineData("[Group] Show [48.5] [1080p]", "48.5")]
    [InlineData("[Group] Show EP12.25", "12.25")]
    public void FractionalEpisodeNeverBecomesAnIntegerCandidate(string title, string expected)
    {
        var result = MikanRssEpisodeParser.Parse(title);

        Assert.Equal(TorrentEpisodeCandidateKind.Fractional, result.Kind);
        Assert.Equal(expected, result.SourceEpisode);
        Assert.Null(result.NormalEpisode);
    }

    [Theory]
    [InlineData("[Group] Show [SP01] [1080p]")]
    [InlineData("[Group] Show OVA [1080p]")]
    [InlineData("[Group] Show S00E03 [1080p]")]
    public void SpecialEpisodeNeverBecomesAnIntegerCandidate(string title)
    {
        var result = MikanRssEpisodeParser.Parse(title);

        Assert.Equal(TorrentEpisodeCandidateKind.Special, result.Kind);
        Assert.Null(result.NormalEpisode);
    }

    [Theory]
    [InlineData("[Group] Show [1080p] [HEVC]")]
    [InlineData("[Group] Show [2022.07.23] [1080p]")]
    public void UnreliableTitlesRemainUngrouped(string title)
    {
        var result = MikanRssEpisodeParser.Parse(title);

        Assert.Equal(TorrentEpisodeCandidateKind.Unknown, result.Kind);
        Assert.Null(result.SourceEpisode);
    }
}
