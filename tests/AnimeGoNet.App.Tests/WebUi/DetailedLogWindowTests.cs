namespace AnimeGoNet.App.Tests.WebUi;

public sealed class DetailedLogWindowTests
{
    [Fact]
    public async Task StaticLogWorkspaceExposesStructuredBoundedControls()
    {
        await using var app = await RunningApp.StartAsync();

        var html = await app.Client.GetStringAsync("/");
        var script = await app.Client.GetStringAsync("/app.js");
        var parser = await app.Client.GetStringAsync("/log-view.js");

        foreach (var id in new[]
        {
            "live-log-search",
            "live-log-category",
            "live-log-event-id",
            "live-log-auto-scroll",
            "live-log-wrap",
            "live-log-copy",
        })
        {
            Assert.Contains($"id=\"{id}\"", html, StringComparison.Ordinal);
        }
        Assert.Contains("详细日志筛选", html, StringComparison.Ordinal);
        Assert.Contains("copyVisibleLiveLogs", script, StringComparison.Ordinal);
        Assert.Contains("maximumRenderedLogs = 500", script, StringComparison.Ordinal);
        Assert.Contains("parseLiveLogEntry", parser, StringComparison.Ordinal);
        Assert.Contains("filterLiveLogEntries", parser, StringComparison.Ordinal);
        Assert.DoesNotContain("innerHTML", script, StringComparison.Ordinal);
        Assert.DoesNotContain("innerHTML", parser, StringComparison.Ordinal);
    }
}
