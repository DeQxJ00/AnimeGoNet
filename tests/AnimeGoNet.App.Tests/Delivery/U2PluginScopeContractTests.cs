namespace AnimeGoNet.App.Tests.Delivery;

public sealed class U2PluginScopeContractTests
{
    [Fact]
    public void ManualU2PluginIsDeliveredWithoutSiteAutomationOrImplicitDefaults()
    {
        var root = RepositoryRoot();
        var todo = Read(root, "TODO.md");
        var readme = Read(root, "README.md");
        var routing = Read(root, "docs/SOURCE_ROUTING.md");
        var checklist = Read(root, "docs/PORTING_CHECKLIST.md");

        Assert.Contains("inner_plugin_u2", todo, StringComparison.Ordinal);
        Assert.Contains("inner_plugin_u2", readme, StringComparison.Ordinal);
        Assert.Contains("不包含 U2 RSS 或站点自动抓取", readme, StringComparison.Ordinal);
        Assert.Contains("不生成默认 SourceProfile", routing, StringComparison.Ordinal);
        Assert.Contains("| 外部 U2 调用 | `inner_plugin_u2` 专用 API + AnimeGoHelper U2 油猴脚本 | 扩展 | 已验证 |", checklist, StringComparison.Ordinal);
        Assert.Contains("| U2 source adapter | 编译期 adapter + `inner_plugin_u2` 手动入口 | 扩展 | 已验证 |", checklist, StringComparison.Ordinal);

        var catalog = Read(root, "src/AnimeGoNet.Core/Plugins/BuiltInPluginCatalog.cs");
        Assert.Contains("new U2SourceAdapter()", catalog, StringComparison.Ordinal);
        var defaults = Read(root, "src/AnimeGoNet.Core/Configuration/AnimeGoDefaults.cs");
        Assert.DoesNotContain("Adapter = \"u2\"", defaults, StringComparison.Ordinal);
    }

    private static string Read(string root, string relativePath) =>
        File.ReadAllText(Path.Combine(
            root,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));
}
