namespace AnimeGoNet.App.Tests.Delivery;

public sealed class HostCliDeliveryContractTests
{
    [Fact]
    public async Task EveryNativeAotArtifactRunsHelpAndHeadlessSmoke()
    {
        string root = RepositoryRoot();
        string workflow = await File.ReadAllTextAsync(Path.Combine(
            root,
            ".github",
            "workflows",
            "animegonet-native-aot.yml"));
        string smoke = await File.ReadAllTextAsync(Path.Combine(
            root,
            "eng",
            "smoke-native-cli.ps1"));

        Assert.Contains(
            "./eng/smoke-native-cli.ps1 -Executable",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains("@('--help')", smoke, StringComparison.Ordinal);
        Assert.Contains("-web=false", smoke, StringComparison.Ordinal);
        Assert.Contains("$env:ANIMEGO_WEB = 'true'", smoke, StringComparison.Ordinal);
        Assert.Contains("Get-FreeLoopbackPort", smoke, StringComparison.Ordinal);
        Assert.Contains("unexpectedly opened a TCP listener", smoke, StringComparison.Ordinal);
        Assert.Contains("did not initialize its database within 20 seconds", smoke, StringComparison.Ordinal);
        Assert.DoesNotContain("TestSpace", smoke, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("192.168.", smoke, StringComparison.OrdinalIgnoreCase);
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
