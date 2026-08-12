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
}

public sealed record BangumiTmdbEpisodeDateMatch(
    BangumiTmdbEpisodeDateMatchKind Kind,
    TmdbEpisode? Episode,
    string? FailureCode,
    TmdbEpisodeDateEvidenceKind? EvidenceKind)
{
    public bool IsApplicable => Kind != BangumiTmdbEpisodeDateMatchKind.NotApplicable;

    public bool IsSuccess => Kind == BangumiTmdbEpisodeDateMatchKind.Matched && Episode is not null;
}

public static class BangumiTmdbEpisodeDateResolver
{
    public static BangumiTmdbEpisodeDateMatch Resolve(
        IReadOnlyList<BangumiEpisode> bangumiEpisodes,
        IReadOnlyList<TmdbEpisode> tmdbEpisodes,
        int sourceEpisode)
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
            .Select(value => new
            {
                Episode = value,
                Distance = Math.Abs(value.AirDate!.Value.DayNumber - evidence.Value.Date.DayNumber),
            })
            .Where(value => value.Distance == 0)
            .OrderBy(value => value.Distance)
            .ThenBy(value => value.Episode.EpisodeNumber)
            .ThenBy(value => value.Episode.Id)
            .ToArray();
        if (candidates.Length == 0)
        {
            return new BangumiTmdbEpisodeDateMatch(
                BangumiTmdbEpisodeDateMatchKind.NoMatch,
                null,
                "tmdb_episode_bangumi_date_not_found",
                evidence.Value.Kind);
        }

        var bestDistance = candidates[0].Distance;
        var nearest = candidates
            .Where(value => value.Distance == bestDistance)
            .ToArray();
        if (nearest.Length == 1)
        {
            return new BangumiTmdbEpisodeDateMatch(
                BangumiTmdbEpisodeDateMatchKind.Matched,
                nearest[0].Episode,
                null,
                evidence.Value.Kind);
        }

        var sameNumber = nearest
            .Where(value => value.Episode.EpisodeNumber == sourceEpisode)
            .ToArray();
        if (sameNumber.Length != 1)
        {
            return new BangumiTmdbEpisodeDateMatch(
                BangumiTmdbEpisodeDateMatchKind.Ambiguous,
                null,
                "tmdb_episode_bangumi_date_ambiguous",
                evidence.Value.Kind);
        }

        return new BangumiTmdbEpisodeDateMatch(
            BangumiTmdbEpisodeDateMatchKind.Matched,
            sameNumber[0].Episode,
            null,
            evidence.Value.Kind);
    }

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
