using AnimeGoNet.Core.Metadata;

namespace AnimeGoNet.Core.Tests.Metadata;

public sealed class BangumiTmdbEpisodeDateResolverTests
{
    [Fact]
    public void MapsLocalBangumiEpisodeToGlobalTmdbEpisodeByAirDate()
    {
        BangumiEpisode[] bangumi =
        [
            new(42599806, 0, 6, new DateOnly(2024, 11, 6)),
        ];
        TmdbEpisode[] tmdb =
        [
            new(6, 65942, 1, 6, "Episode 6", new DateOnly(2016, 5, 9)),
            new(56, 65942, 1, 56, "Episode 56", new DateOnly(2024, 11, 6)),
        ];

        var result = BangumiTmdbEpisodeDateResolver.Resolve(bangumi, tmdb, 6);

        Assert.True(result.IsSuccess);
        Assert.Equal(56, result.Episode!.EpisodeNumber);
        Assert.Equal(TmdbEpisodeDateEvidenceKind.BangumiEpisode, result.EvidenceKind);
    }

    [Fact]
    public void MapsBangumiGlobalSortNumberToTmdbEpisodeByAirDate()
    {
        var result = BangumiTmdbEpisodeDateResolver.Resolve(
            [new BangumiEpisode(9, 0, 9, new DateOnly(2021, 8, 31), SortNumber: 45)],
            [new TmdbEpisode(21, 82684, 2, 21, "Episode 21", new DateOnly(2021, 8, 31))],
            45);

        Assert.True(result.IsSuccess);
        Assert.Equal(21, result.Episode!.EpisodeNumber);
    }

    [Fact]
    public void SameNearestDateIsAmbiguousInsteadOfGuessing()
    {
        BangumiEpisode[] bangumi =
        [
            new(1, 0, 4, new DateOnly(2026, 7, 22)),
        ];
        TmdbEpisode[] tmdb =
        [
            new(10, 1, 1, 10, "A", new DateOnly(2026, 7, 22)),
            new(11, 1, 1, 11, "B", new DateOnly(2026, 7, 22)),
        ];

        var result = BangumiTmdbEpisodeDateResolver.Resolve(bangumi, tmdb, 4);

        Assert.Equal(BangumiTmdbEpisodeDateMatchKind.Ambiguous, result.Kind);
        Assert.Equal("tmdb_episode_bangumi_date_ambiguous", result.FailureCode);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void PrimaryDateMatchAllowsOneCalendarDayDifference(int difference)
    {
        BangumiEpisode[] bangumi =
        [
            new(898797, 0, 13, new DateOnly(2019, 12, 25)),
        ];
        TmdbEpisode[] tmdb =
        [
            new(2013394, 91768, 1, 13, "Episode 13", new DateOnly(2019, 12, 25).AddDays(difference)),
            new(2013395, 91768, 1, 14, "Episode 14", new DateOnly(2019, 12, 25).AddDays(difference)),
        ];

        var result = BangumiTmdbEpisodeDateResolver.Resolve(bangumi, tmdb, 13);

        Assert.True(result.IsSuccess);
        Assert.Equal(13, result.Episode!.EpisodeNumber);
        Assert.Equal(1, result.AirDateDifferenceDays);
        Assert.Equal(TmdbEpisodeDateEvidenceKind.BangumiEpisode, result.EvidenceKind);
    }

    [Fact]
    public void MissingDateEvidenceCannotProduceDateMatch()
    {
        var result = BangumiTmdbEpisodeDateResolver.Resolve(
            [new BangumiEpisode(1, 0, 4, null)],
            [new TmdbEpisode(4, 1, 1, 4, "Episode 4", null)],
            4);

        Assert.Equal(BangumiTmdbEpisodeDateMatchKind.NotApplicable, result.Kind);
    }

    [Fact]
    public void DistantTmdbDatesAreAConflict()
    {
        var result = BangumiTmdbEpisodeDateResolver.Resolve(
            [new BangumiEpisode(1, 0, 4, new DateOnly(2026, 7, 22))],
            [new TmdbEpisode(4, 1, 1, 4, "Episode 4", new DateOnly(2020, 1, 1))],
            4);

        Assert.Equal(BangumiTmdbEpisodeDateMatchKind.NoMatch, result.Kind);
        Assert.Equal("tmdb_episode_bangumi_date_not_found", result.FailureCode);
    }

    [Fact]
    public void MissingExactSourceEpisodeDoesNotUseTorrentPublicationDate()
    {
        var result = BangumiTmdbEpisodeDateResolver.Resolve(
            [new BangumiEpisode(1, 0, 5, new DateOnly(2026, 7, 22))],
            [new TmdbEpisode(55, 1, 1, 55, "Episode 55", new DateOnly(2026, 7, 22))],
            45);

        Assert.Equal(BangumiTmdbEpisodeDateMatchKind.NotApplicable, result.Kind);
    }

    [Fact]
    public void OneDayEpisodeDifferenceUsesPrimaryDateEvidence()
    {
        BangumiEpisode[] bangumi =
        [
            new(15, 0, 15, new DateOnly(2026, 7, 25)),
            new(16, 0, 16, new DateOnly(2026, 8, 1)),
        ];
        TmdbEpisode[] tmdb =
        [
            new(115, 91768, 2, 15, "Episode 15", new DateOnly(2026, 7, 26)),
            new(116, 91768, 2, 16, "Episode 16", new DateOnly(2026, 8, 2)),
        ];

        var result = BangumiTmdbEpisodeDateResolver.Resolve(
            bangumi,
            tmdb,
            15);

        Assert.True(result.IsSuccess);
        Assert.Equal(15, result.Episode!.EpisodeNumber);
        Assert.Equal(TmdbEpisodeDateEvidenceKind.BangumiEpisode, result.EvidenceKind);
    }

    [Theory]
    [InlineData(-7)]
    [InlineData(-2)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    public void SingleFileFallbackAcceptsNearestSameNumberWithinSevenDays(int difference)
    {
        var sourceDate = new DateOnly(2026, 8, 7);
        var result = BangumiTmdbEpisodeDateResolver.Resolve(
            [new BangumiEpisode(6, 0, 6, sourceDate)],
            [new TmdbEpisode(600, 302051, 1, 6, "Episode 6", sourceDate.AddDays(difference))],
            6,
            allowFilenameNearestFallback: true);

        Assert.True(result.IsSuccess);
        Assert.Equal(6, result.Episode!.EpisodeNumber);
        Assert.Equal(Math.Abs(difference), result.AirDateDifferenceDays);
        Assert.Equal(TmdbEpisodeDateEvidenceKind.FilenameEpisodeNearestDate, result.EvidenceKind);
    }

    [Fact]
    public void NearestDateFallbackIsDisabledForMultipleFiles()
    {
        var result = BangumiTmdbEpisodeDateResolver.Resolve(
            [new BangumiEpisode(6, 0, 6, new DateOnly(2026, 8, 7))],
            [new TmdbEpisode(600, 302051, 1, 6, "Episode 6", new DateOnly(2026, 8, 10))],
            6);

        Assert.Equal(BangumiTmdbEpisodeDateMatchKind.NoMatch, result.Kind);
        Assert.Equal("tmdb_episode_bangumi_date_not_found", result.FailureCode);
    }

    [Fact]
    public void FilenameFallbackRejectsNearestDateBeyondSevenDays()
    {
        var result = BangumiTmdbEpisodeDateResolver.Resolve(
            [new BangumiEpisode(6, 0, 6, new DateOnly(2026, 8, 1))],
            [new TmdbEpisode(600, 302051, 1, 6, "Episode 6", new DateOnly(2026, 8, 9))],
            6,
            allowFilenameNearestFallback: true);

        Assert.Equal(BangumiTmdbEpisodeDateMatchKind.NoMatch, result.Kind);
        Assert.Equal("tmdb_episode_bangumi_nearest_date_too_distant", result.FailureCode);
        Assert.Equal(8, result.AirDateDifferenceDays);
    }

    [Fact]
    public void FilenameFallbackRejectsNearestTmdbEpisodeWithDifferentNumber()
    {
        var result = BangumiTmdbEpisodeDateResolver.Resolve(
            [new BangumiEpisode(6, 0, 6, new DateOnly(2026, 8, 1))],
            [new TmdbEpisode(607, 302051, 1, 7, "Episode 7", new DateOnly(2026, 8, 4))],
            6,
            allowFilenameNearestFallback: true);

        Assert.Equal(BangumiTmdbEpisodeDateMatchKind.NoMatch, result.Kind);
        Assert.Equal("tmdb_episode_bangumi_nearest_date_filename_mismatch", result.FailureCode);
        Assert.Equal(3, result.AirDateDifferenceDays);
    }
}
