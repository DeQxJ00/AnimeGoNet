using AnimeGoNet.Core.Feeds;

namespace AnimeGoNet.Core.Tests.Feeds;

public sealed class MikanPublishedAtParserTests
{
    [Fact]
    public void OffsetlessMikanTimestampUsesAsiaShanghai()
    {
        var value = MikanPublishedAtParser.Parse(
            "2026-07-22T12:34:56.123",
            TimeZoneInfo.CreateCustomTimeZone(
                "test-shanghai",
                TimeSpan.FromHours(8),
                "test-shanghai",
                "test-shanghai"));

        Assert.Equal(
            DateTimeOffset.Parse(
                "2026-07-22T12:34:56.123+08:00",
                System.Globalization.CultureInfo.InvariantCulture),
            value);
    }

    [Theory]
    [InlineData("Wed, 22 Jul 2026 12:34:56 GMT", "2026-07-22T12:34:56+00:00")]
    [InlineData("2026-07-22T12:34:56+09:00", "2026-07-22T12:34:56+09:00")]
    public void ExplicitOffsetIsPreserved(string raw, string expected)
    {
        Assert.Equal(
            DateTimeOffset.Parse(
                expected,
                System.Globalization.CultureInfo.InvariantCulture),
            MikanPublishedAtParser.Parse(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-date")]
    public void MissingOrInvalidTimestampReturnsNull(string? raw)
    {
        Assert.Null(MikanPublishedAtParser.Parse(raw));
    }
}
