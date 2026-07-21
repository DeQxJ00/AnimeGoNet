using AnimeGoNet.Core.Metadata;

namespace AnimeGoNet.App.Metadata;

public sealed record BangumiSeasonBacktraceResult(
    TmdbSeason? Season,
    MetadataFailure? Failure,
    int VisitedSubjectCount)
{
    public bool IsSuccess => Season is not null && Failure is null;
}

public sealed class BangumiSeasonBacktraceResolver(IBangumiSubjectClient bangumi)
{
    public async Task<BangumiSeasonBacktraceResult> ResolveAsync(
        int subjectId,
        IReadOnlyList<TmdbSeason> seasons,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(subjectId, 0);
        ArgumentNullException.ThrowIfNull(seasons);
        var visited = new HashSet<int> { subjectId };
        int[] currentLevel = [subjectId];

        while (currentLevel.Length > 0)
        {
            var predecessorIds = new HashSet<int>();
            foreach (var currentId in currentLevel)
            {
                var relations = await bangumi.GetRelatedSubjectsAsync(currentId, cancellationToken)
                    .ConfigureAwait(false);
                foreach (var relation in relations)
                {
                    if (string.Equals(relation.Relation, "前传", StringComparison.Ordinal)
                        && !visited.Contains(relation.Id))
                    {
                        predecessorIds.Add(relation.Id);
                    }
                }
            }

            if (predecessorIds.Count == 0)
            {
                break;
            }

            var predecessors = new List<BangumiSubject>();
            foreach (var predecessorId in predecessorIds.Order())
            {
                visited.Add(predecessorId);
                var subject = await bangumi.GetSubjectAsync(predecessorId, cancellationToken).ConfigureAwait(false);
                if (subject is not null)
                {
                    predecessors.Add(subject);
                }
            }

            predecessors.Sort(static (left, right) =>
            {
                var date = Nullable.Compare(right.AirDate, left.AirDate);
                return date != 0 ? date : left.Id.CompareTo(right.Id);
            });
            foreach (var predecessor in predecessors)
            {
                if (predecessor.AirDate is null)
                {
                    continue;
                }

                var selected = TmdbSeasonSelector.SelectByAirDate(seasons, predecessor.AirDate);
                if (selected.IsSuccess)
                {
                    return new BangumiSeasonBacktraceResult(selected.Value, null, visited.Count);
                }
            }

            currentLevel = predecessors.Select(subject => subject.Id).ToArray();
        }

        return new BangumiSeasonBacktraceResult(
            null,
            new MetadataFailure(MetadataFailureKind.SemanticNoMatch, "tmdb_backtrace_exhausted", true),
            visited.Count);
    }
}
