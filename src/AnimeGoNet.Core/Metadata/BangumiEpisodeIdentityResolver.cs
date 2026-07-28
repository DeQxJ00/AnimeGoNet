using System.Globalization;

namespace AnimeGoNet.Core.Metadata;

public static class BangumiEpisodeIdentityResolver
{
    public static int? Resolve(
        IReadOnlyList<BangumiEpisode> episodes,
        string? sourceEpisode)
    {
        ArgumentNullException.ThrowIfNull(episodes);
        if (!decimal.TryParse(
                sourceEpisode?.Trim(),
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var number)
            || number <= 0
            || decimal.Truncate(number) != number)
        {
            return null;
        }

        var matches = episodes
            .Where(episode =>
                episode.Id > 0
                && episode.Type == 0
                && episode.EpisodeNumber == number)
            .Select(episode => episode.Id)
            .Distinct()
            .Take(2)
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }
}
