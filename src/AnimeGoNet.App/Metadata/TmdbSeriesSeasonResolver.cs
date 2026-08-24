using AnimeGoNet.Core.Metadata;

namespace AnimeGoNet.App.Metadata;

public sealed record TmdbSeriesSeasonResolutionResult(
    TmdbSeriesDetails? Details,
    TmdbSeason? Season,
    MetadataFailure? Failure,
    IReadOnlyList<string> AttemptedTitles)
{
    public bool IsSuccess => Details is not null && Season is not null && Failure is null;

    public bool HasValidatedSeries => Details is not null;
}

public sealed class TmdbSeriesSeasonResolver(
    TmdbSeriesResolver seriesResolver,
    ITmdbClient tmdb)
{
    public async Task<TmdbSeriesSeasonResolutionResult> ResolveAsync(
        IEnumerable<string> titles,
        DateOnly? airDate,
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
                null,
                new MetadataFailure(MetadataFailureKind.InvalidInput, "tmdb_title_required", false),
                []);
        }

        TmdbSeriesDetails? firstValidatedSeries = null;
        MetadataFailure? lastSemanticFailure = null;
        var attemptedTitles = new List<string>();
        var inspectedSeriesIds = new HashSet<int>();
        foreach (var title in candidates)
        {
            TmdbSeriesDetails? matchedDetails = null;
            TmdbSeason? matchedSeason = null;
            var seriesResult = await seriesResolver.ResolveAsync(
                title,
                async (series, token) =>
                {
                    if (!inspectedSeriesIds.Add(series.Id))
                    {
                        return false;
                    }

                    var details = await tmdb.GetSeriesDetailsAsync(series.Id, token).ConfigureAwait(false);
                    if ((details is null || details.Series.Id != series.Id)
                        && tmdb is ITmdbRefreshClient refreshClient)
                    {
                        details = await refreshClient.RefreshSeriesDetailsAsync(
                            series.Id,
                            token).ConfigureAwait(false);
                    }
                    if (details is null || details.Series.Id != series.Id)
                    {
                        lastSemanticFailure = new MetadataFailure(
                            MetadataFailureKind.SemanticNoMatch,
                            "tmdb_series_details_not_found",
                            true);
                        return false;
                    }

                    firstValidatedSeries ??= details;
                    var seasonResult = TmdbSeasonSelector.SelectByAirDate(details.Seasons, airDate);
                    if (!seasonResult.IsSuccess
                        && ShouldRefreshSeasonList(seasonResult.Failure)
                        && tmdb is ITmdbRefreshClient detailsRefreshClient)
                    {
                        var refreshedDetails = await detailsRefreshClient.RefreshSeriesDetailsAsync(
                            series.Id,
                            token).ConfigureAwait(false);
                        if (refreshedDetails is not null
                            && refreshedDetails.Series.Id == series.Id)
                        {
                            details = refreshedDetails;
                            firstValidatedSeries = refreshedDetails;
                            seasonResult = TmdbSeasonSelector.SelectByAirDate(
                                refreshedDetails.Seasons,
                                airDate);
                        }
                    }
                    if (!seasonResult.IsSuccess)
                    {
                        lastSemanticFailure = seasonResult.Failure;
                        return false;
                    }

                    var selectedSeason = seasonResult.Value!;
                    var verifiedSeason = await tmdb.GetSeasonAsync(
                        details.Series.Id,
                        selectedSeason.SeasonNumber,
                        token).ConfigureAwait(false);
                    if (!IsValidSeason(verifiedSeason, details.Series.Id, selectedSeason.SeasonNumber)
                        && tmdb is ITmdbRefreshClient seasonRefreshClient)
                    {
                        verifiedSeason = await seasonRefreshClient.RefreshSeasonAsync(
                            details.Series.Id,
                            selectedSeason.SeasonNumber,
                            token).ConfigureAwait(false);
                    }
                    if (!IsValidSeason(verifiedSeason, details.Series.Id, selectedSeason.SeasonNumber))
                    {
                        lastSemanticFailure = new MetadataFailure(
                            MetadataFailureKind.SemanticNoMatch,
                            "tmdb_season_not_found",
                            true);
                        return false;
                    }

                    matchedDetails = details;
                    matchedSeason = verifiedSeason;
                    return true;
                },
                cancellationToken).ConfigureAwait(false);
            attemptedTitles.AddRange(seriesResult.AttemptedTitles);
            if (seriesResult.IsSuccess)
            {
                return new TmdbSeriesSeasonResolutionResult(
                    matchedDetails!,
                    matchedSeason!,
                    null,
                    attemptedTitles.ToArray());
            }

            var failure = seriesResult.Failure!;
            if (failure.Kind != MetadataFailureKind.SemanticNoMatch)
            {
                return Failed(firstValidatedSeries, failure, attemptedTitles);
            }

            if (lastSemanticFailure is null)
            {
                lastSemanticFailure = failure;
            }
        }

        return Failed(
            firstValidatedSeries,
            lastSemanticFailure ?? new MetadataFailure(
                MetadataFailureKind.SemanticNoMatch,
                "tmdb_series_not_found",
                true),
            attemptedTitles);
    }

    public static IReadOnlyList<string> BangumiTitles(BangumiSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        return new[] { subject.Name, subject.ChineseName }
            .Where(title => !string.IsNullOrWhiteSpace(title))
            .Select(title => title.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static TmdbSeriesSeasonResolutionResult Failed(
        TmdbSeriesDetails? details,
        MetadataFailure failure,
        IReadOnlyList<string> attemptedTitles) =>
        new(details, null, failure, attemptedTitles.ToArray());

    private static bool ShouldRefreshSeasonList(MetadataFailure? failure) =>
        failure?.Code is "tmdb_seasons_empty" or "tmdb_season_air_date_not_matched";

    private static bool IsValidSeason(TmdbSeason? season, int seriesId, int seasonNumber) =>
        season is not null
        && season.Id > 0
        && season.SeriesId == seriesId
        && season.SeasonNumber == seasonNumber;
}
