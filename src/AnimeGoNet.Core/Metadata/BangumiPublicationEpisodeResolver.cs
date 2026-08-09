namespace AnimeGoNet.Core.Metadata;

public static class BangumiPublicationEpisodeResolver
{
    public static int? SelectClosest(
        IReadOnlyList<BangumiEpisode> episodes,
        DateTimeOffset publishedAt)
    {
        ArgumentNullException.ThrowIfNull(episodes);

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
            .Where(candidate =>
                candidate.AirDate <= publishedDate)
            .OrderBy(candidate => candidate.DistanceDays)
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
