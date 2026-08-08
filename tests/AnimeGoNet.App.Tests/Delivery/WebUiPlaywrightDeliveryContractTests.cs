using System.Text.Json;

namespace AnimeGoNet.App.Tests.Delivery;

public sealed class WebUiPlaywrightDeliveryContractTests
{
    [Fact]
    public async Task DockerWorkflowRunsPinnedChromiumAgainstHardenedReleaseContainer()
    {
        string repositoryRoot = FindRepositoryRoot();
        using JsonDocument package = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(repositoryRoot, "package.json")));
        JsonElement root = package.RootElement;
        Assert.Equal(
            "playwright test --config playwright.config.mjs tests/web-e2e/release-container.spec.mjs",
            root.GetProperty("scripts").GetProperty("web:e2e").GetString());
        Assert.Equal(
            "1.62.0",
            root.GetProperty("devDependencies").GetProperty("@playwright/test").GetString());

        string workflow = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            ".github",
            "workflows",
            "animegonet-docker.yml"));
        Assert.Contains("actions/setup-node@v6", workflow, StringComparison.Ordinal);
        Assert.Contains("npm ci", workflow, StringComparison.Ordinal);
        Assert.Contains(
            "npx playwright install --with-deps chromium",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains(
            "./eng/smoke-webui-container.sh animegonet:ci",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains(".artifacts/playwright-report", workflow, StringComparison.Ordinal);

        string launcher = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "eng",
            "smoke-webui-container.sh"));
        Assert.Contains("--user \"$test_uid:$test_gid\"", launcher, StringComparison.Ordinal);
        Assert.Contains("--read-only", launcher, StringComparison.Ordinal);
        Assert.Contains("no-new-privileges:true", launcher, StringComparison.Ordinal);
        Assert.Contains("127.0.0.1::7991", launcher, StringComparison.Ordinal);
        Assert.Contains("npm run web:e2e", launcher, StringComparison.Ordinal);
        Assert.DoesNotContain("TestSpace", launcher, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("123456", launcher, StringComparison.Ordinal);

        string spec = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "tests",
            "web-e2e",
            "release-container.spec.mjs"));
        Assert.Contains("expect(status.native_aot).toBe(true)", spec, StringComparison.Ordinal);
        Assert.Contains(
            ".toEqual([\"4\", \"3\", \"independent\", \"2\", \"1\"])",
            spec,
            StringComparison.Ordinal);
        Assert.Contains("viewportWidth: 390", spec, StringComparison.Ordinal);
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
