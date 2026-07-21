using AnimeGoNet.Core.Metadata;

namespace AnimeGoNet.Core.Tests.Metadata;

public sealed class TmdbSeasonFallbackSelectorTests
{
    private static readonly TmdbSeason[] Seasons =
    [
        new(11, 7, 1, "Season 1", new DateOnly(2020, 1, 1), 12),
        new(12, 7, 2, "Season 2", new DateOnly(2022, 1, 1), 12),
    ];

    [Theory]
    [InlineData("Example Season 2", 2)]
    [InlineData("Example 2nd Season", 2)]
    [InlineData("示例 第二季", 2)]
    [InlineData("示例 第2期", 2)]
    public void TitleSeasonSelectsExistingPositiveSeason(string title, int expected)
    {
        Assert.Equal(expected, TmdbSeasonFallbackSelector.SelectTitleSeason(title, Seasons)!.SeasonNumber);
    }

    [Fact]
    public void MissingOrUnknownTitleSeasonDoesNotInventSeason()
    {
        Assert.Null(TmdbSeasonFallbackSelector.SelectTitleSeason("Example", Seasons));
        Assert.Null(TmdbSeasonFallbackSelector.SelectTitleSeason("Example Season 3", Seasons));
    }

    [Fact]
    public void FirstSeasonRequiresRealSeasonOne()
    {
        Assert.Equal(1, TmdbSeasonFallbackSelector.SelectFirstSeason(Seasons)!.SeasonNumber);
        Assert.Null(TmdbSeasonFallbackSelector.SelectFirstSeason([Seasons[1]]));
    }
}
