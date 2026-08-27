using System.Globalization;
using System.Text.RegularExpressions;

namespace AnimeGoNet.Core.Metadata;

/// <summary>
/// U2's independent filename episode candidate policy.
///
/// This intentionally starts with the same rules as the Mikan resolver, but
/// lives in its own type so U2-specific parsing can evolve without changing
/// Mikan behavior.
/// </summary>
public sealed record U2FileEpisodeCandidateResolution(
    int? Episode,
    string Reason,
    AutoBangumiRawParseResult? CompatibilityResult)
{
    public bool IsCandidate => Episode is > 0;
}

public static partial class U2FileEpisodeCandidateResolver
{
    private const int MinimumWeakEpisodeNumber = 1;
    private const int MaximumWeakEpisodeNumber = 9999;

    public static U2FileEpisodeCandidateResolution Resolve(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

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
            return new U2FileEpisodeCandidateResolution(null, "compatibility_parser_failed", null);
        }
        catch (RegexMatchTimeoutException)
        {
            return new U2FileEpisodeCandidateResolution(null, "compatibility_parser_timeout", null);
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
                .Where(IsPlausibleWeakEpisodeNumber)
                .ToHashSet();
            if (explicitEpisode <= 0
                || explicitEpisode is >= 1900 and <= 2100
                || explicitEpisode is 720 or 1080 or 2160 or 4320
                || NonFeatureToken().IsMatch(basename)
                || otherMarkers.Any(value => value != explicitEpisode))
            {
                return new U2FileEpisodeCandidateResolution(
                    null,
                    "ambiguous_episode_markers",
                    parsed);
            }

            return new U2FileEpisodeCandidateResolution(
                explicitEpisode,
                "accepted_season_episode_extension",
                parsed);
        }

        if (parsed.Episode <= 0)
        {
            return new U2FileEpisodeCandidateResolution(null, "upstream_episode_not_parsed", parsed);
        }

        if (parsed.Episode is >= 1900 and <= 2100)
        {
            return new U2FileEpisodeCandidateResolution(null, "year_like_episode", parsed);
        }

        if (parsed.Episode is 720 or 1080 or 2160 or 4320)
        {
            return new U2FileEpisodeCandidateResolution(null, "resolution_like_episode", parsed);
        }

        if (NonFeatureToken().IsMatch(basename))
        {
            return new U2FileEpisodeCandidateResolution(null, "non_feature_episode", parsed);
        }

        var candidates = new HashSet<int>();
        var hasOutOfRangeMarker = false;
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
                    if (IsPlausibleWeakEpisodeNumber(value))
                    {
                        candidates.Add(value);
                    }
                    else
                    {
                        hasOutOfRangeMarker = true;
                    }

                    break;
                }
            }
        }

        if (candidates.Count == 1)
        {
            var candidate = candidates.Single();
            return candidate == parsed.Episode
                || !IsPlausibleWeakEpisodeNumber(parsed.Episode)
                    ? new U2FileEpisodeCandidateResolution(candidate, "accepted", parsed)
                    : new U2FileEpisodeCandidateResolution(null, "ambiguous_episode_markers", parsed);
        }

        if (candidates.Count == 0 && hasOutOfRangeMarker)
        {
            return new U2FileEpisodeCandidateResolution(null, "episode_number_out_of_range", parsed);
        }

        return new U2FileEpisodeCandidateResolution(null, "ambiguous_episode_markers", parsed);
    }

    private static bool IsPlausibleWeakEpisodeNumber(int value) =>
        value is >= MinimumWeakEpisodeNumber and <= MaximumWeakEpisodeNumber;

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
