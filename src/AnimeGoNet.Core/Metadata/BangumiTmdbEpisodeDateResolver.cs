namespace AnimeGoNet.Core.Metadata;

public enum BangumiTmdbEpisodeDateMatchKind
{
    NotApplicable,
    Matched,
    NoMatch,
    Ambiguous,
}

public enum TmdbEpisodeDateEvidenceKind
{
    BangumiEpisode,
    FilenameEpisodeNearestDate,
}

public sealed record BangumiTmdbEpisodeDateMatch(
    BangumiTmdbEpisodeDateMatchKind Kind,
    TmdbEpisode? Episode,
    string? FailureCode,
    TmdbEpisodeDateEvidenceKind? EvidenceKind,
    int? AirDateDifferenceDays = null)
{
    public bool IsApplicable => Kind != BangumiTmdbEpisodeDateMatchKind.NotApplicable;

    public bool IsSuccess => Kind == BangumiTmdbEpisodeDateMatchKind.Matched && Episode is not null;
}

public static class BangumiTmdbEpisodeDateResolver
{
    public const int PrimaryMaximumAirDateDifferenceDays = 1;
    public const int FilenameFallbackMaximumAirDateDifferenceDays = 7;

    public static BangumiTmdbEpisodeDateMatch Resolve(
        IReadOnlyList<BangumiEpisode> bangumiEpisodes,
        IReadOnlyList<TmdbEpisode> tmdbEpisodes,
        int sourceEpisode,
        bool allowFilenameNearestFallback = false)
    {
        ArgumentNullException.ThrowIfNull(bangumiEpisodes);
        ArgumentNullException.ThrowIfNull(tmdbEpisodes);
        ArgumentOutOfRangeException.ThrowIfLessThan(sourceEpisode, 1);

        var evidence = SelectReferenceDate(bangumiEpisodes, sourceEpisode);
        if (evidence is null || tmdbEpisodes.All(value => value.AirDate is null))
        {
            return new BangumiTmdbEpisodeDateMatch(
                BangumiTmdbEpisodeDateMatchKind.NotApplicable,
                null,
                null,
                null);
        }

        var candidates = tmdbEpisodes
            .Where(value =>
                value.Id > 0
                && value.SeriesId > 0
                && value.SeasonNumber > 0
                && value.EpisodeNumber > 0
                && value.AirDate is not null)
            .Select(value => new EpisodeCandidate(
                value,
                Math.Abs(value.AirDate!.Value.DayNumber - evidence.Value.Date.DayNumber)))
            .OrderBy(value => value.Distance)
            .ThenBy(value => value.Episode.EpisodeNumber)
            .ThenBy(value => value.Episode.Id)
            .ToArray();
        var primary = SelectCandidate(
            candidates.Where(value => value.Distance <= PrimaryMaximumAirDateDifferenceDays).ToArray(),
            sourceEpisode,
            TmdbEpisodeDateEvidenceKind.BangumiEpisode,
            "tmdb_episode_bangumi_date_ambiguous");
        if (primary?.IsSuccess == true
            || (primary is not null && !allowFilenameNearestFallback))
        {
            return primary;
        }

        if (!allowFilenameNearestFallback)
        {
            return new BangumiTmdbEpisodeDateMatch(
                BangumiTmdbEpisodeDateMatchKind.NoMatch,
                null,
                "tmdb_episode_bangumi_date_not_found",
                evidence.Value.Kind);
        }

        if (candidates.Length == 0)
        {
            return new BangumiTmdbEpisodeDateMatch(
                BangumiTmdbEpisodeDateMatchKind.NoMatch,
                null,
                "tmdb_episode_bangumi_nearest_date_not_found",
                TmdbEpisodeDateEvidenceKind.FilenameEpisodeNearestDate);
        }

        var bestDistance = candidates[0].Distance;
        if (bestDistance > FilenameFallbackMaximumAirDateDifferenceDays)
        {
            return new BangumiTmdbEpisodeDateMatch(
                BangumiTmdbEpisodeDateMatchKind.NoMatch,
                null,
                "tmdb_episode_bangumi_nearest_date_too_distant",
                TmdbEpisodeDateEvidenceKind.FilenameEpisodeNearestDate,
                bestDistance);
        }

        var nearest = candidates.Where(value => value.Distance == bestDistance).ToArray();
        var sameNumber = nearest
            .Where(value => value.Episode.EpisodeNumber == sourceEpisode)
            .ToArray();
        if (sameNumber.Length == 0)
        {
            return new BangumiTmdbEpisodeDateMatch(
                BangumiTmdbEpisodeDateMatchKind.NoMatch,
                null,
                "tmdb_episode_bangumi_nearest_date_filename_mismatch",
                TmdbEpisodeDateEvidenceKind.FilenameEpisodeNearestDate,
                bestDistance);
        }

        if (sameNumber.Length > 1)
        {
            return new BangumiTmdbEpisodeDateMatch(
                BangumiTmdbEpisodeDateMatchKind.Ambiguous,
                null,
                "tmdb_episode_bangumi_nearest_date_ambiguous",
                TmdbEpisodeDateEvidenceKind.FilenameEpisodeNearestDate,
                bestDistance);
        }

        return new BangumiTmdbEpisodeDateMatch(
            BangumiTmdbEpisodeDateMatchKind.Matched,
            sameNumber[0].Episode,
            null,
            TmdbEpisodeDateEvidenceKind.FilenameEpisodeNearestDate,
            bestDistance);
    }

    private static BangumiTmdbEpisodeDateMatch? SelectCandidate(
        IReadOnlyList<EpisodeCandidate> candidates,
        int sourceEpisode,
        TmdbEpisodeDateEvidenceKind evidenceKind,
        string ambiguousCode)
    {
        if (candidates.Count == 0)
        {
            return null;
        }

        var bestDistance = candidates[0].Distance;
        var nearest = candidates.Where(value => value.Distance == bestDistance).ToArray();
        if (nearest.Length == 1)
        {
            return new BangumiTmdbEpisodeDateMatch(
                BangumiTmdbEpisodeDateMatchKind.Matched,
                nearest[0].Episode,
                null,
                evidenceKind,
                bestDistance);
        }

        var sameNumber = nearest
            .Where(value => value.Episode.EpisodeNumber == sourceEpisode)
            .ToArray();
        return sameNumber.Length == 1
            ? new BangumiTmdbEpisodeDateMatch(
                BangumiTmdbEpisodeDateMatchKind.Matched,
                sameNumber[0].Episode,
                null,
                evidenceKind,
                bestDistance)
            : new BangumiTmdbEpisodeDateMatch(
                BangumiTmdbEpisodeDateMatchKind.Ambiguous,
                null,
                ambiguousCode,
                evidenceKind,
                bestDistance);
    }

    private sealed record EpisodeCandidate(TmdbEpisode Episode, int Distance);

    private static (DateOnly Date, TmdbEpisodeDateEvidenceKind Kind)? SelectReferenceDate(
        IReadOnlyList<BangumiEpisode> episodes,
        int sourceEpisode)
    {
        var exact = episodes
            .Where(value =>
                value.Id > 0
                && value.Type == 0
                && (value.EpisodeNumber == sourceEpisode
                    || value.SortNumber == sourceEpisode)
                && value.AirDate is not null)
            .Select(value => value.AirDate!.Value)
            .Distinct()
            .ToArray();
        return exact.Length == 1
            ? (exact[0], TmdbEpisodeDateEvidenceKind.BangumiEpisode)
            : null;
    }
}
