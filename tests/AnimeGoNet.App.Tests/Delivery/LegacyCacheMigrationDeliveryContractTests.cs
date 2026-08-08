namespace AnimeGoNet.App.Tests.Delivery;

public sealed class LegacyCacheMigrationDeliveryContractTests
{
    [Fact]
    public async Task CiTestsExporterAndEveryNativeArtifactIncludesAndSmokesImporter()
    {
        var root = RepositoryRoot();
        var continuousIntegration = await File.ReadAllTextAsync(Path.Combine(
            root,
            ".github",
            "workflows",
            "dotnet-ci.yml"));
        var native = await File.ReadAllTextAsync(Path.Combine(
            root,
            ".github",
            "workflows",
            "animegonet-native-aot.yml"));
        var smoke = await File.ReadAllTextAsync(Path.Combine(
            root,
            "eng",
            "smoke-legacy-data-migration.ps1"));

        Assert.False(File.Exists(Path.Combine(root, ".github", "workflows", "test.yml")));
        Assert.False(File.Exists(Path.Combine(root, ".github", "workflows", "release.yml")));
        Assert.Contains("working-directory: tools/legacy-cache-exporter", continuousIntegration, StringComparison.Ordinal);
        Assert.Contains("run: go test ./...", continuousIntegration, StringComparison.Ordinal);
        Assert.Contains("Smoke legacy cache and directory-sidecar migration", continuousIntegration, StringComparison.Ordinal);
        Assert.Equal(5, Count(native, "importer: AnimeGoNet.LegacyCacheImporter"));
        Assert.Contains(
            "dotnet restore tools/AnimeGoNet.LegacyCacheImporter/AnimeGoNet.LegacyCacheImporter.csproj",
            native,
            StringComparison.Ordinal);
        Assert.Contains(
            "dotnet publish tools/AnimeGoNet.LegacyCacheImporter/AnimeGoNet.LegacyCacheImporter.csproj",
            native,
            StringComparison.Ordinal);
        Assert.Contains("./eng/smoke-legacy-data-migration.ps1", native, StringComparison.Ordinal);
        Assert.Contains("-Importer \"artifacts/publish/${{ matrix.rid }}/${{ matrix.importer }}\"", native, StringComparison.Ordinal);
        Assert.Contains("already_imported", smoke, StringComparison.Ordinal);
        Assert.Contains("last_rejected_count -ne 0", smoke, StringComparison.Ordinal);
        Assert.Contains("Remove-Item -LiteralPath $resolvedSmokeRoot", smoke, StringComparison.Ordinal);
        Assert.DoesNotContain("TestSpace", smoke, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("192.168.", smoke, StringComparison.OrdinalIgnoreCase);
    }

    private static int Count(string source, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
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
