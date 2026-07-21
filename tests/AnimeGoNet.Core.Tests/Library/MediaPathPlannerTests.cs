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

        Assert.Equal(Path.Combine("_CON", "S12", "Other", "PV_ 01_.mkv"), result);
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
