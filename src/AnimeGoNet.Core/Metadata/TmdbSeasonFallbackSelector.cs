using System.Globalization;
using System.Text.RegularExpressions;

namespace AnimeGoNet.Core.Metadata;

public static partial class TmdbSeasonFallbackSelector
{
    public static TmdbSeason? SelectTitleSeason(string title, IReadOnlyList<TmdbSeason> seasons)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(seasons);
        var number = ParseSeasonNumber(title);
        return number is null
            ? null
            : seasons.FirstOrDefault(season => season.SeasonNumber == number.Value && season.SeasonNumber > 0);
    }

    public static TmdbSeason? SelectFirstSeason(IReadOnlyList<TmdbSeason> seasons)
    {
        ArgumentNullException.ThrowIfNull(seasons);
        return seasons.FirstOrDefault(season => season.SeasonNumber == 1);
    }

    public static int? ParseSeasonNumber(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        var english = EnglishSeason().Match(title);
        if (english.Success)
        {
            var value = english.Groups[1].Success ? english.Groups[1].Value : english.Groups[2].Value;
            return int.Parse(value, CultureInfo.InvariantCulture);
        }

        var chinese = ChineseSeason().Match(title);
        if (!chinese.Success)
        {
            return null;
        }

        if (int.TryParse(chinese.Groups[1].Value, CultureInfo.InvariantCulture, out var numeric))
        {
            return numeric;
        }

        return chinese.Groups[1].Value switch
        {
            "一" => 1,
            "二" or "两" => 2,
            "三" => 3,
            "四" => 4,
            "五" or "伍" => 5,
            "六" => 6,
            "七" => 7,
            "八" => 8,
            "九" => 9,
            "十" => 10,
            _ => null,
        };
    }

    [GeneratedRegex(@"(?:season\s*([0-9]{1,2})|([0-9]{1,2})(?:st|nd|rd|th)\s*season)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EnglishSeason();

    [GeneratedRegex(@"第?\s*([0-9]{1,2}|一|二|两|三|四|五|伍|六|七|八|九|十)\s*(?:季|期)", RegexOptions.CultureInvariant)]
    private static partial Regex ChineseSeason();
}
