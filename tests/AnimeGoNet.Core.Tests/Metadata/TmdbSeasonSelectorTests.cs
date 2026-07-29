using AnimeGoNet.Core.Metadata;

namespace AnimeGoNet.Core.Tests.Metadata;

public sealed class TmdbSeasonSelectorTests
{
    [Fact]
    public void SelectsNearestOrdinarySeasonAndSkipsSpecials()
    {
        var result = TmdbSeasonSelector.SelectByAirDate(
        [
            Season(10, 0, "Specials", new DateOnly(2022, 7, 6)),
            Season(11, 1, "Season 1", new DateOnly(2017, 7, 7)),
            Season(12, 2, "Season 2", new DateOnly(2022, 7, 6)),
        ],
        new DateOnly(2022, 7, 8));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.SeasonNumber);
        Assert.Equal(2, result.AirDateDifferenceDays);
    }

    [Theory]
    [InlineData(90, true)]
    [InlineData(91, false)]
    public void PreservesUpstreamNinetyDayThreshold(int difference, bool expectedSuccess)
    {
        var source = new DateOnly(2022, 1, 1);
        var result = TmdbSeasonSelector.SelectByAirDate(
            [Season(1, 1, "Season 1", source.AddDays(difference))],
            source);

        Assert.Equal(expectedSuccess, result.IsSuccess);
        if (!expectedSuccess)
        {
            Assert.Equal("tmdb_season_air_date_not_matched", result.Failure!.Code);
        }
    }

    [Fact]
    public void SeasonWithoutAirDateIsIgnored()
    {
        var result = TmdbSeasonSelector.SelectByAirDate(
        [
            Season(1, 0, "Specials", null),
            Season(2, 1, "Season 1", null),
            Season(3, 2, "Season 2", new DateOnly(2024, 1, 1)),
        ],
        new DateOnly(2024, 1, 2));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.SeasonNumber);
        Assert.Equal(1, result.AirDateDifferenceDays);
    }

    [Fact]
    public void MissingSourceDateIsSemanticNoMatch()
    {
        var result = TmdbSeasonSelector.SelectByAirDate(
            [Season(3, 2, "Season 2", new DateOnly(2024, 1, 1))],
            null);

        Assert.False(result.IsSuccess);
        Assert.Equal("tmdb_season_source_air_date_required", result.Failure!.Code);
        Assert.True(result.Failure.TmdbAccessConfirmed);
    }

    [Fact]
    public void SpecialsOnlyIsSemanticNoMatch()
    {
        var result = TmdbSeasonSelector.SelectByAirDate(
            [Season(1, 0, "Specials", new DateOnly(2022, 1, 1))],
            new DateOnly(2022, 1, 1));

        Assert.False(result.IsSuccess);
        Assert.Equal(MetadataFailureKind.SemanticNoMatch, result.Failure!.Kind);
        Assert.True(result.Failure.TmdbAccessConfirmed);
    }

    private static TmdbSeason Season(int id, int number, string name, DateOnly? airDate) =>
        new(id, 100, number, name, airDate, 12);
}
