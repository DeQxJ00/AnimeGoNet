namespace AnimeGoNet.App.Tests.Delivery;

public sealed class VerifiedDocumentationStatusContractTests
{
    [Fact]
    public void CurrentGuidesRecordObservedX64ResultsWithoutPromotingPendingArchitectures()
    {
        var webUi = Read("docs/WEB_UI.md");
        var matrix = Read("docs/VERIFICATION_MATRIX.md");
        var operations = Read("docs/OPERATIONS.md");
        var upstream = Read("docs/UPSTREAM_BASELINE.md");

        Assert.Contains("Ubuntu 24.04 x86_64 CT", webUi, StringComparison.Ordinal);
        Assert.Contains("1/1", webUi, StringComparison.Ordinal);
        Assert.DoesNotContain("发布镜像 Playwright 待验收", matrix, StringComparison.Ordinal);
        Assert.Contains("linux-arm64", matrix, StringComparison.Ordinal);
        Assert.Contains("2026-08-11-ubuntu-ct-docker-validation.md", operations, StringComparison.Ordinal);
        Assert.DoesNotContain("当前状态明确为“未验证”", operations, StringComparison.Ordinal);
        Assert.Contains("3109 条事件", upstream, StringComparison.Ordinal);
        Assert.Contains("100 个上游 skip", upstream, StringComparison.Ordinal);
        Assert.DoesNotContain("已生成、未验证；首次实际 runner", upstream, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("docs/verification/2026-07-30-dual-qbittorrent-compose.md")]
    [InlineData("docs/verification/2026-08-08-container-runtime-hardening.md")]
    [InlineData("docs/verification/2026-08-09-external-plugin-container-delivery.md")]
    [InlineData("docs/verification/2026-08-09-upstream-plugin-fixture-closure.md")]
    public void HistoricalDeliveryReportsLinkTheLaterUbuntuVerification(string relativePath)
    {
        var report = Read(relativePath);

        Assert.Contains("2026-08-11", report, StringComparison.Ordinal);
        Assert.Contains("2026-08-11-ubuntu-ct-docker-validation.md", report, StringComparison.Ordinal);
    }

    private static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(
            RepositoryRoot(),
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
