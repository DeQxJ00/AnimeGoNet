using AnimeGoNet.Core.Metadata;

namespace AnimeGoNet.Core.Tests.Metadata;

public sealed class U2FileEpisodeCandidateResolverTests
{
    private static readonly string[] AcceptedReasons = ["accepted", "accepted_season_episode_extension"];

    [Theory]
    [InlineData("[Class de 2 Banme ni Kawaii Onnanoko to Tomodachi ni Natta][08][BDRIP][1080P][H264_FLAC].mkv", 8)]
    [InlineData("[Group] Show - 11 [WebRip 1080p].mkv", 11)]
    [InlineData("[Group] Show S02E04 [1080p].mkv", 4)]
    public void UsesTheCopiedFilenameRulesForU2(string path, int expectedEpisode)
    {
        var result = U2FileEpisodeCandidateResolver.Resolve(path);

        Assert.True(result.IsCandidate);
        Assert.Equal(expectedEpisode, result.Episode);
        Assert.Contains(result.Reason, AcceptedReasons);
    }

    [Theory]
    [InlineData("[Group] Show [2024][1080p].mkv")]
    [InlineData("[Group] Show [01][02][1080p].mkv")]
    [InlineData("[Group] Show [SP01][1080p].mkv")]
    public void KeepsTheCopiedSafetyChecksForU2(string path)
    {
        var result = U2FileEpisodeCandidateResolver.Resolve(path);

        Assert.False(result.IsCandidate);
        Assert.Null(result.Episode);
    }
}
