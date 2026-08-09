namespace AnimeGoNet.App.Tests.Delivery;

public sealed class LocalIntegrationScriptTests
{
    [Fact]
    public async Task QbittorrentScriptKeepsWriteFixtureExplicitAndNeverRunsTmdbTests()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));
        var scriptPath = Path.Combine(
            repositoryRoot,
            "eng",
            "qbittorrent-local-integration.ps1");
        Assert.True(File.Exists(scriptPath), $"qBittorrent integration script was not found: {scriptPath}");

        var script = await File.ReadAllTextAsync(scriptPath);

        Assert.Contains("[switch]$DispatchFixture", script, StringComparison.Ordinal);
        Assert.Contains("[switch]$DownloadFixture", script, StringComparison.Ordinal);
        Assert.Contains(
            "'FullyQualifiedName~QbittorrentSandboxTests'",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "'FullyQualifiedName~QbittorrentSandboxTests|FullyQualifiedName~QbittorrentDispatchFixtureTests'",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "'FullyQualifiedName~QbittorrentSandboxTests|FullyQualifiedName~QbittorrentLegalDownloadE2ETests'",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "$env:ANIMEGONET_QBIT_DISPATCH_FIXTURE = $(if ($DispatchFixture) { '1' } else { '0' })",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "$env:ANIMEGONET_QBIT_DOWNLOAD_FIXTURE = $(if ($DownloadFixture) { '1' } else { '0' })",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "tests\\fixtures\\animegonet-ci.torrent.b64",
            script,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "ANIMEGONET_TMDB_INTEGRATION = '1'",
            script,
            StringComparison.Ordinal);
        Assert.DoesNotContain("123456", script, StringComparison.Ordinal);

        string legalDownloadTest = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "tests",
            "AnimeGoNet.LocalIntegration.Tests",
            "QbittorrentLegalDownloadE2ETests.cs"));
        Assert.Contains("http://127.0.0.1:9/announce", legalDownloadTest, StringComparison.Ordinal);
        Assert.Contains("LoopbackFileServer", legalDownloadTest, StringComparison.Ordinal);
        Assert.Contains("LoopbackMultiFileServer", legalDownloadTest, StringComparison.Ordinal);
        Assert.Contains(
            "LegalMultiFileDownloadAppliesPrioritiesAndMovesAssociatedSubtitle",
            legalDownloadTest,
            StringComparison.Ordinal);
        Assert.Contains("download_wanted = 0", legalDownloadTest, StringComparison.Ordinal);
        Assert.Contains(".zh-Hans.forced.ass", legalDownloadTest, StringComparison.Ordinal);
        Assert.Contains("deleteFiles: false", legalDownloadTest, StringComparison.Ordinal);
        Assert.Contains("MediaOrganizationResult.FilesCompleted", legalDownloadTest, StringComparison.Ordinal);
        Assert.Contains("MediaOrganizationResult.CleanupCompleted", legalDownloadTest, StringComparison.Ordinal);
        Assert.DoesNotContain("TestSpace", legalDownloadTest, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("123456", legalDownloadTest, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MikanAuditIsExplicitSecretSafePausedAndAuditable()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));
        var script = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "eng",
            "mikan-live-audit.ps1"));
        var test = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "tests",
            "AnimeGoNet.LocalIntegration.Tests",
            "MikanLiveChainAuditTests.cs"));

        Assert.Contains("FullyQualifiedName~MikanLiveChainAuditTests", script, StringComparison.Ordinal);
        Assert.Contains("ANIMEGONET_MIKAN_LIVE_AUDIT", script, StringComparison.Ordinal);
        Assert.Contains("ANIMEGONET_AI_API_KEY", script, StringComparison.Ordinal);
        Assert.Contains("mikan-live-audit", script, StringComparison.Ordinal);
        Assert.Contains("[switch]$RealDownload", script, StringComparison.Ordinal);
        Assert.Contains("ANIMEGONET_MIKAN_REAL_DOWNLOAD", script, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("123456", script, StringComparison.Ordinal);
        Assert.Contains("WaitForPausedTaskAsync", test, StringComparison.Ordinal);
        Assert.Contains("deleteFiles: false", test, StringComparison.Ordinal);
        Assert.Contains("ListAttemptsAsync", test, StringComparison.Ordinal);
        Assert.Contains("AiUsageSummary", test, StringComparison.Ordinal);
        Assert.Contains("Assert.Equal(29, sourceCases.Length)", test, StringComparison.Ordinal);
        Assert.Contains("DownloadPreparationProcessor", test, StringComparison.Ordinal);
        Assert.Contains("MediaOrganizationProcessor", test, StringComparison.Ordinal);
        Assert.Contains("WaitForDownloadAsync", test, StringComparison.Ordinal);
        Assert.Contains("ReadMediaRelativePathsAsync", test, StringComparison.Ordinal);
        Assert.Contains("Path.GetRelativePath", test, StringComparison.Ordinal);
        Assert.DoesNotContain("exception.Message", test, StringComparison.Ordinal);
        Assert.Contains("WriteReportAtomicallyAsync", test, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-", test, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("123456", test, StringComparison.Ordinal);
    }
}
