using AnimeGoNet.App.Configuration;
using AnimeGoNet.App.Downloads;
using AnimeGoNet.Core.Downloads;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeGoNet.App.Tests.Configuration;

public sealed class LegacyDownloaderMigrationDetectorTests
{
    [Theory]
    [InlineData("Transmission")]
    [InlineData(" transmission ")]
    [InlineData("aria2")]
    public void UnsupportedLegacyEnvironmentTypeBlocksDownloads(string value)
    {
        var root = CreateRoot();
        try
        {
            var state = LegacyDownloaderMigrationDetector.Detect(value, root);

            var diagnostic = Assert.Single(state.Diagnostics);
            Assert.True(state.BlocksDownloads);
            Assert.Equal("UnsupportedDownloaderType", diagnostic.Code);
            Assert.Equal("environment:ANIMEGO_CLIENT", diagnostic.Source);
            Assert.True(diagnostic.BlocksDownloads);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ExplicitQbittorrentEnvironmentOverridesLegacyYaml()
    {
        var root = CreateRoot();
        try
        {
            File.WriteAllText(
                Path.Combine(root, "animego.yaml"),
                LegacyYaml("Transmission", "private-user", "private-password"));

            var state = LegacyDownloaderMigrationDetector.Detect("QBittorrent", root);

            Assert.False(state.BlocksDownloads);
            Assert.Empty(state.Diagnostics);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ReadsOnlyNestedLegacyDownloaderTypeWithoutReturningCredentials()
    {
        var root = CreateRoot();
        try
        {
            File.WriteAllText(
                Path.Combine(root, "animego.yaml"),
                LegacyYaml("\"Transmission\"", "legacy-user", "legacy-secret"));

            var state = LegacyDownloaderMigrationDetector.Detect(null, root);

            var diagnostic = Assert.Single(state.Diagnostics);
            Assert.Equal("UnsupportedDownloaderType", diagnostic.Code);
            Assert.Equal("legacy_yaml", diagnostic.Source);
            Assert.Equal("Transmission", diagnostic.LegacyDownloaderType);
            var projected = string.Join(
                "|",
                diagnostic.Code,
                diagnostic.Source,
                diagnostic.LegacyDownloaderType,
                diagnostic.Message);
            Assert.DoesNotContain("legacy-user", projected, StringComparison.Ordinal);
            Assert.DoesNotContain("legacy-secret", projected, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void UnreadableOrOversizedLegacyYamlFailsClosed()
    {
        var root = CreateRoot();
        try
        {
            File.WriteAllBytes(
                Path.Combine(root, "animego.yaml"),
                new byte[1024 * 1024 + 1]);

            var state = LegacyDownloaderMigrationDetector.Detect(null, root);

            var diagnostic = Assert.Single(state.Diagnostics);
            Assert.True(state.BlocksDownloads);
            Assert.Equal("LegacyConfigurationUnreadable", diagnostic.Code);
            Assert.Equal("unknown", diagnostic.LegacyDownloaderType);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ExplicitLegacyConfigPathIsInspectedAndMissingPathFailsClosed()
    {
        var root = CreateRoot();
        try
        {
            var externalPath = Path.Combine(root, "old", "custom.yaml");
            Directory.CreateDirectory(Path.GetDirectoryName(externalPath)!);
            File.WriteAllText(
                externalPath,
                LegacyYaml("Transmission", "ignored-user", "ignored-password"));

            var detected = LegacyDownloaderMigrationDetector.Detect(
                null,
                root,
                externalPath);
            var missing = LegacyDownloaderMigrationDetector.Detect(
                null,
                root,
                Path.Combine(root, "missing.yaml"));

            Assert.Equal(
                "UnsupportedDownloaderType",
                Assert.Single(detected.Diagnostics).Code);
            Assert.Equal(
                "LegacyConfigurationUnreadable",
                Assert.Single(missing.Diagnostics).Code);
            Assert.True(missing.BlocksDownloads);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CommentsAndUnrelatedClientKeysDoNotCreateFalsePositive()
    {
        var root = CreateRoot();
        try
        {
            File.WriteAllText(
                Path.Combine(root, "animego.yaml"),
                """
                client:
                  client: Transmission
                setting:
                  # client: Transmission
                  client:
                    url: "http://Transmission.invalid/#fragment"
                    client: QBittorrent # effective legacy type
                """);

            var state = LegacyDownloaderMigrationDetector.Detect(null, root);

            Assert.False(state.BlocksDownloads);
            Assert.Empty(state.Diagnostics);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(true, "environment:ANIMEGO_CLIENT")]
    [InlineData(false, "legacy_yaml")]
    public async Task ApplicationBuildAppliesFailClosedMigrationState(
        bool useEnvironmentOverride,
        string expectedSource)
    {
        var root = CreateRoot();
        var dataPath = Path.Combine(root, "data");
        var downloadPath = Path.Combine(root, "download", "incomplete");
        var savePath = Path.Combine(root, "download", "anime");
        Directory.CreateDirectory(dataPath);
        Directory.CreateDirectory(downloadPath);
        Directory.CreateDirectory(savePath);
        if (!useEnvironmentOverride)
        {
            await File.WriteAllTextAsync(
                Path.Combine(dataPath, "animego.yaml"),
                LegacyYaml("Transmission", "ignored-user", "ignored-password"));
        }

        var args = new List<string>
        {
            "--data_path", dataPath,
            "--download_path", downloadPath,
            "--save_path", savePath,
        };
        if (useEnvironmentOverride)
        {
            args.AddRange(["--ANIMEGO_CLIENT", "Transmission"]);
        }

        var app = await AnimeGoApplication.BuildAsync(
            args.ToArray(),
            runningInContainer: false,
            startBackgroundWorkers: true);
        try
        {
            var state = app.Services.GetRequiredService<LegacyDownloaderMigrationState>();
            var runtime = app.Services.GetRequiredService<RuntimeConfigurationState>();
            var registry = app.Services.GetRequiredService<IDownloadClientRegistry>();

            var diagnostic = Assert.Single(state.Diagnostics);
            Assert.Equal(expectedSource, diagnostic.Source);
            Assert.True(state.BlocksDownloads);
            Assert.False(runtime.BackgroundWorkersEnabled);
            Assert.IsType<BlockedDownloadClientRegistry>(registry);
            Assert.Empty(registry.InstanceIds);
        }
        finally
        {
            await app.DisposeAsync();
            Directory.Delete(root, recursive: true);
        }
    }

    private static string LegacyYaml(string type, string username, string password) =>
        $$"""
        version: 1.7.1
        setting:
          client:
            client: {{type}} # legacy ANIMEGO_CLIENT
            url: http://127.0.0.1:9091
            username: {{username}}
            password: {{password}}
        advanced:
          client:
            retry_connect_num: 3
        """;

    private static string CreateRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "animegonet-legacy-migration-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
