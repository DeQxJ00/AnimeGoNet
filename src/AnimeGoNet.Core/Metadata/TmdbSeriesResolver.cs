namespace AnimeGoNet.Core.Metadata;

public sealed record TmdbSeriesResolutionResult(
    TmdbSeries? Value,
    MetadataFailure? Failure,
    IReadOnlyList<string> AttemptedTitles)
{
    public bool IsSuccess => Value is not null && Failure is null;
}

public sealed class TmdbSeriesResolver(ITmdbClient client)
{
    public async Task<TmdbSeriesResolutionResult> ResolveAsync(
        string title,
        CancellationToken cancellationToken = default) =>
        await ResolveAsync(
            title,
            static (_, _) => ValueTask.FromResult(true),
            cancellationToken).ConfigureAwait(false);

    public async Task<TmdbSeriesResolutionResult> ResolveAsync(
        string title,
        Func<TmdbSeries, CancellationToken, ValueTask<bool>> candidateValidator,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidateValidator);
        if (string.IsNullOrWhiteSpace(title))
        {
            return Failed(
                MetadataFailureKind.InvalidInput,
                "tmdb_title_required",
                accessConfirmed: false,
                []);
        }

        var originalTitle = title.Trim();
        var currentTitle = originalTitle;
        var attempts = new List<string>();
        var attempted = new HashSet<string>(StringComparer.Ordinal);
        var validatedSeriesIds = new HashSet<int>();
        var rejectedAsNotSimilar = false;
        try
        {
            for (var step = 0; step <= TmdbTitleHeuristics.SuffixStepCount; step++)
            {
                if (attempted.Add(currentTitle))
                {
                    attempts.Add(currentTitle);
                    var candidates = await client.SearchSeriesAsync(currentTitle, cancellationToken)
                        .ConfigureAwait(false);
                    if (candidates.Count == 0 && client is ITmdbRefreshClient refreshClient)
                    {
                        candidates = await refreshClient.RefreshSeriesSearchAsync(
                            currentTitle,
                            cancellationToken).ConfigureAwait(false);
                    }
                    var selected = SelectCandidates(originalTitle, currentTitle, candidates);
                    foreach (var candidate in selected.Values)
                    {
                        if (!validatedSeriesIds.Add(candidate.Id))
                        {
                            continue;
                        }

                        if (await candidateValidator(candidate, cancellationToken).ConfigureAwait(false))
                        {
                            return new TmdbSeriesResolutionResult(candidate, null, attempts);
                        }
                    }

                    rejectedAsNotSimilar |= selected.RejectedAsNotSimilar;
                }

                if (step < TmdbTitleHeuristics.SuffixStepCount)
                {
                    currentTitle = TmdbTitleHeuristics.ApplySuffixStep(currentTitle, step);
                }
            }

            return Failed(
                MetadataFailureKind.SemanticNoMatch,
                rejectedAsNotSimilar ? "tmdb_series_not_similar" : "tmdb_series_not_found",
                accessConfirmed: true,
                attempts);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TmdbClientException exception)
        {
            return Failed(
                exception.Kind,
                exception.SafeCode,
                exception.TmdbAccessConfirmed,
                attempts);
        }
    }

    private static CandidateSelection SelectCandidates(
        string originalTitle,
        string searchedTitle,
        IReadOnlyList<TmdbSeries> candidates)
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
                IsExact(candidate.OriginalName, originalTitle, searchedTitle)
                    || IsExact(candidate.Name, originalTitle, searchedTitle),
                Similarity(candidate, originalTitle, searchedTitle),
                index))
            .Where(candidate => candidate.Exact || candidate.Similarity >= TmdbTitleHeuristics.MinimumSimilarity)
            .OrderByDescending(candidate => candidate.Exact)
            .ThenByDescending(candidate => candidate.Similarity)
            .ThenBy(candidate => candidate.Index)
            .Select(candidate => candidate.Value)
            .ToArray();
        if (ranked.Length > 0)
        {
            return new CandidateSelection(ranked, false);
        }

        return new CandidateSelection([], true);
    }

    private static double Similarity(
        TmdbSeries candidate,
        string originalTitle,
        string searchedTitle) =>
        Math.Max(
            Math.Max(
                TmdbTitleHeuristics.SimilarText(candidate.OriginalName, originalTitle),
                TmdbTitleHeuristics.SimilarText(candidate.Name, originalTitle)),
            Math.Max(
                TmdbTitleHeuristics.SimilarText(candidate.OriginalName, searchedTitle),
                TmdbTitleHeuristics.SimilarText(candidate.Name, searchedTitle)));

    private static bool IsExact(string candidate, string originalTitle, string searchedTitle) =>
        string.Equals(candidate, originalTitle, StringComparison.Ordinal)
        || string.Equals(candidate, searchedTitle, StringComparison.Ordinal);

    private static TmdbSeriesResolutionResult Failed(
        MetadataFailureKind kind,
        string code,
        bool accessConfirmed,
        IReadOnlyList<string> attempts) =>
        new(null, new MetadataFailure(kind, code, accessConfirmed), attempts.ToArray());

    private sealed record CandidateSelection(
        IReadOnlyList<TmdbSeries> Values,
        bool RejectedAsNotSimilar);

    private sealed record RankedCandidate(
        TmdbSeries Value,
        bool Exact,
        double Similarity,
        int Index);
}
