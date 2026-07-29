using AnimeGoNet.App.Metadata;
using AnimeGoNet.Core.Metadata;

namespace AnimeGoNet.App.Tests.Metadata;

public sealed class TmdbSeriesSeasonResolverTests
{
    [Fact]
    public async Task TriesJapaneseThenChineseUntilSeriesAndSeasonBothMatch()
    {
        var japaneseSeries = Series(10, "日文候选", "日本語候補");
        var chineseSeries = Series(20, "中文候选", "中国語候補");
        var client = new FakeTmdbClient(
            new Dictionary<string, IReadOnlyList<TmdbSeries>>(StringComparer.Ordinal)
            {
                ["日本語名"] = [japaneseSeries],
                ["中文名"] = [chineseSeries],
            },
            new Dictionary<int, TmdbSeriesDetails>
            {
                [10] = Details(japaneseSeries, Season(10, 1, new DateOnly(2018, 1, 1))),
                [20] = Details(chineseSeries, Season(20, 4, new DateOnly(2026, 4, 1))),
            });
        var resolver = new TmdbSeriesSeasonResolver(new TmdbSeriesResolver(client), client);

        var result = await resolver.ResolveAsync(
            ["日本語名", "中文名"],
            new DateOnly(2026, 4, 2));

        Assert.True(result.IsSuccess);
        Assert.Equal(20, result.Details!.Series.Id);
        Assert.Equal(4, result.Season!.SeasonNumber);
        Assert.Equal(["日本語名", "中文名"], client.SearchTitles);
        Assert.Equal([10, 20], client.DetailIds);
    }

    [Fact]
    public async Task SeasonMismatchContinuesCleanedSearchRoundsForSameTitle()
    {
        var rawSeries = Series(10, "作品 第四季", "作品 第四季");
        var cleanedSeries = Series(20, "作品", "作品");
        var client = new FakeTmdbClient(
            new Dictionary<string, IReadOnlyList<TmdbSeries>>(StringComparer.Ordinal)
            {
                ["作品 第四季"] = [rawSeries],
                ["作品"] = [cleanedSeries],
            },
            new Dictionary<int, TmdbSeriesDetails>
            {
                [10] = Details(rawSeries, Season(10, 1, new DateOnly(2018, 1, 1))),
                [20] = Details(cleanedSeries, Season(20, 4, new DateOnly(2026, 4, 1))),
            });
        var resolver = new TmdbSeriesSeasonResolver(new TmdbSeriesResolver(client), client);

        var result = await resolver.ResolveAsync(
            ["作品 第四季"],
            new DateOnly(2026, 4, 2));

        Assert.True(result.IsSuccess);
        Assert.Equal(20, result.Details!.Series.Id);
        Assert.Equal(4, result.Season!.SeasonNumber);
        Assert.Equal(["作品 第四季", "作品"], client.SearchTitles);
        Assert.Equal([10, 20], client.DetailIds);
    }

    [Fact]
    public async Task SeasonMismatchChecksNextEligibleSeriesFromSameSearchResponse()
    {
        var wrongSeasonSeries = Series(10, "同名作品", "同名作品");
        var matchingSeasonSeries = Series(20, "同名作品", "同名作品");
        var client = new FakeTmdbClient(
            new Dictionary<string, IReadOnlyList<TmdbSeries>>(StringComparer.Ordinal)
            {
                ["同名作品"] = [wrongSeasonSeries, matchingSeasonSeries],
            },
            new Dictionary<int, TmdbSeriesDetails>
            {
                [10] = Details(wrongSeasonSeries, Season(10, 1, new DateOnly(2018, 1, 1))),
                [20] = Details(matchingSeasonSeries, Season(20, 4, new DateOnly(2026, 4, 1))),
            });
        var resolver = new TmdbSeriesSeasonResolver(new TmdbSeriesResolver(client), client);

        var result = await resolver.ResolveAsync(
            ["同名作品"],
            new DateOnly(2026, 4, 2));

        Assert.True(result.IsSuccess);
        Assert.Equal(20, result.Details!.Series.Id);
        Assert.Equal(4, result.Season!.SeasonNumber);
        Assert.Equal(["同名作品"], client.SearchTitles);
        Assert.Equal([10, 20], client.DetailIds);
    }

    [Fact]
    public async Task MissingAirDateKeepsValidatedSeriesButDoesNotInventSeason()
    {
        var series = Series(10, "名称", "名前");
        var client = new FakeTmdbClient(
            new Dictionary<string, IReadOnlyList<TmdbSeries>>(StringComparer.Ordinal)
            {
                ["名前"] = [series],
            },
            new Dictionary<int, TmdbSeriesDetails>
            {
                [10] = Details(series, Season(10, 1, new DateOnly(2026, 1, 1))),
            });
        var resolver = new TmdbSeriesSeasonResolver(new TmdbSeriesResolver(client), client);

        var result = await resolver.ResolveAsync(["名前"], null);

        Assert.False(result.IsSuccess);
        Assert.True(result.HasValidatedSeries);
        Assert.Equal(10, result.Details!.Series.Id);
        Assert.Equal("tmdb_season_source_air_date_required", result.Failure!.Code);
    }

    [Fact]
    public async Task SameSeriesFromBothNamesReadsDetailsOnlyOnce()
    {
        var series = Series(10, "中文名", "日本語名");
        var client = new FakeTmdbClient(
            new Dictionary<string, IReadOnlyList<TmdbSeries>>(StringComparer.Ordinal)
            {
                ["日本語名"] = [series],
                ["中文名"] = [series],
            },
            new Dictionary<int, TmdbSeriesDetails>
            {
                [10] = Details(series, Season(10, 1, new DateOnly(2018, 1, 1))),
            });
        var resolver = new TmdbSeriesSeasonResolver(new TmdbSeriesResolver(client), client);

        var result = await resolver.ResolveAsync(
            ["日本語名", "中文名"],
            new DateOnly(2026, 1, 1));

        Assert.False(result.IsSuccess);
        Assert.True(result.HasValidatedSeries);
        Assert.Equal(["日本語名", "中文名"], client.SearchTitles);
        Assert.Equal([10], client.DetailIds);
    }

    private static TmdbSeries Series(int id, string name, string originalName) =>
        new(id, name, originalName, null);

    private static TmdbSeason Season(int seriesId, int number, DateOnly airDate) =>
        new(seriesId * 100 + number, seriesId, number, $"Season {number}", airDate, 12);

    private static TmdbSeriesDetails Details(TmdbSeries series, params TmdbSeason[] seasons) =>
        new(series, seasons);

    private sealed class FakeTmdbClient(
        IReadOnlyDictionary<string, IReadOnlyList<TmdbSeries>> searches,
        IReadOnlyDictionary<int, TmdbSeriesDetails> details) : ITmdbClient
    {
        public List<string> SearchTitles { get; } = [];

        public List<int> DetailIds { get; } = [];

        public Task<IReadOnlyList<TmdbSeries>> SearchSeriesAsync(
            string title,
            CancellationToken cancellationToken = default)
        {
            SearchTitles.Add(title);
            return Task.FromResult(searches.TryGetValue(title, out var value)
                ? value
                : (IReadOnlyList<TmdbSeries>)[]);
        }

        public Task<TmdbSeries?> GetSeriesAsync(int seriesId, CancellationToken cancellationToken = default) =>
            Task.FromResult<TmdbSeries?>(details.TryGetValue(seriesId, out var value) ? value.Series : null);

        public Task<TmdbSeriesDetails?> GetSeriesDetailsAsync(
            int seriesId,
            CancellationToken cancellationToken = default)
        {
            DetailIds.Add(seriesId);
            return Task.FromResult<TmdbSeriesDetails?>(details.TryGetValue(seriesId, out var value) ? value : null);
        }

        public Task<TmdbSeason?> GetSeasonAsync(
            int seriesId,
            int seasonNumber,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TmdbEpisode?> GetEpisodeAsync(
            int seriesId,
            int seasonNumber,
            int episodeNumber,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
