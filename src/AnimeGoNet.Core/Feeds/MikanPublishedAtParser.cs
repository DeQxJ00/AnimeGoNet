using System.Globalization;
using System.Text.RegularExpressions;

namespace AnimeGoNet.Core.Feeds;

public static partial class MikanPublishedAtParser
{
    public static DateTimeOffset? Parse(string? raw, TimeZoneInfo? sourceTimeZone = null)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var value = raw.Trim();
        if (HasExplicitOffset().IsMatch(value))
        {
            return DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var withOffset)
                ? withOffset
                : null;
        }

        if (!DateTime.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out var local))
        {
            return null;
        }

        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        var zone = sourceTimeZone ?? FindShanghaiTimeZone();
        if (zone.IsInvalidTime(local))
        {
            return null;
        }

        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    private static TimeZoneInfo FindShanghaiTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        }
    }

    [GeneratedRegex(
        "(?:[zZ]|[+-][0-9]{2}:?[0-9]{2}|\\bGMT|\\bUTC)\\s*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex HasExplicitOffset();
}
