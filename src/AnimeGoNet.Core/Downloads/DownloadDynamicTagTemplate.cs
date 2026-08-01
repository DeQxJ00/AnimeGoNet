using System.Globalization;
using AnimeGoNet.Core.Configuration;

namespace AnimeGoNet.Core.Downloads;

public sealed record DownloadDynamicTagRenderResult(
    IReadOnlyList<string> Tags,
    string? FailureCode)
{
    public bool IsSuccess => FailureCode is null;
}

public static class DownloadDynamicTagTemplate
{
    public const int MaximumLength = 512;

    private static readonly string[] DateTokens =
    [
        "year",
        "quarter",
        "quarter_index",
        "quarter_name",
        "week",
        "week_name",
    ];

    private static readonly HashSet<string> KnownTokens =
        new(DateTokens.Append("ep"), StringComparer.Ordinal);

    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var template = value.Trim();
        if (template.Length > MaximumLength || template.Any(char.IsControl))
        {
            throw new ArgumentException(
                $"dynamic_tag_template must contain at most {MaximumLength} characters without control characters.");
        }

        ValidateTokens(template);
        var tagTemplates = template.Split(',', StringSplitOptions.TrimEntries);
        if (tagTemplates.Length is < 1 or > SourceDownloadPolicy.MaximumTagCount
            || tagTemplates.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                $"dynamic_tag_template must produce 1 to {SourceDownloadPolicy.MaximumTagCount} non-empty comma-separated tags.");
        }

        return template;
    }

    public static DownloadDynamicTagRenderResult Render(
        string? template,
        DateOnly? airDate,
        int? episodeNumber)
    {
        var normalized = Normalize(template);
        if (normalized is null)
        {
            return new DownloadDynamicTagRenderResult([], null);
        }

        if (DateTokens.Any(token => normalized.Contains("{" + token + "}", StringComparison.Ordinal))
            && airDate is null)
        {
            return new DownloadDynamicTagRenderResult([], "dynamic_tag_air_date_unavailable");
        }

        if (normalized.Contains("{ep}", StringComparison.Ordinal)
            && episodeNumber is null or <= 0)
        {
            return new DownloadDynamicTagRenderResult([], "dynamic_tag_episode_unavailable");
        }

        var rendered = normalized;
        if (airDate is not null)
        {
            var date = airDate.Value;
            var quarterIndex = (date.Month + 2) / 3;
            var quarterMonth = ((quarterIndex - 1) * 3) + 1;
            var quarterName = new[] { "冬", "春", "夏", "秋" }[quarterIndex - 1];
            var week = date.DayOfWeek == DayOfWeek.Sunday
                ? 7
                : (int)date.DayOfWeek;
            var weekName = new[]
            {
                "星期日", "星期一", "星期二", "星期三", "星期四", "星期五", "星期六",
            }[(int)date.DayOfWeek];
            rendered = rendered
                .Replace("{year}", date.Year.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace("{quarter}", quarterMonth.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace("{quarter_index}", quarterIndex.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace("{quarter_name}", quarterName, StringComparison.Ordinal)
                .Replace("{week}", week.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace("{week_name}", weekName, StringComparison.Ordinal);
        }

        if (episodeNumber is > 0)
        {
            rendered = rendered.Replace(
                "{ep}",
                episodeNumber.Value.ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal);
        }

        try
        {
            var tags = SourceDownloadPolicy.NormalizeTags(
                rendered.Split(',', StringSplitOptions.TrimEntries));
            return new DownloadDynamicTagRenderResult(tags, null);
        }
        catch (ArgumentException)
        {
            return new DownloadDynamicTagRenderResult([], "dynamic_tag_rendered_value_invalid");
        }
    }

    private static void ValidateTokens(string template)
    {
        for (var index = 0; index < template.Length; index++)
        {
            if (template[index] == '}')
            {
                throw new ArgumentException("dynamic_tag_template contains an unmatched closing brace.");
            }

            if (template[index] != '{')
            {
                continue;
            }

            var closing = template.IndexOf('}', index + 1);
            if (closing < 0)
            {
                throw new ArgumentException("dynamic_tag_template contains an unmatched opening brace.");
            }

            var token = template[(index + 1)..closing];
            if (!KnownTokens.Contains(token))
            {
                throw new ArgumentException(
                    $"dynamic_tag_template contains unsupported placeholder '{{{token}}}'.");
            }

            index = closing;
        }
    }
}
