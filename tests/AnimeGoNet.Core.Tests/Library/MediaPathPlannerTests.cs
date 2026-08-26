using AnimeGoNet.Core.Library;

namespace AnimeGoNet.Core.Tests.Library;

public sealed class MediaPathPlannerTests
{
    [Fact]
    public void EpisodeUsesCanonicalTmdbSeriesSeasonAndEpisode()
    {
        var result = MediaPathPlanner.PlanRelativePath(new MediaPathInput(
            "葬送的芙莉莲: 再会",
            2,
            "episode",
            7,
            "Group/Source.EP48.5.MKV"));

        Assert.Equal(Path.Combine("葬送的芙莉莲_ 再会", "S02", "E007.MKV"), result);
    }

    [Fact]
    public void OtherPreservesSanitizedOriginalNameBelowConfirmedSeason()
    {
        var result = MediaPathPlanner.PlanRelativePath(new MediaPathInput(
            "CON",
            12,
            "other",
            null,
            "Extras\\PV: 01?.mkv"));

        Assert.Equal(Path.Combine("_CON", "S12", "Extras", "PV_ 01_.mkv"), result);
    }

    [Fact]
    public void ExtrasPreservesAttachmentNameBelowConfirmedSeason()
    {
        var result = MediaPathPlanner.PlanRelativePath(new MediaPathInput(
            "Medalist",
            2,
            "extras",
            null,
            "Release/Medalist [Fonts].7z"));

        Assert.Equal(
            Path.Combine("Medalist", "S02", "Extras", "Medalist [Fonts].7z"),
            result);
    }

    [Fact]
    public void AssociatedSubtitleKeepsLanguageAndTrackSuffix()
    {
        var result = MediaPathPlanner.PlanRelativePath(new MediaPathInput(
            "Series", 1, "episode", 3, "Anime - 03.zh-Hans.forced.ass", ".zh-Hans.forced.ass"));

        Assert.Equal(Path.Combine("Series", "S01", "E003.zh-Hans.forced.ass"), result);
    }

    [Fact]
    public void PortableComparisonNormalizesSeparatorsUnicodeAndCrossPlatformInvalidCharacters()
    {
        var torrentPath = "番組\\Cyborg 009: Ne\u0065\u0301mesis?.mkv";
        var qbittorrentPath = "番組/Cyborg 009_ Ne\u00E9mesis_.mkv";

        Assert.Equal(
            PortablePathNormalizer.NormalizeRelativePathForComparison(qbittorrentPath),
            PortablePathNormalizer.NormalizeRelativePathForComparison(torrentPath));
    }

    [Theory]
    [InlineData("/rooted/file.mkv")]
    [InlineData("C:\\rooted\\file.mkv")]
    [InlineData("show/../file.mkv")]
    [InlineData("show//file.mkv")]
    public void PortableComparisonRejectsRootedTraversalOrEmptySegments(string value)
    {
        Assert.Throws<ArgumentException>(() =>
            PortablePathNormalizer.NormalizeRelativePathForComparison(value));
    }

    [Fact]
    public void PortableComparisonPreservesCaseForCaseSensitiveFilesystems()
    {
        Assert.NotEqual(
            PortablePathNormalizer.NormalizeRelativePathForComparison("Show/EP03.mkv"),
            PortablePathNormalizer.NormalizeRelativePathForComparison("show/EP03.mkv"));
    }

    [Theory]
    [InlineData("pending", 1)]
    [InlineData("duplicate", 1)]
    [InlineData("episode", null)]
    [InlineData("episode", 0)]
    public void RejectsNonOrganizableOrUnverifiedEpisode(string disposition, int? episode)
    {
        Assert.Throws<ArgumentException>(() => MediaPathPlanner.PlanRelativePath(new MediaPathInput(
            "Series",
            1,
            disposition,
            episode,
            "episode.mkv")));
    }
}
