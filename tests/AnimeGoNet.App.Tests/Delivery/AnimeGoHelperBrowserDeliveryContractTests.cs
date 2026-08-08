using System.Text.Json;

namespace AnimeGoNet.App.Tests.Delivery;

public sealed class AnimeGoHelperBrowserDeliveryContractTests
{
    [Fact]
    public async Task CiPinsAndRunsTheUnmodifiedUserscriptBrowserContract()
    {
        string repositoryRoot = FindRepositoryRoot();
        string manifestPath = Path.Combine(
            repositoryRoot,
            "tests",
            "web-e2e",
            "fixtures",
            "animegohelper.upstream.json");
        using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));
        Assert.Equal(
            "78a9d0d832801d38efd6294841e9962e0bc791cf",
            manifest.RootElement.GetProperty("commit").GetString());
        Assert.Equal(
            "d165c33c9692530da3d81032a49d1cdf42a815b7469e3438ff8457201a804576",
            manifest.RootElement.GetProperty("sha256").GetString());

        using JsonDocument package = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(repositoryRoot, "package.json")));
        Assert.Equal(
            "playwright test --config playwright.config.mjs tests/web-e2e/animegohelper-compat.spec.mjs",
            package.RootElement.GetProperty("scripts").GetProperty("helper:e2e").GetString());

        string workflow = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            ".github",
            "workflows",
            "dotnet-ci.yml"));
        Assert.Contains("repository: DeQxJ00/AnimeGoHelper", workflow, StringComparison.Ordinal);
        Assert.Contains(
            "ref: 78a9d0d832801d38efd6294841e9962e0bc791cf",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains("persist-credentials: false", workflow, StringComparison.Ordinal);
        Assert.Contains("npm run helper:e2e", workflow, StringComparison.Ordinal);
        Assert.Contains("ANIMEGOHELPER_SCRIPT_PATH", workflow, StringComparison.Ordinal);

        string spec = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "tests",
            "web-e2e",
            "animegohelper-compat.spec.mjs"));
        Assert.Contains("/download/manager", spec, StringComparison.Ordinal);
        Assert.Contains("is_select_ep: false", spec, StringComparison.Ordinal);
        Assert.Contains("/api/plugin/config", spec, StringComparison.Ordinal);
        Assert.Contains("expect(browserErrors).toEqual([])", spec, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AnimeGoNet.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate AnimeGoNet.slnx.");
    }
}
