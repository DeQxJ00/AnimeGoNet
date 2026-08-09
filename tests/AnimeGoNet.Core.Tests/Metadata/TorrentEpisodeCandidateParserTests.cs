using AnimeGoNet.Core.Metadata;

namespace AnimeGoNet.Core.Tests.Metadata;

public sealed class TorrentEpisodeCandidateParserTests
{
    [Theory]
    [InlineData("[orion origin] Benriya Saitou-san [04] [1080p].mp4", 4)]
    [InlineData("[Group] Show [11v2] [1080p].mkv", 11)]
    [InlineData("[Group] Show - 11 [WebRip 1080p].mkv", 11)]
    [InlineData("Show EP12.mkv", 12)]
    [InlineData("Show E13.ass", 13)]
    [InlineData("Show 第14話.mkv", 14)]
    [InlineData("Show【15 END】.mkv", 15)]
    [InlineData("Show S02E04.mkv", 4)]
    public void UpstreamCompatibleIntegerPatternsReturnNormalCandidate(string path, int expected)
    {
        var result = TorrentEpisodeCandidateParser.Parse(path);

        Assert.Equal(TorrentEpisodeCandidateKind.Normal, result.Kind);
        Assert.Equal(expected, result.NormalEpisode);
        Assert.Equal(expected.ToString(System.Globalization.CultureInfo.InvariantCulture), result.SourceEpisode);
    }

    [Theory]
    [InlineData("Show [48.5] [1080p].mkv", "48.5")]
    [InlineData("Show EP 12.25.mkv", "12.25")]
    public void FractionalEpisodeIsNeverPromotedToNormalInteger(string path, string expected)
    {
        var result = TorrentEpisodeCandidateParser.Parse(path);

        Assert.Equal(TorrentEpisodeCandidateKind.Fractional, result.Kind);
        Assert.Equal(expected, result.SourceEpisode);
        Assert.Null(result.NormalEpisode);
        Assert.Equal("fractional_episode", result.Reason);
    }

    [Theory]
    [InlineData("Show [SP01].mkv")]
    [InlineData("Show OVA.mkv")]
    [InlineData("Show NCOP.ass")]
    [InlineData("Show S00E03.mkv")]
    [InlineData("Show Menu 01.mkv")]
    public void SpecialContentIsNeverPromotedToNormalInteger(string path)
    {
        var result = TorrentEpisodeCandidateParser.Parse(path);

        Assert.Equal(TorrentEpisodeCandidateKind.Special, result.Kind);
        Assert.Null(result.NormalEpisode);
        Assert.Equal("special_episode", result.Reason);
    }

    [Theory]
    [InlineData("Show [1080p] [HEVC].mkv")]
    [InlineData("poster.jpg")]
    public void ResolutionAndUnrelatedNumbersAreNotEpisodes(string path)
    {
        var result = TorrentEpisodeCandidateParser.Parse(path);

        Assert.Equal(TorrentEpisodeCandidateKind.Unknown, result.Kind);
        Assert.Null(result.NormalEpisode);
    }
}
