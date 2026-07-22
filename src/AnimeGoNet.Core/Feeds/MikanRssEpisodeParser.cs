using System.Globalization;
using System.Text.RegularExpressions;
using AnimeGoNet.Core.Metadata;

namespace AnimeGoNet.Core.Feeds;

public static partial class MikanRssEpisodeParser
{
    public static TorrentEpisodeCandidate Parse(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        var normalized = title.Replace('【', '[').Replace('】', ']');

        var special = SpecialToken().Match(normalized);
        if (special.Success)
        {
            var value = special.Groups[1].Success ? special.Groups[1].Value : special.Groups[2].Value;
            return new TorrentEpisodeCandidate(
                TorrentEpisodeCandidateKind.Special,
                value.ToLowerInvariant(),
                null,
                "special_episode");
        }

        Match? selected = null;
        foreach (Match match in EpisodeMarker().Matches(normalized))
        {
            selected = match;
        }

        if (selected is null)
        {
            return Unknown();
        }

        var raw = selected.Groups["bracket"].Success
            ? selected.Groups["bracket"].Value
            : selected.Groups["chinese"].Success
                ? selected.Groups["chinese"].Value
                : selected.Groups["explicit"].Success
                    ? selected.Groups["explicit"].Value
                    : selected.Groups["plain"].Value;
        if (raw.Contains('.', StringComparison.Ordinal)
            && decimal.TryParse(raw, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var fractional))
        {
            return new TorrentEpisodeCandidate(
                TorrentEpisodeCandidateKind.Fractional,
                fractional.ToString("0.################", CultureInfo.InvariantCulture),
                null,
                "fractional_episode");
        }

        if (int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var episode)
            && episode > 0)
        {
            return new TorrentEpisodeCandidate(
                TorrentEpisodeCandidateKind.Normal,
                episode.ToString(CultureInfo.InvariantCulture),
                episode,
                null);
        }

        return Unknown();
    }

    private static TorrentEpisodeCandidate Unknown() =>
        new(TorrentEpisodeCandidateKind.Unknown, null, null, "episode_not_parsed");

    [GeneratedRegex(
        @"(?:^|[\s._\-\[\(])((?:sp|special|ova|oad|pv|nced|ncop|menu)(?:\d{0,3})?)(?=$|[\s._\-\]\)])|\b(S00E\d+)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SpecialToken();

    [GeneratedRegex(
        @"\[(?<bracket>\d{1,4}(?:\.\d{1,6})?)(?:\s*[vV]\d+|\s*END)?\]|\[?第(?<chinese>\d{1,4})[话話集]\]?|\b(?:[Ee][Pp]?|Episode)\s*(?<explicit>\d{1,4}(?:\.\d{1,6})?)\b|(?:^|\s)-?\s*(?<plain>\d{1,4}(?:\.\d{1,6})?)(?=\s|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EpisodeMarker();
}
