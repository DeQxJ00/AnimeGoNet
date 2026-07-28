namespace AnimeGoNet.Core.Metadata;

public static class BangumiPublicationEpisodeResolver
{
    public const int MaximumDistanceDays = 31;

    public static int? SelectClosest(
        IReadOnlyList<BangumiEpisode> episodes,
        DateTimeOffset publishedAt,
        int maximumDistanceDays = MaximumDistanceDays)
    {
        ArgumentNullException.ThrowIfNull(episodes);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumDistanceDays);

        var publishedDate = DateOnly.FromDateTime(publishedAt.DateTime);
        var candidates = episodes
            .Where(episode =>
                episode.Id > 0
                && episode.Type == 0
                && episode.AirDate is not null
                && episode.EpisodeNumber is > 0
                && decimal.Truncate(episode.EpisodeNumber.Value) == episode.EpisodeNumber.Value
                && episode.EpisodeNumber.Value <= int.MaxValue)
            .Select(episode => new Candidate(
                episode.Id,
                decimal.ToInt32(episode.EpisodeNumber!.Value),
                episode.AirDate!.Value,
                Math.Abs(episode.AirDate.Value.DayNumber - publishedDate.DayNumber)))
            .Where(candidate => candidate.DistanceDays <= maximumDistanceDays)
            .OrderBy(candidate => candidate.DistanceDays)
            .ThenBy(candidate => candidate.AirDate > publishedDate ? 1 : 0)
            .ThenBy(candidate => candidate.EpisodeNumber)
            .ThenBy(candidate => candidate.Id)
            .ToArray();

        return candidates.FirstOrDefault()?.EpisodeNumber;
    }

    private sealed record Candidate(
        int Id,
        int EpisodeNumber,
        DateOnly AirDate,
        int DistanceDays);
}
