namespace AnimeGoNet.App.Tests.Delivery;

public sealed class NativeMetadataPublishedSmokeContractTests
{
    [Fact]
    public async Task NativeAotMatrixRunsPublishedAiMetadataPipelineSmoke()
    {
        var root = RepositoryRoot();
        var workflow = await File.ReadAllTextAsync(Path.Combine(
            root,
            ".github",
            "workflows",
            "animegonet-native-aot.yml"));
        var script = await File.ReadAllTextAsync(Path.Combine(
            root,
            "eng",
            "smoke-native-metadata.ps1"));

        Assert.Contains(
            "./eng/smoke-native-metadata.ps1 -Executable",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains("animegonet-native-metadata-smoke-", script, StringComparison.Ordinal);
        Assert.Contains("http://127.0.0.1:", script, StringComparison.Ordinal);
        Assert.Contains("background_workers_enabled = 'false'", script, StringComparison.Ordinal);
        Assert.Contains("background_workers_enabled = 'true'", script, StringComparison.Ordinal);
        Assert.Contains("downloaders__bt__enabled = 'false'", script, StringComparison.Ordinal);
        Assert.Contains("ai_tmdb_mcp_url", script, StringComparison.Ordinal);
        Assert.Contains("/api/v1/metadata/tasks/$($seed.task_id)", script, StringComparison.Ordinal);
        Assert.Contains("season_strategy -ne 'ai_metadata'", script, StringComparison.Ordinal);
        Assert.Contains("episode_strategy -ne 'ai_metadata'", script, StringComparison.Ordinal);
        Assert.Contains("tmdb_verified", script, StringComparison.Ordinal);
        Assert.Contains("Remove-Item -LiteralPath $smokeRoot", script, StringComparison.Ordinal);
        Assert.DoesNotContain("TestSpace", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("192.168.", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("passkey", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FixtureUsesProductionStoresAndOnlyDeterministicLoopbackServices()
    {
        var root = RepositoryRoot();
        var project = await File.ReadAllTextAsync(Path.Combine(
            root,
            "tests",
            "AnimeGoNet.NativeMetadataSmokeFixture",
            "AnimeGoNet.NativeMetadataSmokeFixture.csproj"));
        var source = await File.ReadAllTextAsync(Path.Combine(
            root,
            "tests",
            "AnimeGoNet.NativeMetadataSmokeFixture",
            "Program.cs"));

        Assert.Contains("../../src/AnimeGoNet.Data/AnimeGoNet.Data.csproj", project, StringComparison.Ordinal);
        Assert.Contains("new IngestTaskStore(database)", source, StringComparison.Ordinal);
        Assert.Contains("new DownloadJobStore(database)", source, StringComparison.Ordinal);
        Assert.Contains("listener.IsLoopback", source, StringComparison.Ordinal);
        Assert.Contains("/ai/v1/chat/completions", source, StringComparison.Ordinal);
        Assert.Contains("/mcp", source, StringComparison.Ordinal);
        Assert.Contains("/tmdb/3/tv/72517/season/2/episode/7", source, StringComparison.Ordinal);
        Assert.Contains("\"tmdb_id\":72517", source, StringComparison.Ordinal);
        Assert.Contains("unsafe_absolute_paths", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TestSpace", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("192.168.", source, StringComparison.OrdinalIgnoreCase);
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
