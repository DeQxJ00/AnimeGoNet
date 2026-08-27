using AnimeGoNet.Core.Metadata;

namespace AnimeGoNet.Core.Tests.Metadata;

public sealed class U2FileEpisodeCandidateResolverTests
{
    private static readonly string[] AcceptedReasons =
        [
            "accepted",
            "accepted_season_episode_extension",
            "accepted_hash_episode_marker",
            "accepted_explicit_episode_marker",
            "accepted_standalone_episode_marker"
        ];

    [Theory]
    [InlineData("[Class de 2 Banme ni Kawaii Onnanoko to Tomodachi ni Natta][08][BDRIP][1080P][H264_FLAC].mkv", 8)]
    [InlineData("[Group] Show - 11 [WebRip 1080p].mkv", 11)]
    [InlineData("[AI-Raws] アストロガンガー #25 (BD HEVC 1568x1080 FLAC)[FE30BEBA].mkv", 25)]
    [InlineData("Baka to Test to Shokanjyu  EP03 1080p Bluray FLAC 2.0 x264-DECAY.mkv", 3)]
    [InlineData("[Group] Show S02E04 [1080p].mkv", 4)]
    [InlineData("[U2-RIP]GUNSLINGER GIRL 01 [兄妹- fratello -] (BD 1280X720 X264 FLAC).mkv", 1)]
    [InlineData("[U2-Rip] 戦う司書 the book of bantorra 27 「世界の力」 (BD 1920x1080 x264 FLACx2).mkv", 27)]
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
    [InlineData("[AI-Raws] アストロガンガー パイロット版「アストロマン」 (BD HEVC 1440x1080 FLAC)[3065DF18].mkv")]
    [InlineData("Baka to Test to Shokanjyu Extra7 1080p Bluray FLAC 2.0 x264-DECAY.mkv")]
    [InlineData("Baka to Test to Shokanjyu NCED3 1080p Bluray FLAC 2.0 x264-DECAY.mkv")]
    [InlineData("[U2-Rip] 戦う司書 the book of bantorra 映像特典 DRAMA 10 (BD 1920x1080 x264 FLAC).mkv")]
    [InlineData("[U2-Rip] 戦う司書 the book of bantorra 映像特典 ノンテロップOP1 (BD 1920x1080 x264 FLAC).mkv")]
    public void KeepsTheCopiedSafetyChecksForU2(string path)
    {
        var result = U2FileEpisodeCandidateResolver.Resolve(path);

        Assert.False(result.IsCandidate);
        Assert.Null(result.Episode);
    }
}
