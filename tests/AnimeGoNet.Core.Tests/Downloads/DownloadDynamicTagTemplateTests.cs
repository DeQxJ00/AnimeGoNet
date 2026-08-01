using AnimeGoNet.Core.Downloads;

namespace AnimeGoNet.Core.Tests.Downloads;

public sealed class DownloadDynamicTagTemplateTests
{
    [Fact]
    public void ReproducesUpstreamTagVariables()
    {
        var result = DownloadDynamicTagTemplate.Render(
            "{year}年{quarter}月新番,{quarter_index},{quarter_name}季,第{ep}集,周{week},{week_name}",
            new DateOnly(2022, 4, 11),
            10);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            ["2022年4月新番", "2", "春季", "第10集", "周1", "星期一"],
            result.Tags);
    }

    [Fact]
    public void MapsWinterAndSundayLikeUpstream()
    {
        var result = DownloadDynamicTagTemplate.Render(
            "{year}-{quarter}-{quarter_index}-{quarter_name}-{week}-{week_name}",
            new DateOnly(2026, 1, 4),
            null);

        Assert.Equal(["2026-1-1-冬-7-星期日"], result.Tags);
    }

    [Fact]
    public void ReportsMissingCanonicalInputsWithoutLeavingPlaceholders()
    {
        Assert.Equal(
            "dynamic_tag_air_date_unavailable",
            DownloadDynamicTagTemplate.Render("{year}新番", null, 1).FailureCode);
        Assert.Equal(
            "dynamic_tag_episode_unavailable",
            DownloadDynamicTagTemplate.Render("第{ep}集", new DateOnly(2026, 4, 1), null).FailureCode);
    }

    [Theory]
    [InlineData("{unknown}")]
    [InlineData("{year")]
    [InlineData("year}")]
    [InlineData("one,,two")]
    public void RejectsInvalidTemplates(string template)
    {
        Assert.Throws<ArgumentException>(() => DownloadDynamicTagTemplate.Normalize(template));
    }

    [Fact]
    public void EmptyTemplateDisablesDynamicTags()
    {
        Assert.Null(DownloadDynamicTagTemplate.Normalize("  "));
        Assert.Empty(DownloadDynamicTagTemplate.Render(null, null, null).Tags);
    }
}
