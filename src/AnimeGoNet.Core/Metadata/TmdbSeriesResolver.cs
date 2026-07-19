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
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return Failed(
                MetadataFailureKind.InvalidInput,
                "tmdb_title_required",
                accessConfirmed: false,
                []);
        }

        var originalTitle = title;
        var currentTitle = title;
        var attempts = new List<string>();
        try
        {
            for (var step = 0; step <= TmdbTitleHeuristics.SuffixStepCount; step++)
            {
                attempts.Add(currentTitle);
                var candidates = await client.SearchSeriesAsync(currentTitle, cancellationToken).ConfigureAwait(false);
                var selected = Select(originalTitle, candidates);
                if (selected.Value is not null)
                {
                    return new TmdbSeriesResolutionResult(selected.Value, null, attempts);
                }

                if (selected.RejectedAsNotSimilar)
                {
                    return Failed(
                        MetadataFailureKind.SemanticNoMatch,
                        "tmdb_series_not_similar",
                        accessConfirmed: true,
                        attempts);
                }

                if (step < TmdbTitleHeuristics.SuffixStepCount)
                {
                    currentTitle = TmdbTitleHeuristics.ApplySuffixStep(currentTitle, step);
                }
            }

            return Failed(
                MetadataFailureKind.SemanticNoMatch,
                "tmdb_series_not_found",
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

    private static Selection Select(string originalTitle, IReadOnlyList<TmdbSeries> candidates)
    {
        if (candidates.Count == 0)
        {
            return default;
        }

        if (candidates.Count == 1)
        {
            return new Selection(candidates[0], false);
        }

        var exact = candidates.FirstOrDefault(candidate =>
            string.Equals(candidate.OriginalName, originalTitle, StringComparison.Ordinal));
        if (exact is not null)
        {
            return new Selection(exact, false);
        }

        TmdbSeries? best = null;
        var maximum = 0d;
        foreach (var candidate in candidates)
        {
            var similarity = TmdbTitleHeuristics.SimilarText(candidate.OriginalName, originalTitle);
            if (similarity > maximum)
            {
                maximum = similarity;
                best = candidate;
            }
        }

        return maximum >= TmdbTitleHeuristics.MinimumSimilarity
            ? new Selection(best, false)
            : new Selection(null, true);
    }

    private static TmdbSeriesResolutionResult Failed(
        MetadataFailureKind kind,
        string code,
        bool accessConfirmed,
        IReadOnlyList<string> attempts) =>
        new(null, new MetadataFailure(kind, code, accessConfirmed), attempts.ToArray());

    private readonly record struct Selection(TmdbSeries? Value, bool RejectedAsNotSimilar);
}
