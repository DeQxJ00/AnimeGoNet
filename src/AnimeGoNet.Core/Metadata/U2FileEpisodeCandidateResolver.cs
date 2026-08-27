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

        // U2 commonly uses a hash-prefixed episode marker (for example
        // "Astro Ganger #25").  This is intentionally U2-only; Mikan's
        // compatibility resolver keeps its original marker set unchanged.
        var hashEpisodeMatches = HashEpisodeMarker().Matches(basename);
        if (hashEpisodeMatches.Count > 0)
        {
            var hashEpisodes = hashEpisodeMatches
                .Select(match => int.Parse(
                    match.Groups["episode"].Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture))
                .ToHashSet();
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
            if (hashEpisodes.Count == 1
                && !NonFeatureToken().IsMatch(basename)
                && !otherMarkers.Any(value => !hashEpisodes.Contains(value)))
            {
                return new U2FileEpisodeCandidateResolution(
                    hashEpisodes.Single(),
                    "accepted_hash_episode_marker",
                    parsed);
            }

            return new U2FileEpisodeCandidateResolution(
                null,
                "ambiguous_episode_markers",
                parsed);
        }

        // U2 also uses an explicit EP marker in release names (for example
        // "Baka to Test ... EP03").  This is deliberately separate from
        // Mikan's compatibility parser.
        // Check U2's feature/creditless markers before the explicit EP rule;
        // otherwise names such as "Creditless ED ep 12" would be mistaken
        // for a regular episode merely because they contain "ep 12".
        if (NonFeatureToken().IsMatch(basename))
        {
            return new U2FileEpisodeCandidateResolution(null, "non_feature_episode", parsed);
        }

        var explicitEpisodeMatches = ExplicitEpisodeMarker().Matches(basename);
        if (explicitEpisodeMatches.Count > 0)
        {
            var explicitEpisodes = explicitEpisodeMatches
                .Select(match => int.Parse(
                    match.Groups["episode"].Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture))
                .ToHashSet();
            if (explicitEpisodes.Count == 1
                && explicitEpisodes.Single() is >= MinimumWeakEpisodeNumber and <= MaximumWeakEpisodeNumber)
            {
                return new U2FileEpisodeCandidateResolution(
                    explicitEpisodes.Single(),
                    "accepted_explicit_episode_marker",
                    parsed);
            }

            return new U2FileEpisodeCandidateResolution(
                null,
                "ambiguous_episode_markers",
                parsed);
        }

        // U2 releases also commonly put a bare episode number between the
        // title and the bracketed/parenthesized release metadata, e.g.
        // "GUNSLINGER GIRL 01 [兄妹 ...]" or "戦う司書 27 「世界の力」".
        // The narrow trailing-delimiter check keeps numbers in titles,
        // resolutions and years from becoming episode candidates.
        var standaloneEpisodeMatches = StandaloneEpisodeMarker().Matches(basename);
        if (standaloneEpisodeMatches.Count > 0)
        {
            var standaloneEpisodes = standaloneEpisodeMatches
                .Select(match => int.Parse(
                    match.Groups["episode"].Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture))
                .ToHashSet();
            if (standaloneEpisodes.Count == 1
                && !NonFeatureToken().IsMatch(basename))
            {
                var episode = standaloneEpisodes.Single();
                if (IsPlausibleWeakEpisodeNumber(episode)
                    && episode is not (>= 1900 and <= 2100)
                    && episode is not (720 or 1080 or 2160 or 4320))
                {
                    return new U2FileEpisodeCandidateResolution(
                        episode,
                        "accepted_standalone_episode_marker",
                        parsed);
                }
            }

            return new U2FileEpisodeCandidateResolution(
                null,
                "ambiguous_episode_markers",
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
        @"(?:^|[\s._\-\[\(])(?:sp|special|ova|oad|pv|nced|ncop|menu|logo|extra|drama|spot|endcard|creditless|映像特典|特典|ノンテロップ|メニュー)(?:\d{0,3})?(?=$|[\s._\-\]\)])|S00E\d+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NonFeatureToken();

    [GeneratedRegex(
        @"\s#(?<episode>\d{1,4})\s",
        RegexOptions.CultureInvariant)]
    private static partial Regex HashEpisodeMarker();

    [GeneratedRegex(
        @"(?<![A-Za-z0-9])(?:ep|episode)\s*(?<episode>\d{1,4})(?!\d)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExplicitEpisodeMarker();

    [GeneratedRegex(
        @"(?<![#A-Za-z0-9])(?<episode>\d{1,4})(?=\s*(?:\[|\(|「|【|$)|\.(?:\s*(?:第|[^\x00-\x7F])))",
        RegexOptions.CultureInvariant)]
    private static partial Regex StandaloneEpisodeMarker();

    [GeneratedRegex(
        @" -? (\d+)|\[(\d+)\]|\[(\d+).?[vV]\d{1}\]|[第](\d+)[话話集]|\[(\d+).?END\]",
        RegexOptions.CultureInvariant)]
    private static partial Regex UpstreamIntegerMarker();

    [GeneratedRegex(
        @"\b[Ss](0*[1-9]\d*)[Ee](\d{1,4})\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex SeasonEpisodeMarker();
}
