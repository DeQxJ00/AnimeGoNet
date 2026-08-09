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
        Assert.DoesNotContain("Mikan 自动化", html + script, StringComparison.Ordinal);
        foreach (var workspace in new[]
        {
            "overview",
            "library",
            "tasks",
            "mikan",
            "connections",
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
        Assert.Contains("#/", script, StringComparison.Ordinal);
        Assert.Contains("#main-content > section[data-workspace]", script, StringComparison.Ordinal);
        Assert.Contains(".app-shell", styles, StringComparison.Ordinal);
        Assert.Contains(".app-sidebar.open", styles, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 900px)", styles, StringComparison.Ordinal);
        Assert.Contains("#main-content > section[hidden]", styles, StringComparison.Ordinal);
    }
}
