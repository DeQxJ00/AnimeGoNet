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
            "live-log-http-scope",
            "live-log-outbound-quick",
            "live-log-event-id",
            "live-log-from",
            "live-log-to",
            "live-log-exception-only",
            "live-log-auto-scroll",
            "live-log-wrap",
            "live-log-copy",
        })
        {
            Assert.Contains($"id=\"{id}\"", html, StringComparison.Ordinal);
        }
        Assert.Contains("详细日志筛选", html, StringComparison.Ordinal);
        Assert.Contains("id=\"ai-log-filters\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"ai-log-list\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"ai-log-error-category\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"ai-log-output-format-total\"", html, StringComparison.Ordinal);
        Assert.Contains("AI 返回 JSON / 结构错误", html, StringComparison.Ordinal);
        Assert.Contains("AI 调用日志", html, StringComparison.Ordinal);
        Assert.Contains("/api/v1/logs/ai-invocations", script, StringComparison.Ordinal);
        Assert.Contains("loadAiInvocationLogs", script, StringComparison.Ordinal);
        Assert.Contains("aiLogErrorCategoryLabel", script, StringComparison.Ordinal);
        Assert.Contains("validated_episodes", script, StringComparison.Ordinal);
        Assert.Contains("TMDB 最终验证 EP", script, StringComparison.Ordinal);
        Assert.Contains("AI 触发原因", script, StringComparison.Ordinal);
        Assert.Contains("metadataAttemptFileReason", script, StringComparison.Ordinal);
        Assert.Contains("引起阻塞的文件", script, StringComparison.Ordinal);
        Assert.Contains("受同批阻塞影响的文件", script, StringComparison.Ordinal);
        Assert.Contains("ai_trigger_reason", script, StringComparison.Ordinal);
        Assert.Contains("error_category", script, StringComparison.Ordinal);
        Assert.Contains("id=\"ai-debug-dialog\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"configuration-ai-debug\"", html, StringComparison.Ordinal);
        Assert.Contains("openAiDebugChain", script, StringComparison.Ordinal);
        Assert.Contains("AI 前置链路", script, StringComparison.Ordinal);
        Assert.Contains("原始 Prompt 模板", script, StringComparison.Ordinal);
        Assert.Contains("最终渲染 Prompt", script, StringComparison.Ordinal);
        Assert.Contains("copyVisibleLiveLogs", script, StringComparison.Ordinal);
        Assert.Contains("maximumRenderedLogs = 500", script, StringComparison.Ordinal);
        Assert.Contains("parseLiveLogEntry", parser, StringComparison.Ordinal);
        Assert.Contains("filterLiveLogEntries", parser, StringComparison.Ordinal);
        Assert.Contains("classifyLiveLogHttpDirection", parser, StringComparison.Ordinal);
        Assert.Contains("外部 HTTP（Mikan / TMDB / Bangumi 等）", html, StringComparison.Ordinal);
        Assert.Contains("仅外部 HTTP 请求（Mikan / TMDB / Bangumi 等）", html, StringComparison.Ordinal);
        Assert.Contains("data-log-http-scope=\"outbound\"", html, StringComparison.Ordinal);
        Assert.Contains("outboundQuick.setAttribute(\"aria-pressed\"", script, StringComparison.Ordinal);
        Assert.Contains("仅 WebUI / API 入站", html, StringComparison.Ordinal);
        Assert.Contains("排除 HTTP 连接日志", html, StringComparison.Ordinal);
        Assert.DoesNotContain("innerHTML", script, StringComparison.Ordinal);
        Assert.DoesNotContain("innerHTML", parser, StringComparison.Ordinal);
    }
}
