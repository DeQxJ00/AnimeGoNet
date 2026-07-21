using System.Globalization;
using System.Text.RegularExpressions;

namespace AnimeGoNet.Core.Metadata;

public enum TorrentEpisodeCandidateKind
{
    Unknown,
    Normal,
    Fractional,
    Special,
}

public sealed record TorrentEpisodeCandidate(
    TorrentEpisodeCandidateKind Kind,
    string? SourceEpisode,
    int? NormalEpisode,
    string? Reason);

public static partial class TorrentEpisodeCandidateParser
{
    public static TorrentEpisodeCandidate Parse(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        var name = Path.GetFileNameWithoutExtension(relativePath)
            .Replace('【', '[')
            .Replace('】', ']');

        var special = SpecialToken().Match(name);
        if (special.Success)
        {
            var sourceEpisode = special.Groups[1].Success
                ? special.Groups[1].Value
                : special.Groups[2].Value;
            return new TorrentEpisodeCandidate(
                TorrentEpisodeCandidateKind.Special,
                sourceEpisode.ToLowerInvariant(),
                null,
                "special_episode");
        }

        var fractional = FractionalEpisode().Match(name);
        if (fractional.Success
            && decimal.TryParse(
                fractional.Groups[1].Value,
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var decimalEpisode))
        {
            return new TorrentEpisodeCandidate(
                TorrentEpisodeCandidateKind.Fractional,
                decimalEpisode.ToString("0.################", CultureInfo.InvariantCulture),
                null,
                "fractional_episode");
        }

        var normal = NormalEpisode().Match(name);
        if (normal.Success)
        {
            for (var group = 1; group < normal.Groups.Count; group++)
            {
                if (normal.Groups[group].Success
                    && int.TryParse(normal.Groups[group].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var episode)
                    && episode > 0)
                {
                    return new TorrentEpisodeCandidate(
                        TorrentEpisodeCandidateKind.Normal,
                        episode.ToString(CultureInfo.InvariantCulture),
                        episode,
                        null);
                }
            }
        }

        return new TorrentEpisodeCandidate(TorrentEpisodeCandidateKind.Unknown, null, null, "episode_not_parsed");
    }

    [GeneratedRegex(
        @"(?:^|[\s._\-\[\(])((?:sp|special|ova|oad|pv|nced|ncop|menu)(?:\d{0,3})?)(?=$|[\s._\-\]\)])|\b(S00E\d+)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SpecialToken();

    [GeneratedRegex(
        @"(?:\[|\b(?:ep?|episode)\s*|\s-\s)(\d{1,4}\.\d{1,6})(?=\s*(?:v\d+)?(?:\]|\b|$))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FractionalEpisode();

    [GeneratedRegex(
        @"\[(\d{1,4})(?:\s*[vV]\d+|\s*END)?\]|第(\d{1,4})[话話集]|\b[Ee][Pp]?(\d{1,4})\b|\s-\s(\d{1,4})(?=\s|$)",
        RegexOptions.CultureInvariant)]
    private static partial Regex NormalEpisode();
}
