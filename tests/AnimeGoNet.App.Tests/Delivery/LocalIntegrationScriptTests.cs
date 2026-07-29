namespace AnimeGoNet.App.Tests.Delivery;

public sealed class LocalIntegrationScriptTests
{
    [Fact]
    public async Task QbittorrentScriptRunsOnlyQbittorrentSandboxTests()
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

        Assert.Contains(
            "--filter 'FullyQualifiedName~QbittorrentSandboxTests'",
            script,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "ANIMEGONET_TMDB_INTEGRATION = '1'",
            script,
            StringComparison.Ordinal);
    }
}
