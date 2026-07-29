using AnimeGoNet.Core.Scheduling;

namespace AnimeGoNet.Core.Tests.Scheduling;

public sealed class SixFieldCronExpressionTests
{
    [Theory]
    [InlineData("*/15 * * * * ?", "2026-07-29T10:00:01Z", "2026-07-29T10:00:15Z")]
    [InlineData("0 0/20 * * * ?", "2026-07-29T10:00:00Z", "2026-07-29T10:20:00Z")]
    [InlineData("0 30 8 * JAN,MAR MON-FRI", "2026-01-02T08:30:00Z", "2026-01-05T08:30:00Z")]
    [InlineData("@daily", "2026-07-29T00:00:00Z", "2026-07-30T00:00:00Z")]
    public void CalculatesNextOccurrence(string expression, string after, string expected)
    {
        var cron = SixFieldCronExpression.Parse(expression);

        var result = cron.GetNextOccurrence(
            DateTimeOffset.Parse(after, System.Globalization.CultureInfo.InvariantCulture),
            TimeZoneInfo.Utc);

        Assert.Equal(
            DateTimeOffset.Parse(expected, System.Globalization.CultureInfo.InvariantCulture),
            result);
    }

    [Fact]
    public void RestrictedDayAndDayOfWeekUseCronOrSemantics()
    {
        var cron = SixFieldCronExpression.Parse("0 0 0 13 * FRI");

        var result = cron.GetNextOccurrence(
            DateTimeOffset.Parse(
                "2026-02-13T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            TimeZoneInfo.Utc);

        Assert.Equal(
            DateTimeOffset.Parse(
                "2026-02-20T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            result);
    }

    [Fact]
    public void HandlesLeapDayWithoutSecondBySecondSearch()
    {
        var cron = SixFieldCronExpression.Parse("0 0 0 29 2 ?");

        var result = cron.GetNextOccurrence(
            DateTimeOffset.Parse(
                "2025-03-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            TimeZoneInfo.Utc);

        Assert.Equal(
            DateTimeOffset.Parse(
                "2028-02-29T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            result);
    }

    [Fact]
    public void SkipsInvalidDstTimeAndOrdersBothAmbiguousOccurrences()
    {
        var zone = TestEasternTimeZone();
        var spring = SixFieldCronExpression.Parse("0 30 2 * * *");
        var fall = SixFieldCronExpression.Parse("0 30 1 * * *");

        Assert.Equal(
            DateTimeOffset.Parse(
                "2026-03-09T06:30:00Z", System.Globalization.CultureInfo.InvariantCulture),
            spring.GetNextOccurrence(
                DateTimeOffset.Parse(
                    "2026-03-08T06:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
                zone));
        Assert.Equal(
            DateTimeOffset.Parse(
                "2026-11-01T05:30:00Z", System.Globalization.CultureInfo.InvariantCulture),
            fall.GetNextOccurrence(
                DateTimeOffset.Parse(
                    "2026-11-01T05:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
                zone));
        Assert.Equal(
            DateTimeOffset.Parse(
                "2026-11-01T06:30:00Z", System.Globalization.CultureInfo.InvariantCulture),
            fall.GetNextOccurrence(
                DateTimeOffset.Parse(
                    "2026-11-01T05:30:00Z", System.Globalization.CultureInfo.InvariantCulture),
                zone));
    }

    [Theory]
    [InlineData("* * * * *", "cron_field_count_invalid")]
    [InlineData("? * * * * *", "cron_question_mark_invalid")]
    [InlineData("60 * * * * *", "cron_value_invalid")]
    [InlineData("*/0 * * * * *", "cron_value_invalid")]
    [InlineData("0 0 0 20-10 * *", "cron_range_invalid")]
    [InlineData("@every 5s", "cron_descriptor_invalid")]
    public void RejectsInvalidExpressionsWithStableCode(string expression, string code)
    {
        var exception = Assert.Throws<CronExpressionException>(
            () => SixFieldCronExpression.Parse(expression));

        Assert.Equal(code, exception.Code);
    }

    private static TimeZoneInfo TestEasternTimeZone()
    {
        var daylightStart = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
            new DateTime(1, 1, 1, 2, 0, 0),
            3,
            2,
            DayOfWeek.Sunday);
        var daylightEnd = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
            new DateTime(1, 1, 1, 2, 0, 0),
            11,
            1,
            DayOfWeek.Sunday);
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            new DateTime(2020, 1, 1),
            new DateTime(2030, 12, 31),
            TimeSpan.FromHours(1),
            daylightStart,
            daylightEnd);
        return TimeZoneInfo.CreateCustomTimeZone(
            "AnimeGoNet-Test-Eastern",
            TimeSpan.FromHours(-5),
            "Test Eastern",
            "Test Eastern Standard",
            "Test Eastern Daylight",
            [rule]);
    }
}
