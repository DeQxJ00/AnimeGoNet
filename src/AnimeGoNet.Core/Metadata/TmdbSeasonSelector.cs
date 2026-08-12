namespace AnimeGoNet.Core.Metadata;

public static class TmdbSeasonSelector
{
    public const int MaximumAirDateDifferenceDays = 1;

    public static TmdbSeasonResolutionResult SelectByAirDate(
        IReadOnlyList<TmdbSeason> seasons,
        DateOnly? sourceAirDate)
    {
        ArgumentNullException.ThrowIfNull(seasons);
        if (seasons.Count == 0)
        {
            return Failed("tmdb_seasons_empty");
        }

        if (sourceAirDate is null)
        {
            return Failed("tmdb_season_source_air_date_required");
        }

        TmdbSeason? selected = null;
        var minimum = 36_500;
        foreach (var season in seasons)
        {
            if (season.SeasonNumber == 0
                || string.Equals(season.Name, "Specials", StringComparison.Ordinal)
                || season.AirDate is null)
            {
                continue;
            }

            var difference = Math.Abs(season.AirDate.Value.DayNumber - sourceAirDate.Value.DayNumber);
            if (difference < minimum)
            {
                minimum = difference;
                selected = season;
            }
        }

        return selected is null || minimum > MaximumAirDateDifferenceDays
            ? Failed("tmdb_season_air_date_not_matched")
            : new TmdbSeasonResolutionResult(selected, null, minimum);
    }

    private static TmdbSeasonResolutionResult Failed(string code) =>
        new(
            null,
            new MetadataFailure(MetadataFailureKind.SemanticNoMatch, code, TmdbAccessConfirmed: true),
            null);
}

public sealed record TmdbSeasonResolutionResult(
    TmdbSeason? Value,
    MetadataFailure? Failure,
    int? AirDateDifferenceDays)
{
    public bool IsSuccess => Value is not null && Failure is null;
}
