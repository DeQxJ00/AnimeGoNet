using System.Globalization;
using System.Text.RegularExpressions;

namespace AnimeGoNet.Core.Metadata;

public sealed record FileEpisodeCandidateResolution(
    int? Episode,
    string Reason,
    AutoBangumiRawParseResult? CompatibilityResult)
{
    public bool IsCandidate => Episode is > 0;
}

public static partial class FileEpisodeCandidateResolver
{
    public static FileEpisodeCandidateResolution Resolve(
        string sourceAdapter,
        string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceAdapter);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (!string.Equals(sourceAdapter, "mikan", StringComparison.Ordinal))
        {
            return new FileEpisodeCandidateResolution(null, "source_not_mikan", null);
        }

        var basename = Path.GetFileNameWithoutExtension(relativePath);
        AutoBangumiRawParseResult parsed;
        try
        {
            parsed = AutoBangumiRawParser.Parse(basename);
        }
        catch (Exception exception) when (exception is
            ArgumentException
            or FormatException
            or OverflowException
            or KeyNotFoundException)
        {
            return new FileEpisodeCandidateResolution(null, "compatibility_parser_failed", null);
        }
        catch (RegexMatchTimeoutException)
        {
            return new FileEpisodeCandidateResolution(null, "compatibility_parser_timeout", null);
        }

        var seasonEpisodeCandidates = SeasonEpisodeMarker()
            .Matches(basename)
            .Select(match => int.Parse(
                match.Groups[2].Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture))
            .ToHashSet();
        if (seasonEpisodeCandidates.Count > 0)
        {
            var explicitEpisode = seasonEpisodeCandidates.Count == 1
                ? seasonEpisodeCandidates.Single()
                : 0;
            var otherMarkers = UpstreamIntegerMarker()
                .Matches(basename)
                .SelectMany(marker => marker.Groups.Cast<Group>().Skip(1))
                .Where(group => group.Success)
                .Select(group => int.TryParse(
                    group.Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var value)
                        ? value
                        : 0)
                .Where(value => value > 0)
                .ToHashSet();
            if (explicitEpisode <= 0
                || explicitEpisode is >= 1900 and <= 2100
                || explicitEpisode is 720 or 1080 or 2160 or 4320
                || NonFeatureToken().IsMatch(basename)
                || otherMarkers.Any(value => value != explicitEpisode))
            {
                return new FileEpisodeCandidateResolution(
                    null,
                    "ambiguous_episode_markers",
                    parsed);
            }

            return new FileEpisodeCandidateResolution(
                explicitEpisode,
                "accepted_season_episode_extension",
                parsed);
        }

        if (parsed.Episode <= 0)
        {
            return new FileEpisodeCandidateResolution(null, "upstream_episode_not_parsed", parsed);
        }

        if (parsed.Episode is >= 1900 and <= 2100)
        {
            return new FileEpisodeCandidateResolution(null, "year_like_episode", parsed);
        }

        if (parsed.Episode is 720 or 1080 or 2160 or 4320)
        {
            return new FileEpisodeCandidateResolution(null, "resolution_like_episode", parsed);
        }

        if (NonFeatureToken().IsMatch(basename))
        {
            return new FileEpisodeCandidateResolution(null, "non_feature_episode", parsed);
        }

        var candidates = new HashSet<int>();
        foreach (Match marker in UpstreamIntegerMarker().Matches(basename))
        {
            for (var group = 1; group < marker.Groups.Count; group++)
            {
                if (marker.Groups[group].Success
                    && int.TryParse(
                        marker.Groups[group].Value,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out var value)
                    && value > 0)
                {
                    candidates.Add(value);
                    break;
                }
            }
        }

        if (candidates.Count != 1 || !candidates.Contains(parsed.Episode))
        {
            return new FileEpisodeCandidateResolution(null, "ambiguous_episode_markers", parsed);
        }

        return new FileEpisodeCandidateResolution(parsed.Episode, "accepted", parsed);
    }

    [GeneratedRegex(
        @"(?:^|[\s._\-\[\(])(?:sp|special|ova|oad|pv|nced|ncop|menu|logo)(?:\d{0,3})?(?=$|[\s._\-\]\)])|S00E\d+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NonFeatureToken();

    [GeneratedRegex(
        @" -? (\d+)|\[(\d+)\]|\[(\d+).?[vV]\d{1}\]|[第](\d+)[话話集]|\[(\d+).?END\]",
        RegexOptions.CultureInvariant)]
    private static partial Regex UpstreamIntegerMarker();

    [GeneratedRegex(
        @"\b[Ss](0*[1-9]\d*)[Ee](\d{1,4})\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex SeasonEpisodeMarker();
}
