namespace AnimeGoNet.App.Tests.Delivery;

public sealed class UnverifiedDeliveryStatusContractTests
{
    [Fact]
    public void TodoSeparatesGeneratedUnverifiedGatesFromCompletedImplementation()
    {
        var root = RepositoryRoot();
        var todo = File.ReadAllText(Path.Combine(root, "TODO.md"));

        Assert.Contains("`[~]` 功能/门禁已生成但尚未完成全部验证", todo, StringComparison.Ordinal);
        var unverified = todo
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("- [~]", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(6, unverified.Length);
        Assert.All(unverified, line => Assert.Contains("未验证", line, StringComparison.Ordinal));

        AssertUnverified(unverified, "优雅退出和取消传播");
        AssertUnverified(unverified, "Docker NativeAOT 双架构功能");
        AssertUnverified(unverified, "Linux x64/arm64 NativeAOT");
        AssertUnverified(unverified, "五 RID NativeAOT artifact");
        AssertUnverified(unverified, "首个可用预发布自动化");
        AssertUnverified(unverified, "AnimeGoNetData 不可变 Release");

        AssertCompleted(todo, "移植 Mikan：");
        AssertCompleted(todo, "移植 feed → filter → parse → download pipeline");
        AssertCompleted(todo, "`move` 安全编排");
        AssertCompleted(todo, "多文件 Torrent 逐文件去重");
        AssertCompleted(todo, "多文件任务逐集验证 TMDB Episode");
        AssertCompleted(todo, "TypeScript 7 strict 类型检查");
        AssertCompleted(todo, "Linux Go 容器基线 job");
    }

    [Fact]
    public void PortingChecklistDistinguishesVerifiedX64DockerFromPendingArchitectures()
    {
        var root = RepositoryRoot();
        var checklist = File.ReadAllText(Path.Combine(root, "docs", "PORTING_CHECKLIST.md"));

        Assert.Contains("`未验证` 表示功能/门禁已生成", checklist, StringComparison.Ordinal);
        Assert.Contains("| Docker 路径映射 | `/data`、`/download/incomplete`、`/download/anime` | 扩展 | 已验证 |", checklist, StringComparison.Ordinal);
        Assert.Contains("| Go Dockerfile | NativeAOT runtime image | 替换 | 未验证 |", checklist, StringComparison.Ordinal);
        Assert.Contains("| Go release workflows | .NET 10 build/test | 替换 | 未验证 |", checklist, StringComparison.Ordinal);
        Assert.Contains("| `internal/client/qbittorrent` | 多命名 qBittorrent adapter | 保留+扩展 | 已验证 |", checklist, StringComparison.Ordinal);
        Assert.Contains("合法 WebSeed 下载→Bangumi/TMDB→move/NFO/sidecar→API/WebUI→qB 清理的全链门禁", checklist, StringComparison.Ordinal);
        Assert.Contains("Ubuntu 24.04 x86_64 CT 已实际验证", checklist, StringComparison.Ordinal);
        Assert.Contains("linux-arm64", checklist, StringComparison.Ordinal);
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
            "Dockerfile.container-e2e-fixture",
            "Dockerfile.external-plugin-fixture",
            "docker-compose.animegonet.yml",
            "docker-compose.external-qbittorrent.yml",
            "docker-compose.qbittorrent-integration.yml",
            "eng/capture-upstream-go-baseline.sh",
            "eng/export-external-plugin-fixture.sh",
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
