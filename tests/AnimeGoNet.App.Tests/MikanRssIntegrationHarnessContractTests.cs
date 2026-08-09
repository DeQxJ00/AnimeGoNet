namespace AnimeGoNet.App.Tests;

public sealed class MikanRssIntegrationHarnessContractTests
{
    [Fact]
    public void RssHarnessOnlyAcceptsPrivateUrlThroughProcessEnvironment()
    {
        var script = ReadRepositoryFile("eng", "mikan-rss-live-audit.ps1");
        Assert.Contains("ANIMEGONET_MIKAN_RSS_URL", script, StringComparison.Ordinal);
        Assert.Contains("'Process'", script, StringComparison.Ordinal);
        Assert.DoesNotContain("token=", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MyBangumi?", script, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadRepositoryFile(params string[] segments)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        return File.ReadAllText(Path.Combine([root, .. segments]));
    }
}
