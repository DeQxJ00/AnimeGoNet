namespace AnimeGoNet.App.Tests.WebUi;

public sealed class WorkspaceNavigationTests
{
    [Fact]
    public async Task StaticControlPlaneUsesPrimaryAndSecondaryWorkspaces()
    {
        await using var app = await RunningApp.StartAsync();

        var html = await app.Client.GetStringAsync("/");
        var script = await app.Client.GetStringAsync("/app.js");
        var styles = await app.Client.GetStringAsync("/styles.css");

        Assert.Contains("id=\"app-sidebar\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"workspace-tabs\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"sidebar-toggle\"", html, StringComparison.Ordinal);
        Assert.Contains(">Mikan 手动设置</button>", html, StringComparison.Ordinal);
        Assert.Contains("title: \"Mikan 手动设置\"", script, StringComparison.Ordinal);
        Assert.Contains(">Bangumi缓存</button>", html, StringComparison.Ordinal);
        Assert.Contains("title: \"Bangumi缓存\"", script, StringComparison.Ordinal);
        Assert.Contains("AnimeGoNetData 本地缓存使用记录", script, StringComparison.Ordinal);
        Assert.Contains("本地缓存逐条命中明细", html, StringComparison.Ordinal);
        Assert.Contains("/api/v1/data-update/archive-usage", script, StringComparison.Ordinal);
        Assert.Contains(
            "data-workspace=\"bangumi-cache\" data-subview=\"versions\"",
            html,
            StringComparison.Ordinal);
        Assert.Contains(">下载工具配置</button>", html, StringComparison.Ordinal);
        Assert.Contains("title: \"下载工具配置\"", script, StringComparison.Ordinal);
        Assert.Contains(">设置与备份</button>", html, StringComparison.Ordinal);
        Assert.Contains("title: \"设置与备份\"", script, StringComparison.Ordinal);
        Assert.Contains("id=\"manual-rss-manage-source\"", html, StringComparison.Ordinal);
        Assert.Contains("openSelectedMikanSourceSettings", script, StringComparison.Ordinal);
        Assert.DoesNotContain(">连接与配置</button>", html, StringComparison.Ordinal);
        Assert.Contains(">AI 匹配测试工具</button>", html, StringComparison.Ordinal);
        Assert.Contains("title: \"AI 匹配测试工具\"", script, StringComparison.Ordinal);
        Assert.Contains(">日志</button>", html, StringComparison.Ordinal);
        Assert.Contains("title: \"日志\"", script, StringComparison.Ordinal);
        Assert.Contains(
            "data-workspace=\"logs\" data-subview=\"runtime\"",
            html,
            StringComparison.Ordinal);
        Assert.Contains(
            "data-workspace=\"logs\" data-subview=\"ai-invocations\"",
            html,
            StringComparison.Ordinal);
        Assert.Contains(">系统缓存</button>", html, StringComparison.Ordinal);
        Assert.Contains("title: \"系统缓存\"", script, StringComparison.Ordinal);
        Assert.Contains("查看完整内容", script, StringComparison.Ordinal);
        Assert.Contains("id=\"cache-entry-dialog\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Mikan 自动化", html + script, StringComparison.Ordinal);
        foreach (var workspace in new[]
        {
            "overview",
            "library",
            "tasks",
            "mikan",
            "bangumi-cache",
            "download-tools",
            "connections",
            "tools",
            "logs",
            "system",
        })
        {
            Assert.Contains(
                $"data-workspace-target=\"{workspace}\"",
                html,
                StringComparison.Ordinal);
            Assert.Contains(
                $"data-workspace=\"{workspace}\"",
                html,
                StringComparison.Ordinal);
        }

        Assert.Contains("function initializeWorkspaceNavigation", script, StringComparison.Ordinal);
        Assert.Contains("function selectWorkspace", script, StringComparison.Ordinal);
        Assert.Contains("defaultSubview: \"metadata\"", script, StringComparison.Ordinal);
        var metadataTab = script.IndexOf(
            "{ id: \"metadata\", label: \"匹配与整理\" }",
            StringComparison.Ordinal);
        var downloadsTab = script.IndexOf(
            "{ id: \"downloads\", label: \"下载任务\" }",
            StringComparison.Ordinal);
        Assert.True(metadataTab >= 0 && downloadsTab > metadataTab);
        Assert.Contains("#/", script, StringComparison.Ordinal);
        Assert.Contains("#main-content > section[data-workspace]", script, StringComparison.Ordinal);
        Assert.Contains(".app-shell", styles, StringComparison.Ordinal);
        Assert.Contains(".app-sidebar.open", styles, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 900px)", styles, StringComparison.Ordinal);
        Assert.Contains("#main-content > section[hidden]", styles, StringComparison.Ordinal);
    }
}
