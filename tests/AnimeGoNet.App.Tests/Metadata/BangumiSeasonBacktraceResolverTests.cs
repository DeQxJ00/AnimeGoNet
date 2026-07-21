using AnimeGoNet.App.Metadata;
using AnimeGoNet.Core.Metadata;

namespace AnimeGoNet.App.Tests.Metadata;

public sealed class BangumiSeasonBacktraceResolverTests
{
    private static readonly TmdbSeason[] Seasons =
    [
        new(10, 7, 1, "Season 1", new DateOnly(2018, 1, 1), 12),
        new(20, 7, 2, "Season 2", new DateOnly(2022, 1, 1), 12),
    ];

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

        var result = await new BangumiSeasonBacktraceResolver(client).ResolveAsync(1, Seasons);

        Assert.True(result.IsSuccess);
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

        var result = await new BangumiSeasonBacktraceResolver(client).ResolveAsync(1, Seasons);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Season!.SeasonNumber);
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

        var result = await new BangumiSeasonBacktraceResolver(client).ResolveAsync(1, Seasons);

        Assert.False(result.IsSuccess);
        Assert.Equal("tmdb_backtrace_exhausted", result.Failure!.Code);
        Assert.Equal(2, result.VisitedSubjectCount);
        Assert.Equal([1, 2], client.RelationRequests);
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
