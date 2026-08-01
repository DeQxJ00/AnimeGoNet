using AnimeGoNet.Core.Sources;

namespace AnimeGoNet.Core.Tests.Sources;

public sealed class SourceRssSchedulePolicyTests
{
    [Fact]
    public void NormalizesMikanUrlAndSixFieldCronWithoutRemovingSecretQuery()
    {
        const string value = " https://mikanani.me/RSS/MyBangumi?token=private-value ";

        var url = SourceRssSchedulePolicy.NormalizeFeedUrl("MIKAN", value);
        var cron = SourceRssSchedulePolicy.NormalizeCron(" 0 5/15 * * * ? ");

        Assert.Equal(
            "https://mikanani.me/RSS/MyBangumi?token=private-value",
            url);
        Assert.Equal("0 5/15 * * * ?", cron);
    }

    [Theory]
    [InlineData("u2", "https://u2.invalid/rss")]
    [InlineData("mikan", "file:///tmp/feed.xml")]
    [InlineData("mikan", "https://user:password@mikanani.me/rss")]
    [InlineData("mikan", "https://mikanani.me/rss#secret")]
    public void RejectsUnsupportedOrUnsafeFeedUrls(string adapter, string url)
    {
        Assert.Throws<ArgumentException>(() =>
            SourceRssSchedulePolicy.NormalizeFeedUrl(adapter, url));
    }

    [Fact]
    public void EnabledScheduleRequiresEnabledMikanSourceAndUrl()
    {
        Assert.Throws<ArgumentException>(() =>
            SourceRssSchedulePolicy.ValidateEnabled("mikan", false, true, "https://mikanani.me/rss"));
        Assert.Throws<ArgumentException>(() =>
            SourceRssSchedulePolicy.ValidateEnabled("u2", true, true, "https://u2.invalid/rss"));
        Assert.Throws<ArgumentException>(() =>
            SourceRssSchedulePolicy.ValidateEnabled("mikan", true, true, null));

        SourceRssSchedulePolicy.ValidateEnabled(
            "mikan",
            true,
            true,
            "https://mikanani.me/rss");
    }

    [Fact]
    public void InvalidCronIsReportedAsConfigurationArgumentFailure()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            SourceRssSchedulePolicy.NormalizeCron("not a cron"));

        Assert.DoesNotContain("private", exception.Message, StringComparison.Ordinal);
    }
}
