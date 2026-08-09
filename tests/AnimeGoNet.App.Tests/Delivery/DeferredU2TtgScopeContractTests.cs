namespace AnimeGoNet.App.Tests.Delivery;

public sealed class DeferredU2TtgScopeContractTests
{
    [Fact]
    public void FirstReleaseDocsDeferU2AndTtgWithoutDeletingTheGenericScaffolding()
    {
        var root = RepositoryRoot();
        var todo = Read(root, "TODO.md");
        var readme = Read(root, "README.md");
        var routing = Read(root, "docs/SOURCE_ROUTING.md");
        var checklist = Read(root, "docs/PORTING_CHECKLIST.md");
        var decision = Read(root, "docs/verification/2026-08-09-u2-ttg-deferred.md");

        var decisionLine = Assert.Single(
            todo.Split('\n'),
            line => line.Contains("确认 U2/TTG 首版暂缓", StringComparison.Ordinal));
        Assert.StartsWith("- [x]", decisionLine.Trim(), StringComparison.Ordinal);
        Assert.DoesNotContain("确认 U2/TTG 默认文件策略", todo, StringComparison.Ordinal);

        Assert.Contains("正式输入源仅交付 Mikan", readme, StringComparison.Ordinal);
        Assert.Contains("U2/TTG 已由项目所有者确认为首版暂缓", readme, StringComparison.Ordinal);
        Assert.Contains("不生成默认 SourceProfile", routing, StringComparison.Ordinal);
        Assert.Contains("| 外部 U2/TTG 调用 | 保留的通用 source adapter/API/路由骨架 | 扩展 | 暂缓 |", checklist, StringComparison.Ordinal);
        Assert.Contains("| U2/TTG source adapter | 保留编译期扩展骨架 | 扩展 | 暂缓 |", checklist, StringComparison.Ordinal);
        Assert.Contains("Existing compile-time adapters", decision, StringComparison.Ordinal);

        var catalog = Read(root, "src/AnimeGoNet.Core/Plugins/BuiltInPluginCatalog.cs");
        Assert.Contains("new U2SourceAdapter()", catalog, StringComparison.Ordinal);
        Assert.Contains("new TtgSourceAdapter()", catalog, StringComparison.Ordinal);
        var defaults = Read(root, "src/AnimeGoNet.Core/Configuration/AnimeGoDefaults.cs");
        Assert.DoesNotContain("Adapter = \"u2\"", defaults, StringComparison.Ordinal);
        Assert.DoesNotContain("Adapter = \"ttg\"", defaults, StringComparison.Ordinal);
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
