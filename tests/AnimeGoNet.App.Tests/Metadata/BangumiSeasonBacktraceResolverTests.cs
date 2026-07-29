using AnimeGoNet.App.Metadata;
using AnimeGoNet.Core.Metadata;

namespace AnimeGoNet.App.Tests.Metadata;

public sealed class BangumiSeasonBacktraceResolverTests
{
    [Fact]
    public async Task WalksMultipleLevelsUntilPredecessorAirDateMatches()
    {
        var client = new GraphClient(
            subjects: new Dictionary<int, BangumiSubject>
            {
                [2] = Subject(2, new DateOnly(2020, 1, 1)),
                [3] = Subject(3, new DateOnly(2018, 1, 1)),
            },
            relations: new Dictionary<int, IReadOnlyList<BangumiSubjectRelation>>
            {
                [1] = [Predecessor(2)],
                [2] = [Predecessor(3)],
            });
        var series2 = Series(20, "Subject 2");
        var series3 = Series(30, "Subject 3");
        var tmdb = new FakeTmdbClient(
            new Dictionary<string, IReadOnlyList<TmdbSeries>>(StringComparer.Ordinal)
            {
                ["Subject 2"] = [series2],
                ["Subject 3"] = [series3],
            },
            new Dictionary<int, TmdbSeriesDetails>
            {
                [20] = Details(series2, Season(20, 1, new DateOnly(2010, 1, 1))),
                [30] = Details(series3, Season(30, 1, new DateOnly(2018, 1, 1))),
            });

        var result = await CreateResolver(client, tmdb).ResolveAsync(1);

        Assert.True(result.IsSuccess);
        Assert.Equal(30, result.Details!.Series.Id);
        Assert.Equal(1, result.Season!.SeasonNumber);
        Assert.Equal(3, result.VisitedSubjectCount);
        Assert.Equal([1, 2], client.RelationRequests);
    }

    [Fact]
    public async Task SameLevelPredecessorsUseLatestAirDateThenLowestId()
    {
        var client = new GraphClient(
            subjects: new Dictionary<int, BangumiSubject>
            {
                [2] = Subject(2, new DateOnly(2018, 1, 1)),
                [3] = Subject(3, new DateOnly(2022, 1, 1)),
            },
            relations: new Dictionary<int, IReadOnlyList<BangumiSubjectRelation>>
            {
                [1] = [Predecessor(2), Predecessor(3)],
            });
        var series2 = Series(20, "Subject 2");
        var series3 = Series(30, "Subject 3");
        var tmdb = new FakeTmdbClient(
            new Dictionary<string, IReadOnlyList<TmdbSeries>>(StringComparer.Ordinal)
            {
                ["Subject 2"] = [series2],
                ["Subject 3"] = [series3],
            },
            new Dictionary<int, TmdbSeriesDetails>
            {
                [20] = Details(series2, Season(20, 1, new DateOnly(2018, 1, 1))),
                [30] = Details(series3, Season(30, 2, new DateOnly(2022, 1, 1))),
            });

        var result = await CreateResolver(client, tmdb).ResolveAsync(1);

        Assert.True(result.IsSuccess);
        Assert.Equal(30, result.Details!.Series.Id);
        Assert.Equal(2, result.Season!.SeasonNumber);
        Assert.Equal(["Subject 3"], tmdb.SearchTitles);
    }

    [Fact]
    public async Task MissingDatesStillTraverseAndCycleTerminates()
    {
        var client = new GraphClient(
            subjects: new Dictionary<int, BangumiSubject>
            {
                [2] = Subject(2, null),
            },
            relations: new Dictionary<int, IReadOnlyList<BangumiSubjectRelation>>
            {
                [1] = [Predecessor(2)],
                [2] = [Predecessor(1)],
            });
        var tmdb = new FakeTmdbClient(
            new Dictionary<string, IReadOnlyList<TmdbSeries>>(),
            new Dictionary<int, TmdbSeriesDetails>());

        var result = await CreateResolver(client, tmdb).ResolveAsync(1);

        Assert.False(result.IsSuccess);
        Assert.Equal("tmdb_backtrace_exhausted", result.Failure!.Code);
        Assert.Equal(2, result.VisitedSubjectCount);
        Assert.Equal([1, 2], client.RelationRequests);
        Assert.Empty(tmdb.SearchTitles);
    }

    [Fact]
    public async Task EachPredecessorTriesJapaneseAndChineseAsOneSeriesSeasonMatch()
    {
        var client = new GraphClient(
            subjects: new Dictionary<int, BangumiSubject>
            {
                [2] = new BangumiSubject(
                    2,
                    "日本語名",
                    "中文名",
                    new DateOnly(2026, 4, 1),
                    12),
            },
            relations: new Dictionary<int, IReadOnlyList<BangumiSubjectRelation>>
            {
                [1] = [Predecessor(2)],
            });
        var japaneseSeries = Series(20, "日本語名");
        var chineseSeries = Series(21, "中文名");
        var tmdb = new FakeTmdbClient(
            new Dictionary<string, IReadOnlyList<TmdbSeries>>(StringComparer.Ordinal)
            {
                ["日本語名"] = [japaneseSeries],
                ["中文名"] = [chineseSeries],
            },
            new Dictionary<int, TmdbSeriesDetails>
            {
                [20] = Details(japaneseSeries, Season(20, 1, new DateOnly(2018, 1, 1))),
                [21] = Details(chineseSeries, Season(21, 4, new DateOnly(2026, 4, 1))),
            });

        var result = await CreateResolver(client, tmdb).ResolveAsync(1);

        Assert.True(result.IsSuccess);
        Assert.Equal(21, result.Details!.Series.Id);
        Assert.Equal(4, result.Season!.SeasonNumber);
        Assert.Equal(["日本語名", "中文名"], tmdb.SearchTitles);
    }

    private static BangumiSeasonBacktraceResolver CreateResolver(
        GraphClient bangumi,
        FakeTmdbClient tmdb) =>
        new(bangumi, new TmdbSeriesSeasonResolver(new TmdbSeriesResolver(tmdb), tmdb));

    private static TmdbSeries Series(int id, string name) => new(id, name, name, null);

    private static TmdbSeason Season(int seriesId, int number, DateOnly airDate) =>
        new(seriesId * 100 + number, seriesId, number, $"Season {number}", airDate, 12);

    private static TmdbSeriesDetails Details(TmdbSeries series, params TmdbSeason[] seasons) =>
        new(series, seasons);

    private sealed class FakeTmdbClient(
        IReadOnlyDictionary<string, IReadOnlyList<TmdbSeries>> searches,
        IReadOnlyDictionary<int, TmdbSeriesDetails> details) : ITmdbClient
    {
        public List<string> SearchTitles { get; } = [];

        public Task<IReadOnlyList<TmdbSeries>> SearchSeriesAsync(
            string title,
            CancellationToken cancellationToken = default)
        {
            SearchTitles.Add(title);
            return Task.FromResult(searches.TryGetValue(title, out var value)
                ? value
                : (IReadOnlyList<TmdbSeries>)[]);
        }

        public Task<TmdbSeries?> GetSeriesAsync(
            int seriesId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<TmdbSeries?>(details.TryGetValue(seriesId, out var value) ? value.Series : null);

        public Task<TmdbSeriesDetails?> GetSeriesDetailsAsync(
            int seriesId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<TmdbSeriesDetails?>(details.TryGetValue(seriesId, out var value) ? value : null);

        public Task<TmdbSeason?> GetSeasonAsync(
            int seriesId,
            int seasonNumber,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<TmdbSeason?>(
                details.TryGetValue(seriesId, out var value)
                    ? value.Seasons.SingleOrDefault(season => season.SeasonNumber == seasonNumber)
                    : null);

        public Task<TmdbEpisode?> GetEpisodeAsync(
            int seriesId,
            int seasonNumber,
            int episodeNumber,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private static BangumiSubject Subject(int id, DateOnly? airDate) =>
        new(id, $"Subject {id}", string.Empty, airDate, 12);

    private static BangumiSubjectRelation Predecessor(int id) =>
        new(id, 2, $"Subject {id}", string.Empty, "前传");

    private sealed class GraphClient(
        IReadOnlyDictionary<int, BangumiSubject> subjects,
        IReadOnlyDictionary<int, IReadOnlyList<BangumiSubjectRelation>> relations) : IBangumiSubjectClient
    {
        public List<int> RelationRequests { get; } = [];

        public Task<BangumiSubject?> GetSubjectAsync(int subjectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(subjects.TryGetValue(subjectId, out var value) ? value : null);

        public Task<IReadOnlyList<BangumiSubjectRelation>> GetRelatedSubjectsAsync(
            int subjectId,
            CancellationToken cancellationToken = default)
        {
            RelationRequests.Add(subjectId);
            return Task.FromResult(relations.TryGetValue(subjectId, out var values)
                ? values
                : (IReadOnlyList<BangumiSubjectRelation>)[]);
        }
    }
}
