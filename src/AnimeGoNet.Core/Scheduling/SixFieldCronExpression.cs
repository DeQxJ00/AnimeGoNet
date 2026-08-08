using System.Globalization;
using AnimeGoNet.Core.Diagnostics;

namespace AnimeGoNet.Core.Scheduling;

public sealed class CronExpressionException(string code, string message)
    : FormatException(message), IStableError
{
    public string Code { get; } = StableErrorCode.Require(code, nameof(code));

    public StableErrorSemantic Semantics => StableErrorSemantic.ParseFailed;
}

public sealed class SixFieldCronExpression
{
    private const int SearchYears = 8;

    private static readonly Dictionary<string, int> Months = new(StringComparer.OrdinalIgnoreCase)
    {
        ["JAN"] = 1,
        ["FEB"] = 2,
        ["MAR"] = 3,
        ["APR"] = 4,
        ["MAY"] = 5,
        ["JUN"] = 6,
        ["JUL"] = 7,
        ["AUG"] = 8,
        ["SEP"] = 9,
        ["OCT"] = 10,
        ["NOV"] = 11,
        ["DEC"] = 12,
    };

    private static readonly Dictionary<string, int> DaysOfWeek = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SUN"] = 0,
        ["MON"] = 1,
        ["TUE"] = 2,
        ["WED"] = 3,
        ["THU"] = 4,
        ["FRI"] = 5,
        ["SAT"] = 6,
    };

    private readonly CronField _seconds;
    private readonly CronField _minutes;
    private readonly CronField _hours;
    private readonly CronField _days;
    private readonly CronField _months;
    private readonly CronField _daysOfWeek;

    private SixFieldCronExpression(
        string expression,
        CronField seconds,
        CronField minutes,
        CronField hours,
        CronField days,
        CronField months,
        CronField daysOfWeek)
    {
        Expression = expression;
        _seconds = seconds;
        _minutes = minutes;
        _hours = hours;
        _days = days;
        _months = months;
        _daysOfWeek = daysOfWeek;
    }

    public string Expression { get; }

    public static SixFieldCronExpression Parse(string expression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        var normalized = ExpandDescriptor(expression.Trim());
        var fields = normalized.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 6)
        {
            throw Error(
                "cron_field_count_invalid",
                "Cron expression must contain second, minute, hour, day, month, and day-of-week fields.");
        }

        return new SixFieldCronExpression(
            expression.Trim(),
            ParseField(fields[0], 0, 59, null, false, false),
            ParseField(fields[1], 0, 59, null, false, false),
            ParseField(fields[2], 0, 23, null, false, false),
            ParseField(fields[3], 1, 31, null, true, false),
            ParseField(fields[4], 1, 12, Months, false, false),
            ParseField(fields[5], 0, 7, DaysOfWeek, true, true));
    }

    public DateTimeOffset? GetNextOccurrence(
        DateTimeOffset after,
        TimeZoneInfo? timeZone = null)
    {
        var zone = timeZone ?? TimeZoneInfo.Local;
        var threshold = after.ToUniversalTime();
        var localStart = TimeZoneInfo.ConvertTime(threshold, zone).Date;
        var maximumDate = localStart.AddYears(SearchYears);

        for (var date = localStart; date <= maximumDate; date = date.AddDays(1))
        {
            if (!_months.Contains(date.Month) || !MatchesDay(date))
            {
                continue;
            }

            DateTimeOffset? best = null;
            foreach (var hour in _hours.Values)
            {
                foreach (var minute in _minutes.Values)
                {
                    foreach (var second in _seconds.Values)
                    {
                        var local = DateTime.SpecifyKind(
                            date.AddHours(hour).AddMinutes(minute).AddSeconds(second),
                            DateTimeKind.Unspecified);
                        if (zone.IsInvalidTime(local))
                        {
                            continue;
                        }

                        if (zone.IsAmbiguousTime(local))
                        {
                            foreach (var offset in zone.GetAmbiguousTimeOffsets(local))
                            {
                                Select(new DateTimeOffset(local, offset).ToUniversalTime());
                            }
                        }
                        else
                        {
                            Select(new DateTimeOffset(
                                TimeZoneInfo.ConvertTimeToUtc(local, zone),
                                TimeSpan.Zero));
                        }

                        void Select(DateTimeOffset candidate)
                        {
                            if (candidate > threshold && (best is null || candidate < best))
                            {
                                best = candidate;
                            }
                        }
                    }
                }
            }

            if (best is not null)
            {
                return best;
            }
        }

        return null;
    }

    private bool MatchesDay(DateTime date)
    {
        var dayMatches = _days.Contains(date.Day);
        var dayOfWeekMatches = _daysOfWeek.Contains((int)date.DayOfWeek);
        if (_days.IsWildcard && _daysOfWeek.IsWildcard) return true;
        if (_days.IsWildcard) return dayOfWeekMatches;
        if (_daysOfWeek.IsWildcard) return dayMatches;
        return dayMatches || dayOfWeekMatches;
    }

    private static CronField ParseField(
        string text,
        int minimum,
        int maximum,
        IReadOnlyDictionary<string, int>? names,
        bool questionAllowed,
        bool normalizeSunday)
    {
        if (text.Contains('?', StringComparison.Ordinal) && !questionAllowed)
        {
            throw Error("cron_question_mark_invalid", "Question mark is only valid for day fields.");
        }

        var isWildcard = text.StartsWith('*') || text.StartsWith('?');
        var values = new SortedSet<int>();
        foreach (var component in text.Split(',', StringSplitOptions.None))
        {
            if (component.Length == 0)
            {
                throw Error("cron_component_empty", "Cron field contains an empty component.");
            }

            var slash = component.IndexOf('/');
            if (slash != component.LastIndexOf('/'))
            {
                throw Error("cron_step_invalid", "Cron field contains an invalid step.");
            }
            var rangeText = slash < 0 ? component : component[..slash];
            var step = slash < 0
                ? 1
                : ParseNumber(component[(slash + 1)..], 1, maximum - minimum + 1, null);

            int start;
            int end;
            if (rangeText is "*" or "?")
            {
                if (rangeText == "?" && !questionAllowed)
                {
                    throw Error("cron_question_mark_invalid", "Question mark is only valid for day fields.");
                }
                start = minimum;
                end = maximum;
            }
            else
            {
                var dash = rangeText.IndexOf('-');
                if (dash >= 0)
                {
                    if (dash != rangeText.LastIndexOf('-'))
                    {
                        throw Error("cron_range_invalid", "Cron field contains an invalid range.");
                    }
                    start = ParseNumber(rangeText[..dash], minimum, maximum, names);
                    end = ParseNumber(rangeText[(dash + 1)..], minimum, maximum, names);
                }
                else
                {
                    start = ParseNumber(rangeText, minimum, maximum, names);
                    end = slash < 0 ? start : maximum;
                }
            }

            if (start > end)
            {
                throw Error("cron_range_invalid", "Cron field range start must not exceed its end.");
            }
            for (var value = start; value <= end; value += step)
            {
                values.Add(normalizeSunday && value == 7 ? 0 : value);
            }
        }

        if (values.Count == 0)
        {
            throw Error("cron_field_empty", "Cron field does not select any value.");
        }
        return new CronField(values.ToArray(), isWildcard);
    }

    private static int ParseNumber(
        string value,
        int minimum,
        int maximum,
        IReadOnlyDictionary<string, int>? names)
    {
        if (names is not null && names.TryGetValue(value, out var named))
        {
            return named;
        }
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            || parsed < minimum
            || parsed > maximum)
        {
            throw Error("cron_value_invalid", "Cron field contains an invalid value.");
        }
        return parsed;
    }

    private static string ExpandDescriptor(string expression) =>
        expression.ToLowerInvariant() switch
        {
            "@yearly" or "@annually" => "0 0 0 1 1 *",
            "@monthly" => "0 0 0 1 * *",
            "@weekly" => "0 0 0 * * 0",
            "@daily" or "@midnight" => "0 0 0 * * *",
            "@hourly" => "0 0 * * * *",
            _ when expression.StartsWith('@') =>
                throw Error("cron_descriptor_invalid", "Cron descriptor is not supported."),
            _ => expression,
        };

    private static CronExpressionException Error(string code, string message) => new(code, message);

    private sealed record CronField(int[] Values, bool IsWildcard)
    {
        public bool Contains(int value) => Array.BinarySearch(Values, value) >= 0;
    }
}
