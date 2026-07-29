namespace AnimeGoNet.App.Tests.Delivery;

public sealed class DeploymentYamlDeliveryContractTests
{
    [Fact]
    public async Task NativeAotMatrixRunsFirstStartAndLegacyUpgradeSmoke()
    {
        var root = RepositoryRoot();
        var workflow = await File.ReadAllTextAsync(
            Path.Combine(
                root,
                ".github",
                "workflows",
                "animegonet-native-aot.yml"));
        var smoke = await File.ReadAllTextAsync(
            Path.Combine(root, "eng", "smoke-native.ps1"));

        Assert.Contains("win-x64", workflow, StringComparison.Ordinal);
        Assert.Contains("win-arm64", workflow, StringComparison.Ordinal);
        Assert.Contains("linux-x64", workflow, StringComparison.Ordinal);
        Assert.Contains("linux-arm64", workflow, StringComparison.Ordinal);
        Assert.Contains("osx-arm64", workflow, StringComparison.Ordinal);
        Assert.Equal(
            2,
            Count(
                workflow,
                "./eng/smoke-native.ps1 -Executable"));
        Assert.Contains("-LegacyYamlUpgrade", workflow, StringComparison.Ordinal);

        Assert.Contains("[switch]$LegacyYamlUpgrade", smoke, StringComparison.Ordinal);
        Assert.Contains("version: 1.6.1", smoke, StringComparison.Ordinal);
        Assert.Contains("animego-1.6.1-*.yaml", smoke, StringComparison.Ordinal);
        Assert.Contains("backup does not exactly match", smoke, StringComparison.Ordinal);
        Assert.Contains("incorrectly migrated as a static tag", smoke, StringComparison.Ordinal);
        Assert.DoesNotContain("TestSpace", workflow + smoke, StringComparison.OrdinalIgnoreCase);
    }

    private static int Count(string value, string search)
    {
        var count = 0;
        var offset = 0;
        while ((offset = value.IndexOf(search, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += search.Length;
        }

        return count;
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
