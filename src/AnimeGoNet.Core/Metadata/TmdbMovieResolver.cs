namespace AnimeGoNet.Core.Metadata;

public sealed record TmdbMovieResolutionResult(
    TmdbMovie? Value,
    MetadataFailure? Failure,
    IReadOnlyList<string> AttemptedTitles)
{
    public bool IsSuccess => Value is not null && Failure is null;
}

public sealed class TmdbMovieResolver(ITmdbMovieClient client)
{
    public async Task<TmdbMovieResolutionResult> ResolveAsync(
        IEnumerable<string> titles,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(titles);
        var candidates = titles
            .Where(title => !string.IsNullOrWhiteSpace(title))
            .Select(title => title.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (candidates.Length == 0)
        {
            return Failed(
                MetadataFailureKind.InvalidInput,
                "tmdb_movie_title_required",
                false,
                []);
        }

        var attempts = new List<string>();
        var attemptedQueries = new HashSet<string>(StringComparer.Ordinal);
        var inspectedMovieIds = new HashSet<int>();
        var rejectedAsNotSimilar = false;
        try
        {
            foreach (var title in candidates)
            {
                var currentTitle = title;
                for (var step = 0; step <= TmdbTitleHeuristics.SuffixStepCount; step++)
                {
                    if (attemptedQueries.Add(currentTitle))
                    {
                        attempts.Add(currentTitle);
                        var found = await client.SearchMoviesAsync(currentTitle, cancellationToken)
                            .ConfigureAwait(false);
                        var ranked = SelectCandidates(title, currentTitle, found);
                        rejectedAsNotSimilar |= ranked.RejectedAsNotSimilar;
                        foreach (var candidate in ranked.Values)
                        {
                            if (!inspectedMovieIds.Add(candidate.Id))
                            {
                                continue;
                            }

                            var verified = await client.GetMovieAsync(candidate.Id, cancellationToken)
                                .ConfigureAwait(false);
                            if (verified is not null && verified.Id == candidate.Id)
                            {
                                return new TmdbMovieResolutionResult(
                                    verified,
                                    null,
                                    attempts.ToArray());
                            }
                        }
                    }

                    if (step < TmdbTitleHeuristics.SuffixStepCount)
                    {
                        currentTitle = TmdbTitleHeuristics.ApplySuffixStep(currentTitle, step);
                    }
                }
            }

            return Failed(
                MetadataFailureKind.SemanticNoMatch,
                rejectedAsNotSimilar ? "tmdb_movie_not_similar" : "tmdb_movie_not_found",
                true,
                attempts);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TmdbClientException exception)
        {
            return Failed(exception.Kind, exception.SafeCode, exception.TmdbAccessConfirmed, attempts);
        }
    }

    private static CandidateSelection SelectCandidates(
        string originalTitle,
        string searchedTitle,
        IReadOnlyList<TmdbMovie> candidates)
    {
        if (candidates.Count == 0)
        {
            return new CandidateSelection([], false);
        }

        if (candidates.Count == 1)
        {
            return new CandidateSelection(candidates, false);
        }

        var ranked = candidates
            .Select((candidate, index) => new RankedCandidate(
                candidate,
                IsExact(candidate.OriginalTitle, originalTitle, searchedTitle)
                    || IsExact(candidate.Title, originalTitle, searchedTitle),
                Similarity(candidate, originalTitle, searchedTitle),
                index))
            .Where(candidate => candidate.Exact
                || candidate.Similarity >= TmdbTitleHeuristics.MinimumSimilarity)
            .OrderByDescending(candidate => candidate.Exact)
            .ThenByDescending(candidate => candidate.Similarity)
            .ThenBy(candidate => candidate.Index)
            .Select(candidate => candidate.Value)
            .ToArray();
        return ranked.Length > 0
            ? new CandidateSelection(ranked, false)
            : new CandidateSelection([], true);
    }

    private static double Similarity(
        TmdbMovie candidate,
        string originalTitle,
        string searchedTitle) =>
        Math.Max(
            Math.Max(
                TmdbTitleHeuristics.SimilarText(candidate.OriginalTitle, originalTitle),
                TmdbTitleHeuristics.SimilarText(candidate.Title, originalTitle)),
            Math.Max(
                TmdbTitleHeuristics.SimilarText(candidate.OriginalTitle, searchedTitle),
                TmdbTitleHeuristics.SimilarText(candidate.Title, searchedTitle)));

    private static bool IsExact(string candidate, string originalTitle, string searchedTitle) =>
        string.Equals(candidate, originalTitle, StringComparison.Ordinal)
        || string.Equals(candidate, searchedTitle, StringComparison.Ordinal);

    private static TmdbMovieResolutionResult Failed(
        MetadataFailureKind kind,
        string code,
        bool accessConfirmed,
        IReadOnlyList<string> attempts) =>
        new(null, new MetadataFailure(kind, code, accessConfirmed), attempts.ToArray());

    private sealed record CandidateSelection(
        IReadOnlyList<TmdbMovie> Values,
        bool RejectedAsNotSimilar);

    private sealed record RankedCandidate(
        TmdbMovie Value,
        bool Exact,
        double Similarity,
        int Index);
}
