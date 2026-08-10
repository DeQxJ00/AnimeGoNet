using System.Globalization;
using System.Text.RegularExpressions;

#pragma warning disable CA1859 // Kept behavior-compatible with the validated tester.

namespace AnimeGoNet.App.AiTesterCompat;

/// <summary>
/// C# port of assets/plugin/filter/Auto_Bangumi/raw_parser.py from wetor/AnimeGo.
/// It intentionally treats the parsed episode as a candidate rather than authoritative metadata.
/// </summary>
public static partial class AutoBangumiRawParser
{
    private static readonly IReadOnlyDictionary<string, int> ChineseNumberMap = new Dictionary<string, int>(StringComparer.Ordinal)
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
        ["十"] = 10
    };

    public static AutoBangumiParseResult Parse(string rawTitle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawTitle);

        string contentTitle = rawTitle.Trim().Replace('【', '[').Replace('】', ']');
        string group = GetGroup(contentTitle);
        Match titleMatch = TitleRegex().Match(contentTitle);
        string seasonInfo = "";
        string episodeInfo = "";
        string other = "";
        if (titleMatch.Success)
        {
            seasonInfo = titleMatch.Groups[1].Value.Trim();
            episodeInfo = titleMatch.Groups[2].Value.Trim();
            other = titleMatch.Groups[3].Value.Trim();
        }

        string processed = PrefixProcess(seasonInfo, group);
        (string rawName, string seasonRaw, int season) = SeasonProcess(processed);
        (string titleEn, string titleZh, string titleJp) = NameProcess(rawName);

        Match episodeMatch = EpisodeNumberRegex().Match(episodeInfo);
        int episode = episodeMatch.Success && int.TryParse(episodeMatch.Value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsedEpisode)
            ? parsedEpisode
            : 0;

        (string subtitle, string resolution, string source) = FindTags(other);
        return new AutoBangumiParseResult(
            titleEn,
            titleZh,
            titleJp,
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
        string[] parts = BracketSplitRegex().Split(name);
        return parts.Length > 1 ? parts[1] : "";
    }

    private static string PrefixProcess(string raw, string group)
    {
        raw = Regex.Replace(raw, "." + group + ".", "", RegexOptions.CultureInvariant);

        string processed = PrefixRegex().Replace(raw, "/");
        foreach (string item in processed.Split('/'))
        {
            if ((NewAnimeTagRegex().IsMatch(item) && item.Length <= 5) || RegionTagRegex().IsMatch(item))
            {
                raw = Regex.Replace(raw, "." + item + ".", "", RegexOptions.CultureInvariant);
            }
        }

        return raw;
    }

    private static (string Name, string SeasonRaw, int Season) SeasonProcess(string seasonInfo)
    {
        string normalized = SquareBracketRegex().Replace(seasonInfo, " ");
        MatchCollection seasons = SeasonRegex().Matches(normalized);
        if (seasons.Count == 0)
        {
            return (normalized, "", 0);
        }

        string name = SeasonRegex().Replace(normalized, "");
        string seasonRaw = "";
        int parsedSeason = 0;
        foreach (Match match in seasons)
        {
            seasonRaw = match.Value;
            if (seasonRaw.Contains("Season", StringComparison.Ordinal) || seasonRaw.Contains('S'))
            {
                string digits = seasonRaw.Replace("Season", "", StringComparison.Ordinal).Replace("S", "", StringComparison.Ordinal).Trim();
                if (int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out parsedSeason)) break;
            }
            else
            {
                string value = seasonRaw.Replace("第", "", StringComparison.Ordinal)
                    .Replace("季", "", StringComparison.Ordinal)
                    .Replace("期", "", StringComparison.Ordinal)
                    .Replace(" ", "", StringComparison.Ordinal);
                if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsedSeason) ||
                    ChineseNumberMap.TryGetValue(value, out parsedSeason))
                {
                    break;
                }
            }
        }

        return (name, seasonRaw, parsedSeason);
    }

    private static (string English, string Chinese, string Japanese) NameProcess(string value)
    {
        string name = RegionSuffixRegex().Replace(value.Trim(), "");
        List<string> parts = NameSplitRegex().Split(name).Where(item => item.Length > 0).ToList();
        if (parts.Count == 1)
        {
            if (name.Contains('_', StringComparison.Ordinal))
            {
                parts = name.Split('_').Where(item => item.Length > 0).ToList();
            }
            else if (name.Contains(" - ", StringComparison.Ordinal))
            {
                parts = name.Split('-').Where(item => item.Length > 0).ToList();
            }
        }

        if (parts.Count == 1)
        {
            List<string> words = parts[0].Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
            string? chinese = words.FirstOrDefault(word => ChineseWordRegex().IsMatch(word));
            if (chinese is not null)
            {
                words.Remove(chinese);
                parts = [chinese.Trim(), string.Join(' ', words).Trim()];
            }
        }

        string titleEn = "";
        string titleZh = "";
        string titleJp = "";
        foreach (string rawPart in parts)
        {
            string part = rawPart.Trim();
            if (titleJp.Length == 0 && JapaneseTitleRegex().IsMatch(part))
            {
                titleJp = part;
            }
            else if (titleZh.Length == 0 && ChineseTitleRegex().IsMatch(part))
            {
                titleZh = part;
            }
            else if (titleEn.Length == 0 && EnglishTitleRegex().IsMatch(part))
            {
                titleEn = part;
            }
        }

        return (titleEn, titleZh, titleJp);
    }

    private static (string Subtitle, string Resolution, string Source) FindTags(string other)
    {
        string subtitle = "";
        string resolution = "";
        string source = "";
        string normalized = TagBracketRegex().Replace(other, " ");
        foreach (string element in normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (SubtitleRegex().IsMatch(element)) subtitle = element;
            else if (ResolutionRegex().IsMatch(element)) resolution = element;
            else if (SourceRegex().IsMatch(element)) source = element;
        }

        subtitle = SubtitleSuffixRegex().Replace(subtitle, "");
        return (subtitle, resolution, source);
    }

    [GeneratedRegex(@"\d+", RegexOptions.CultureInvariant)]
    private static partial Regex EpisodeNumberRegex();

    [GeneratedRegex(@"(.*|\[.*])( -? \d+|\[\d+]|\[\d+.?[vV]\d{1}]|[第]\d+[话話集]|\[\d+.?END])(.*)", RegexOptions.CultureInvariant)]
    private static partial Regex TitleRegex();

    [GeneratedRegex(@"1080|720|2160|4K", RegexOptions.CultureInvariant)]
    private static partial Regex ResolutionRegex();

    [GeneratedRegex(@"B-Global|[Bb]aha|[Bb]ilibili|AT-X|Web", RegexOptions.CultureInvariant)]
    private static partial Regex SourceRegex();

    [GeneratedRegex(@"[简繁日字幕]|CH|BIG5|GB", RegexOptions.CultureInvariant)]
    private static partial Regex SubtitleRegex();

    [GeneratedRegex(@"[^\w\s\u4e00-\u9fff\u3040-\u309f\u30a0-\u30ff-]", RegexOptions.CultureInvariant)]
    private static partial Regex PrefixRegex();

    [GeneratedRegex(@"[\[\]]", RegexOptions.CultureInvariant)]
    private static partial Regex SquareBracketRegex();

    [GeneratedRegex(@"S\d{1,2}|Season \d{1,2}|[第].[季期]", RegexOptions.CultureInvariant)]
    private static partial Regex SeasonRegex();

    [GeneratedRegex(@"/|\s{2}|-\s{2}", RegexOptions.CultureInvariant)]
    private static partial Regex NameSplitRegex();

    [GeneratedRegex(@"[\u0800-\u4e00]{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex JapaneseTitleRegex();

    [GeneratedRegex(@"[\u4e00-\u9fa5]{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex ChineseTitleRegex();

    [GeneratedRegex(@"^[\u4e00-\u9fa5]{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex ChineseWordRegex();

    [GeneratedRegex(@"[a-zA-Z]{3,}", RegexOptions.CultureInvariant)]
    private static partial Regex EnglishTitleRegex();

    [GeneratedRegex(@"[(（]仅限港澳台地区[）)]", RegexOptions.CultureInvariant)]
    private static partial Regex RegionSuffixRegex();

    [GeneratedRegex(@"[\[\]()（）]", RegexOptions.CultureInvariant)]
    private static partial Regex TagBracketRegex();

    [GeneratedRegex(@"_MP4|_MKV", RegexOptions.CultureInvariant)]
    private static partial Regex SubtitleSuffixRegex();

    [GeneratedRegex(@"[\[\]]", RegexOptions.CultureInvariant)]
    private static partial Regex BracketSplitRegex();

    [GeneratedRegex(@"新番|月?番", RegexOptions.CultureInvariant)]
    private static partial Regex NewAnimeTagRegex();

    [GeneratedRegex(@"港澳台地区", RegexOptions.CultureInvariant)]
    private static partial Regex RegionTagRegex();
}

public sealed record AutoBangumiParseResult(
    string TitleEn,
    string TitleZh,
    string TitleJp,
    int Season,
    string SeasonRaw,
    int Episode,
    string Subtitle,
    string Group,
    string Resolution,
    string Source);
