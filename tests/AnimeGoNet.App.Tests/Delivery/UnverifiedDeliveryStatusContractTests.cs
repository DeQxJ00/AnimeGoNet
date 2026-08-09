namespace AnimeGoNet.App.Tests.Delivery;

public sealed class UnverifiedDeliveryStatusContractTests
{
    [Fact]
    public void TodoSeparatesGeneratedUnverifiedGatesFromCompletedImplementation()
    {
        var root = RepositoryRoot();
        var todo = File.ReadAllText(Path.Combine(root, "TODO.md"));

        Assert.Contains("`[~]` 功能/门禁已生成但按用户要求未执行验证", todo, StringComparison.Ordinal);
        var unverified = todo
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("- [~]", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(8, unverified.Length);
        Assert.All(unverified, line => Assert.Contains("未验证", line, StringComparison.Ordinal));

        AssertUnverified(unverified, "Linux Go 容器基线 job");
        AssertUnverified(unverified, "qBittorrent 真实容器 smoke");
        AssertUnverified(unverified, "双实例容器统一导入门禁");
        AssertUnverified(unverified, "Docker NativeAOT 镜像功能");
        AssertUnverified(unverified, "非 root、PUID/PGID");
        AssertUnverified(unverified, "官方 Compose");
        AssertUnverified(unverified, "client.download_path");
        AssertUnverified(unverified, "发布镜像 Web UI Playwright E2E");

        AssertCompleted(todo, "移植 Mikan：");
        AssertCompleted(todo, "移植 feed → filter → parse → download pipeline");
        AssertCompleted(todo, "`move` 安全编排");
        AssertCompleted(todo, "多文件 Torrent 逐文件去重");
        AssertCompleted(todo, "多文件任务逐集验证 TMDB Episode");
        AssertCompleted(todo, "TypeScript 7 strict 类型检查");
    }

    [Fact]
    public void PortingChecklistUsesUnverifiedStatusWithoutClaimingDockerSuccess()
    {
        var root = RepositoryRoot();
        var checklist = File.ReadAllText(Path.Combine(root, "docs", "PORTING_CHECKLIST.md"));

        Assert.Contains("`未验证` 表示功能/门禁已生成", checklist, StringComparison.Ordinal);
        Assert.Contains("| Docker 路径映射 | `/data`、`/download/incomplete`、`/download/anime` | 扩展 | 未验证 |", checklist, StringComparison.Ordinal);
        Assert.Contains("| Go Dockerfile | NativeAOT runtime image | 替换 | 未验证 |", checklist, StringComparison.Ordinal);
        Assert.Contains("| Go release workflows | .NET 10 build/test | 替换 | 未验证 |", checklist, StringComparison.Ordinal);
        Assert.Contains("| `internal/client/qbittorrent` | 多命名 qBittorrent adapter | 保留+扩展 | 已验证 |", checklist, StringComparison.Ordinal);
        Assert.Contains("隔离双容器真实统一投递 smoke 已生成但按用户要求未验证", checklist, StringComparison.Ordinal);
        Assert.DoesNotContain("Docker runner 实跑待验收", checklist, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryUnverifiedDeliveryClaimHasItsGeneratedArtifact()
    {
        var root = RepositoryRoot();
        var paths = new[]
        {
            ".github/workflows/upstream-go-baseline.yml",
            ".github/workflows/animegonet-docker.yml",
            "Dockerfile.animegonet",
            "docker-compose.animegonet.yml",
            "docker-compose.external-qbittorrent.yml",
            "docker-compose.qbittorrent-integration.yml",
            "eng/capture-upstream-go-baseline.sh",
            "eng/smoke-container.sh",
            "eng/smoke-webui-container.sh",
            "eng/smoke-qbittorrent-compose.sh",
        };

        Assert.All(paths, relativePath => Assert.True(
            File.Exists(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar))),
            $"Generated unverified artifact is missing: {relativePath}"));
    }

    private static void AssertUnverified(IEnumerable<string> lines, string marker) =>
        Assert.Single(lines, line => line.Contains(marker, StringComparison.Ordinal));

    private static void AssertCompleted(string todo, string marker)
    {
        var line = Assert.Single(
            todo.Split('\n'),
            line => line.Contains(marker, StringComparison.Ordinal));
        Assert.StartsWith("- [x]", line.Trim(), StringComparison.Ordinal);
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));
}
