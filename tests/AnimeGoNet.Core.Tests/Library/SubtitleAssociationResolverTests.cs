using AnimeGoNet.Core.Library;

namespace AnimeGoNet.Core.Tests.Library;

public sealed class SubtitleAssociationResolverTests
{
    [Fact]
    public void SameStemPreservesLanguageDefaultForcedAndSdhSuffix()
    {
        var results = SubtitleAssociationResolver.Resolve(
        [
            new TorrentMediaFile("video", "Show/Anime - 01.mkv", 1),
            new TorrentMediaFile("zh", "Show/Anime - 01.zh-Hans.default.ass", 1),
            new TorrentMediaFile("en", "Show/Anime - 01.en.forced.sdh.srt", 1),
        ]);

        Assert.Collection(
            results,
            item => Assert.Equal(("video", ".zh-Hans.default.ass", null), (item.VideoFileId, item.RenameSuffix, item.UnmatchedReason)),
            item => Assert.Equal(("video", ".en.forced.sdh.srt", null), (item.VideoFileId, item.RenameSuffix, item.UnmatchedReason)));
    }

    [Fact]
    public void UniqueEpisodeFallbackAssociatesDifferentStem()
    {
        var association = Assert.Single(SubtitleAssociationResolver.Resolve(
        [
            new TorrentMediaFile("video", "Show/[Group] Anime - 12.mkv", 12),
            new TorrentMediaFile("subtitle", "Subs/12.ass", 12),
        ]));

        Assert.Equal("video", association.VideoFileId);
        Assert.Equal(".ass", association.RenameSuffix);
    }

    [Fact]
    public void AmbiguousEpisodeDoesNotGuess()
    {
        var association = Assert.Single(SubtitleAssociationResolver.Resolve(
        [
            new TorrentMediaFile("v1", "A/Anime - 01.mkv", 1),
            new TorrentMediaFile("v2", "B/Anime - 01.mp4", 1),
            new TorrentMediaFile("subtitle", "Subs/01.ass", 1),
        ]));

        Assert.Null(association.VideoFileId);
        Assert.Equal("subtitle_episode_ambiguous", association.UnmatchedReason);
    }

    [Fact]
    public void IdxAndSubPairBindIndependentlyWithTheirExtensions()
    {
        var results = SubtitleAssociationResolver.Resolve(
        [
            new TorrentMediaFile("video", "Anime - 03.mkv", 3),
            new TorrentMediaFile("idx", "Anime - 03.zh.idx", 3),
            new TorrentMediaFile("sub", "Anime - 03.zh.sub", 3),
        ]);

        Assert.Equal([".zh.idx", ".zh.sub"], results.Select(result => result.RenameSuffix).ToArray());
        Assert.All(results, result => Assert.Equal("video", result.VideoFileId));
    }

    [Fact]
    public void SubtitleWithoutUniqueVideoIsUnmatched()
    {
        var association = Assert.Single(SubtitleAssociationResolver.Resolve(
            [new TorrentMediaFile("subtitle", "orphan.zh.ass", null)]));

        Assert.Null(association.VideoFileId);
        Assert.Equal("subtitle_unmatched", association.UnmatchedReason);
    }
}
