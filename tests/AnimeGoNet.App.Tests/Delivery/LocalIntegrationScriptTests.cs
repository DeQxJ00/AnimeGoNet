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
        Assert.Contains("deleteFiles: false", legalDownloadTest, StringComparison.Ordinal);
        Assert.Contains("MediaOrganizationResult.FilesCompleted", legalDownloadTest, StringComparison.Ordinal);
        Assert.Contains("MediaOrganizationResult.CleanupCompleted", legalDownloadTest, StringComparison.Ordinal);
        Assert.DoesNotContain("TestSpace", legalDownloadTest, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("123456", legalDownloadTest, StringComparison.Ordinal);
    }
}
