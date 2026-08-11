namespace AnimeGoNet.App.Tests;

public sealed class IntegrationHarnessContractTests
{
    [Fact]
    public void UbuntuCtHarnessIsIsolatedAndDoesNotPruneSharedDockerState()
    {
        var script = ReadRepositoryFile("eng", "docker-ubuntu-ct-integration.ps1");
        Assert.Contains("-batch", script, StringComparison.Ordinal);
        Assert.Contains("git -C $repository archive", script, StringComparison.Ordinal);
        Assert.Contains("/var/tmp/animegonet-docker-audit-", script, StringComparison.Ordinal);
        Assert.Contains("Ubuntu 24.04", script, StringComparison.Ordinal);
        Assert.Contains("--build-arg TARGETARCH=amd64", script, StringComparison.Ordinal);
        Assert.Contains("lscr.io/linuxserver/qbittorrent:5.1.4", script, StringComparison.Ordinal);
        Assert.Contains("QBITTORRENT_IMAGE='$QbittorrentImage'", script, StringComparison.Ordinal);
        Assert.Contains("bash ./eng/smoke-container.sh", script, StringComparison.Ordinal);
        Assert.Contains("bash ./eng/smoke-qbittorrent-compose.sh", script, StringComparison.Ordinal);
        Assert.Contains("[switch]$FullChainWebUi", script, StringComparison.Ordinal);
        Assert.Contains("ANIMEGONET_FULL_CHAIN_WEBUI=$fullChainWebUiValue", script, StringComparison.Ordinal);
        Assert.DoesNotContain("docker system prune", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", script, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadRepositoryFile(params string[] segments)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        return File.ReadAllText(Path.Combine([root, .. segments]));
    }
}
