using System.Globalization;
using System.Text.RegularExpressions;

namespace AnimeGoNet.Core.Metadata;

public sealed record AutoBangumiRawParseResult(
    string EnglishTitle,
    string ChineseTitle,
    string JapaneseTitle,
    int Season,
    string SeasonRaw,
    int Episode,
    string Subtitle,
    string Group,
    string Resolution,
    string Source);

/// <summary>
/// NativeAOT-safe compatibility port of
/// assets/plugin/filter/Auto_Bangumi/raw_parser.py from AnimeGo develop.
/// Safety policy intentionally belongs to <see cref="FileEpisodeCandidateResolver"/>.
/// </summary>
public static partial class AutoBangumiRawParser
{
    private static readonly Dictionary<string, int> ChineseNumbers =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["一"] = 1,
            ["二"] = 2,
            ["三"] = 3,
            ["四"] = 4,
            ["五"] = 5,
            ["六"] = 6,
            ["七"] = 7,
            ["八"] = 8,
            ["九"] = 9,
            ["十"] = 10,
        };

    private static readonly TimeSpan DynamicRegexTimeout = TimeSpan.FromSeconds(1);

    public static AutoBangumiRawParseResult Parse(string rawTitle)
    {
        ArgumentNullException.ThrowIfNull(rawTitle);
        var contentTitle = rawTitle.Trim()
            .Replace('【', '[')
            .Replace('】', ']');
        var group = GetGroup(contentTitle);

        var seasonInfo = string.Empty;
        var episodeInfo = string.Empty;
        var other = string.Empty;
        var titleMatch = TitlePattern().Match(contentTitle);
        if (titleMatch.Success)
        {
            seasonInfo = titleMatch.Groups[1].Value.Trim();
            episodeInfo = titleMatch.Groups[2].Value.Trim();
            other = titleMatch.Groups[3].Value.Trim();
        }

        var processedPrefix = ProcessPrefix(seasonInfo, group);
        var (rawName, seasonRaw, season) = ProcessSeason(processedPrefix);
        var (englishTitle, chineseTitle, japaneseTitle) = ProcessName(rawName);

        var episodeMatch = EpisodeNumber().Match(episodeInfo);
        var episode = episodeMatch.Success
            ? int.Parse(episodeMatch.Value, NumberStyles.None, CultureInfo.InvariantCulture)
            : 0;
        var (subtitle, resolution, source) = FindTags(other);
        return new AutoBangumiRawParseResult(
            englishTitle,
            chineseTitle,
            japaneseTitle,
            season,
            seasonRaw,
            episode,
            subtitle,
            group,
            resolution,
            source);
    }

    private static string GetGroup(string name)
    {
        var pieces = GroupSplit().Split(name);
        return pieces.Length > 1 ? pieces[1] : string.Empty;
    }

    private static string ProcessPrefix(string raw, string group)
    {
        raw = DynamicReplace(raw, $".{group}.");
        var processed = PrefixUnsupported().Replace(raw, "/");
        foreach (var argument in processed.Split('/'))
        {
            if (NewShowToken().IsMatch(argument) && argument.Length <= 5)
            {
                raw = DynamicReplace(raw, $".{argument}.");
            }
            else if (RegionToken().IsMatch(argument))
            {
                raw = DynamicReplace(raw, $".{argument}.");
            }
        }

        return raw;
    }

    private static (string Name, string SeasonRaw, int Season) ProcessSeason(string seasonInfo)
    {
        var nameSeason = Brackets().Replace(seasonInfo, " ");
        var seasons = SeasonPattern().Matches(nameSeason);
        if (seasons.Count == 0)
        {
            return (nameSeason, string.Empty, 0);
        }

        var name = SeasonPattern().Replace(nameSeason, string.Empty);
        var seasonRaw = string.Empty;
        var parsedSeason = 0;
        foreach (Match match in seasons)
        {
            seasonRaw = match.Value;
            if (SeasonArabicToken().IsMatch(seasonRaw))
            {
                parsedSeason = int.Parse(
                    SeasonArabicToken().Replace(seasonRaw, string.Empty),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture);
                break;
            }

            if (SeasonChineseToken().IsMatch(seasonRaw))
            {
                var value = SeasonChineseDecoration().Replace(seasonRaw, string.Empty);
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedSeason))
                {
                    parsedSeason = ChineseNumbers[value];
                    break;
                }
            }
        }

        return (name, seasonRaw, parsedSeason);
    }

    private static (string English, string Chinese, string Japanese) ProcessName(string rawName)
    {
        var name = RegionQualifier().Replace(rawName.Trim(), string.Empty);
        var pieces = NameSplit().Split(name).Where(value => value.Length > 0).ToList();
        if (pieces.Count == 1)
        {
            if (Underscore().IsMatch(name))
            {
                pieces = Underscore().Split(name).ToList();
            }
            else if (SpacedHyphen().IsMatch(name))
            {
                pieces = Hyphen().Split(name).ToList();
            }
        }

        if (pieces.Count == 1)
        {
            var words = pieces[0].Split(' ').ToList();
            foreach (var item in words.ToArray())
            {
                if (!ChineseNameStart().IsMatch(item))
                {
                    continue;
                }

                words.Remove(item);
                pieces = [item.Trim(), string.Join(" ", words).Trim()];
                break;
            }
        }

        var english = string.Empty;
        var chinese = string.Empty;
        var japanese = string.Empty;
        foreach (var item in pieces)
        {
            if (japanese.Length == 0 && JapaneseText().IsMatch(item))
            {
                japanese = item.Trim();
            }
            else if (chinese.Length == 0 && ChineseText().IsMatch(item))
            {
                chinese = item.Trim();
            }
            else if (english.Length == 0 && EnglishText().IsMatch(item))
            {
                english = item.Trim();
            }
        }

        return (english, chinese, japanese);
    }

    private static (string Subtitle, string Resolution, string Source) FindTags(string other)
    {
        var elements = TagBrackets().Replace(other, " ").Split(' ');
        var subtitle = string.Empty;
        var resolution = string.Empty;
        var source = string.Empty;
        foreach (var element in elements.Where(value => value.Length > 0))
        {
            if (SubtitleToken().IsMatch(element))
            {
                subtitle = element;
            }
            else if (ResolutionToken().IsMatch(element))
            {
                resolution = element;
            }
            else if (SourceToken().IsMatch(element))
            {
                source = element;
            }
        }

        subtitle = SubtitleContainer().Replace(subtitle, string.Empty);
        return (subtitle, resolution, source);
    }

    private static string DynamicReplace(string input, string pattern) =>
        Regex.Replace(input, pattern, string.Empty, RegexOptions.None, DynamicRegexTimeout);

    [GeneratedRegex(@"\d+", RegexOptions.CultureInvariant)]
    private static partial Regex EpisodeNumber();

    [GeneratedRegex(
        @"\A(.*|\[.*])( -? \d+|\[\d+\]|\[\d+.?[vV]\d{1}\]|[第]\d+[话話集]|\[\d+.?END\])(.*)",
        RegexOptions.CultureInvariant)]
    private static partial Regex TitlePattern();

    [GeneratedRegex(@"1080|720|2160|4K", RegexOptions.CultureInvariant)]
    private static partial Regex ResolutionToken();

    [GeneratedRegex(@"B-Global|[Bb]aha|[Bb]ilibili|AT-X|Web", RegexOptions.CultureInvariant)]
    private static partial Regex SourceToken();

    [GeneratedRegex(@"[简繁日字幕]|CH|BIG5|GB", RegexOptions.CultureInvariant)]
    private static partial Regex SubtitleToken();

    [GeneratedRegex(@"[^\w\s\u4e00-\u9fff\u3040-\u309f\u30a0-\u30ff-]")]
    private static partial Regex PrefixUnsupported();

    [GeneratedRegex(@"[\[\]]", RegexOptions.CultureInvariant)]
    private static partial Regex GroupSplit();

    [GeneratedRegex(@"新番|月?番", RegexOptions.CultureInvariant)]
    private static partial Regex NewShowToken();

    [GeneratedRegex(@"港澳台地区", RegexOptions.CultureInvariant)]
    private static partial Regex RegionToken();

    [GeneratedRegex(@"[\[\]]", RegexOptions.CultureInvariant)]
    private static partial Regex Brackets();

    [GeneratedRegex(@"S\d{1,2}|Season \d{1,2}|[第].[季期]", RegexOptions.CultureInvariant)]
    private static partial Regex SeasonPattern();

    [GeneratedRegex(@"Season|S", RegexOptions.CultureInvariant)]
    private static partial Regex SeasonArabicToken();

    [GeneratedRegex(@"[第 ].*[季期(部分)]|部分", RegexOptions.CultureInvariant)]
    private static partial Regex SeasonChineseToken();

    [GeneratedRegex(@"[第季期 ]", RegexOptions.CultureInvariant)]
    private static partial Regex SeasonChineseDecoration();

    [GeneratedRegex(@"[(（]仅限港澳台地区[）)]", RegexOptions.CultureInvariant)]
    private static partial Regex RegionQualifier();

    [GeneratedRegex(@"/|\s{2}|-\s{2}", RegexOptions.CultureInvariant)]
    private static partial Regex NameSplit();

    [GeneratedRegex(@"_", RegexOptions.CultureInvariant)]
    private static partial Regex Underscore();

    [GeneratedRegex(@" - {1}", RegexOptions.CultureInvariant)]
    private static partial Regex SpacedHyphen();

    [GeneratedRegex(@"-", RegexOptions.CultureInvariant)]
    private static partial Regex Hyphen();

    [GeneratedRegex(@"^[\u4e00-\u9fa5]{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex ChineseNameStart();

    [GeneratedRegex(@"[\u0800-\u4e00]{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex JapaneseText();

    [GeneratedRegex(@"[\u4e00-\u9fa5]{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex ChineseText();

    [GeneratedRegex(@"[a-zA-Z]{3,}", RegexOptions.CultureInvariant)]
    private static partial Regex EnglishText();

    [GeneratedRegex(@"[\[\]()（）]", RegexOptions.CultureInvariant)]
    private static partial Regex TagBrackets();

    [GeneratedRegex(@"_MP4|_MKV", RegexOptions.CultureInvariant)]
    private static partial Regex SubtitleContainer();
}
