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
        Assert.DoesNotContain(">Mikan 手动设置</button>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-workspace-target=\"bangumi-cache\"", html, StringComparison.Ordinal);
        Assert.Contains("{ id: \"bangumi\", label: \"Bangumi缓存\" }", script, StringComparison.Ordinal);
        Assert.Contains("{ id: \"anidb\", label: \"AniDB缓存\" }", script, StringComparison.Ordinal);
        Assert.Contains("{ id: \"other\", label: \"其他缓存管理\" }", script, StringComparison.Ordinal);
        Assert.Contains("AnimeGoNetData 本地缓存使用记录", script, StringComparison.Ordinal);
        Assert.Contains("本地缓存逐条命中明细", html, StringComparison.Ordinal);
        Assert.Contains("/api/v1/data-update/archive-usage", script, StringComparison.Ordinal);
        Assert.Contains(
            "data-workspace=\"system\" data-subview=\"bangumi\"",
            html,
            StringComparison.Ordinal);
        Assert.Contains(">下载工具配置</button>", html, StringComparison.Ordinal);
        Assert.Contains("title: \"下载工具配置\"", script, StringComparison.Ordinal);
        Assert.Contains(">输入源</button>", html, StringComparison.Ordinal);
        Assert.Contains("title: \"输入源\"", script, StringComparison.Ordinal);
        Assert.Contains(
            "data-workspace=\"sources\" data-subview=\"manage\"",
            html,
            StringComparison.Ordinal);
        Assert.Contains(
            "data-workspace=\"sources\" data-subview=\"mikan-ingest\"",
            html,
            StringComparison.Ordinal);
        Assert.Contains(
            "data-nav-label=\"Mikan 手动设置\"",
            html,
            StringComparison.Ordinal);
        Assert.Contains(
            "id: \"mikan\"",
            script,
            StringComparison.Ordinal);
        Assert.Contains("children: [", script, StringComparison.Ordinal);
        foreach (var child in new[]
        {
            "mikan-ingest\", label: \"手动设置",
            "mikan-manual-rules\", label: \"人工规则",
            "mikan-offsets\", label: \"可信 Offset",
            "mikan-candidate-rules\", label: \"候选规则",
            "mikan-legacy-filter\", label: \"五级过滤",
        })
        {
            Assert.Contains($"{{ id: \"{child}\" }}", script, StringComparison.Ordinal);
        }
        Assert.Contains("secondary-menu-group", script, StringComparison.Ordinal);
        Assert.Contains("tertiary-navigation", script, StringComparison.Ordinal);
        Assert.Contains("aria-expanded", script, StringComparison.Ordinal);
        Assert.Contains(">插件</button>", html, StringComparison.Ordinal);
        Assert.Contains("title: \"插件\"", script, StringComparison.Ordinal);
        Assert.Contains(
            "data-workspace=\"plugins\" data-subview=\"internal\"",
            html,
            StringComparison.Ordinal);
        Assert.Contains(
            "data-workspace=\"plugins\" data-subview=\"external\"",
            html,
            StringComparison.Ordinal);
        Assert.Contains(
            "{ id: \"internal\", label: \"内部插件\" }",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "{ id: \"external\", label: \"外部插件\" }",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "Web API / AnimeGoHelper (Mikan) 油猴插件",
            html,
            StringComparison.Ordinal);
        Assert.Contains(">设置与备份</button>", html, StringComparison.Ordinal);
        Assert.Contains("title: \"设置与备份\"", script, StringComparison.Ordinal);
        Assert.Contains(
            "{ id: \"webui\", label: \"WebUI 鉴权\" }",
            script,
            StringComparison.Ordinal);
        Assert.Contains("data-nav-label=\"WebUI 鉴权\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "{ id: \"sources\", label: \"输入源\" }",
            script,
            StringComparison.Ordinal);
        Assert.Contains("id=\"manual-rss-manage-source\"", html, StringComparison.Ordinal);
        Assert.Contains("openSelectedMikanSourceSettings", script, StringComparison.Ordinal);
        Assert.DoesNotContain(">连接与配置</button>", html, StringComparison.Ordinal);
        Assert.Contains(">AI 匹配测试</button>", html, StringComparison.Ordinal);
        Assert.Contains("title: \"AI 匹配测试\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("id: \"ai-subtitle\", label: \"AI 字幕匹配\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"ai-subtitle-test\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-workspace-target=\"logs\"", html, StringComparison.Ordinal);
        Assert.Contains(
            "data-workspace=\"tasks\" data-subview=\"runtime\"",
            html,
            StringComparison.Ordinal);
        Assert.Contains(
            "data-workspace=\"tasks\" data-subview=\"ai-invocations\"",
            html,
            StringComparison.Ordinal);
        Assert.Contains(">缓存</button>", html, StringComparison.Ordinal);
        Assert.Contains("title: \"缓存\"", script, StringComparison.Ordinal);
        Assert.Contains("查看完整内容", script, StringComparison.Ordinal);
        Assert.Contains("id=\"cache-entry-dialog\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Mikan 自动化", html + script, StringComparison.Ordinal);
        foreach (var workspace in new[]
        {
            "overview",
            "library",
            "tasks",
            "sources",
            "download-tools",
            "plugins",
            "connections",
            "tools",
            "notifications",
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
        var matchingLogTab = script.IndexOf(
            "{ id: \"matching\", label: \"匹配日志\" }",
            StringComparison.Ordinal);
        var aiLogTab = script.IndexOf(
            "{ id: \"ai-invocations\", label: \"AI 调用日志\" }",
            StringComparison.Ordinal);
        var runtimeLogTab = script.IndexOf(
            "{ id: \"runtime\", label: \"运行日志\" }",
            StringComparison.Ordinal);
        Assert.True(
            metadataTab >= 0
            && downloadsTab > metadataTab
            && matchingLogTab > downloadsTab
            && aiLogTab > matchingLogTab
            && runtimeLogTab > aiLogTab);
        Assert.Contains("rawWorkspace === \"logs\" ? \"tasks\"", script, StringComparison.Ordinal);
        Assert.Contains("window.location.hash.startsWith(\"#/logs/\")", script, StringComparison.Ordinal);
        Assert.Contains("id=\"download-sort\"", html, StringComparison.Ordinal);
        Assert.Contains(">任务加入时间</option>", html, StringComparison.Ordinal);
        Assert.Contains("id=\"download-direction\"", html, StringComparison.Ordinal);
        Assert.Contains(">倒序（最新优先）</option>", html, StringComparison.Ordinal);
        Assert.Contains("sort: \"created\"", script, StringComparison.Ordinal);
        Assert.Contains("direction: \"desc\"", script, StringComparison.Ordinal);
        Assert.Contains("sort: downloadState.sort", script, StringComparison.Ordinal);
        Assert.Contains("#/", script, StringComparison.Ordinal);
        Assert.Contains("#main-content > section[data-workspace]", script, StringComparison.Ordinal);
        Assert.Contains(".app-shell", styles, StringComparison.Ordinal);
        Assert.Contains(".app-sidebar.open", styles, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 900px)", styles, StringComparison.Ordinal);
        Assert.Contains("#main-content > section[hidden]", styles, StringComparison.Ordinal);
    }
}
