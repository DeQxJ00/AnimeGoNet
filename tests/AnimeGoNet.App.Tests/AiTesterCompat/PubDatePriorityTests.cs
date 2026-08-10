using AnimeGoNet.App.AiTesterCompat;

namespace AnimeGoNet.App.Tests.AiTesterCompat;

public sealed class PubDatePriorityTests
{
    private static readonly TesterConfig EnabledConfig = new(
        "https://example.test", "key", "model", ApiMode.Responses, "medium", false, 30, null);

    [Theory]
    [InlineData("2023-01-24T21:02:56.558766")]
    [InlineData("2023-01-24T21:02:56+08:00")]
    [InlineData("2023-01-24T13:02:56Z")]
    public void ValidPubDateAndSingleTorrentEnableGate(string pubDate)
    {
        var input = new MatchRequestInput(
            "Title",
            [new MatchFileInput("Show - 04 .mkv", 1)],
            123,
            null,
            pubDate,
            1,
            true,
            4,
            true);

        PubDatePriorityGate gate = PubDatePriority.Evaluate(EnabledConfig, input);

        Assert.True(gate.UseBangumiPubDateFirst, gate.Reason);
        Assert.NotNull(gate.NormalizedPubDate);
    }

    [Theory]
    [InlineData(null, 123, true, 1, 4, true, "Mikan pubDate 为空")]
    [InlineData("not-a-date", 123, true, 1, 4, true, "格式无效")]
    [InlineData("2023-01-24T21:02:56", null, true, 1, 4, true, "bgmid 为空")]
    [InlineData("2023-01-24T21:02:56", 123, false, 1, 4, true, "BGM MCP 已关闭")]
    [InlineData("2023-01-24T21:02:56", 123, true, 2, 4, true, "不是单文件")]
    [InlineData("2023-01-24T21:02:56", 123, true, 1, null, true, "bgm_episode_candidate 为空")]
    [InlineData("2023-01-24T21:02:56", 123, true, 1, 4, false, "开关已关闭")]
    public void MissingGateConditionFallsBackToGeneralFlow(
        string? pubDate,
        int? bgmid,
        bool bgmEnabled,
        int torrentFileCount,
        int? bgmCandidate,
        bool priorityEnabled,
        string expectedReason)
    {
        TesterConfig config = EnabledConfig with { EnableBgmMcp = bgmEnabled };
        var input = new MatchRequestInput(
            "Title",
            [new MatchFileInput("E04.mkv", 1)],
            bgmid is null ? null : (long)bgmid.Value,
            null,
            pubDate,
            torrentFileCount,
            priorityEnabled,
            bgmCandidate,
            true);

        PubDatePriorityGate gate = PubDatePriority.Evaluate(config, input);

        Assert.False(gate.UseBangumiPubDateFirst);
        Assert.Contains(expectedReason, gate.Reason);
    }

    [Fact]
    public void NonMikanSourceCannotEnablePubDatePriority()
    {
        var input = new MatchRequestInput(
            "Title",
            [new MatchFileInput("Show - 04.mkv", 1)],
            123,
            null,
            "2023-01-24T21:02:56+08:00",
            1,
            true,
            4,
            false);

        PubDatePriorityGate gate = PubDatePriority.Evaluate(EnabledConfig, input);

        Assert.False(gate.UseBangumiPubDateFirst);
        Assert.Contains("不是 Mikan RSS 来源", gate.Reason);
    }

}
