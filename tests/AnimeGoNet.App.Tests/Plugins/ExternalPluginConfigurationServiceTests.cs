using System.Text.Json;
using System.Text.Json.Nodes;
using AnimeGoNet.App.Plugins;

namespace AnimeGoNet.App.Tests.Plugins;

public sealed class ExternalPluginConfigurationServiceTests
{
    [Fact]
    public async Task ValidatedSavePersistsAndResetsRuntimeWithoutStartingPlugin()
    {
        await using var fixture = await ServiceFixture.CreateAsync();

        var saved = await fixture.Service.SaveAsync(
            fixture.PluginId,
            true,
            Json("{\"fallback\":true}"),
            Json("{\"quality\":\"1080p\"}"),
            0);

        var entry = Assert.Single(saved.Plugins).Value;
        Assert.Equal(1, saved.Revision);
        Assert.True(entry.Enabled);
        Assert.Equal(fixture.Now, entry.UpdatedAtUtc);
        Assert.Equal(ExternalPluginRuntimeState.Stopped, fixture.Manager.GetSnapshot(fixture.PluginId)!.State);
        Assert.True(File.Exists(fixture.Store.FilePath));
    }

    [Fact]
    public async Task SchemaFailureDoesNotCreateOrAdvancePrivateConfiguration()
    {
        await using var fixture = await ServiceFixture.CreateAsync();

        var error = await Assert.ThrowsAsync<ExternalPluginConfigurationValidationException>(() =>
            fixture.Service.SaveAsync(
                fixture.PluginId,
                true,
                Json("{}"),
                Json("{\"quality\":\"4k\"}"),
                0));

        Assert.Equal("plugin_config_invalid", error.Code);
        Assert.Equal(0, fixture.Store.Current.Revision);
        Assert.False(File.Exists(fixture.Store.FilePath));
    }

    [Fact]
    public async Task ManifestIdentityChangeIsRejectedBeforeConfigurationWrite()
    {
        await using var fixture = await ServiceFixture.CreateAsync();
        fixture.WriteManifest(version: "2.0.0");

        var error = await Assert.ThrowsAsync<ExternalPluginProtocolException>(() =>
            fixture.Service.SaveAsync(
                fixture.PluginId,
                true,
                Json("{}"),
                Json("{\"quality\":\"1080p\"}"),
                0));

        Assert.Equal("plugin_manifest_changed", error.Code);
        Assert.False(File.Exists(fixture.Store.FilePath));
    }

    [Fact]
    public async Task UnknownPluginFailsWithoutCreatingConfiguration()
    {
        await using var fixture = await ServiceFixture.CreateAsync();

        var error = await Assert.ThrowsAsync<ExternalPluginUnavailableException>(() =>
            fixture.Service.SaveAsync(
                "com.example.missing",
                true,
                Json("{}"),
                Json("{}"),
                0));

        Assert.Equal("plugin_not_found", error.Code);
        Assert.False(File.Exists(fixture.Store.FilePath));
    }

    [Fact]
    public async Task EditableViewReturnsAndSafeSaveRetainsWriteOnlyVars()
    {
        await using var fixture = await ServiceFixture.CreateAsync();
        await fixture.Service.SaveSafeAsync(
            fixture.PluginId,
            true,
            Json("{}"),
            Json("{\"quality\":\"1080p\",\"token\":\"secret\"}"),
            [],
            0);

        var firstView = await fixture.Service.GetAsync(fixture.PluginId);
        await fixture.Service.SaveSafeAsync(
            fixture.PluginId,
            true,
            Json("{}"),
            Json("{\"quality\":\"720p\"}"),
            [],
            1);
        var persisted = fixture.Store.GetOrDefault(fixture.PluginId);

        Assert.Equal("secret", firstView.Vars.Value.GetProperty("token").GetString());
        Assert.Equal("/token", Assert.Single(firstView.Vars.ConfiguredWriteOnlyPaths));
        Assert.Equal("secret", persisted.Vars.GetProperty("token").GetString());
        Assert.Equal("720p", persisted.Vars.GetProperty("quality").GetString());
    }

    private static JsonElement Json(string json) =>
        JsonDocument.Parse(json).RootElement.Clone();

    private sealed class ServiceFixture : IAsyncDisposable
    {
        private ServiceFixture(string rootPath)
        {
            RootPath = rootPath;
            PluginRoot = Path.Combine(rootPath, "plugins");
            PackagePath = Path.Combine(PluginRoot, "example");
            ConfigurationPath = Path.Combine(rootPath, "config");
            PluginDataPath = Path.Combine(rootPath, "plugin-data");
            Directory.CreateDirectory(PackagePath);
            Directory.CreateDirectory(ConfigurationPath);
            WriteEntryPoint();
            File.WriteAllText(
                Path.Combine(PackagePath, "config.schema.json"),
                "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"quality\":{\"type\":\"string\",\"enum\":[\"720p\",\"1080p\"]},\"token\":{\"type\":\"string\",\"writeOnly\":true}}}");
            WriteManifest("1.0.0");
            Loader = new ExternalPluginManifestLoader(PluginRoot, CurrentRid());
            Store = new ExternalPluginConfigurationStore(ConfigurationPath);
        }

        public string PluginId { get; } = "com.example.filter";

        public DateTimeOffset Now { get; } =
            new(2026, 8, 1, 11, 0, 0, TimeSpan.Zero);

        public string RootPath { get; }

        public string PluginRoot { get; }

        public string PackagePath { get; }

        public string ConfigurationPath { get; }

        public string PluginDataPath { get; }

        public ExternalPluginManifestLoader Loader { get; }

        public ExternalPluginConfigurationStore Store { get; }

        public ExternalPluginHostManager Manager { get; private set; } = null!;

        public ExternalPluginConfigurationService Service { get; private set; } = null!;

        public static async Task<ServiceFixture> CreateAsync()
        {
            var fixture = new ServiceFixture(Path.Combine(
                Path.GetTempPath(),
                $"animegonet-plugin-config-service-{Guid.NewGuid():N}"));
            await fixture.Store.LoadAsync();
            var discovery = await fixture.Loader.DiscoverAsync();
            Assert.Empty(discovery.Errors);
            fixture.Manager = new ExternalPluginHostManager(
                fixture.Loader,
                discovery,
                fixture.PluginDataPath,
                configurations: fixture.Store);
            fixture.Service = new ExternalPluginConfigurationService(
                discovery,
                fixture.Loader,
                new ExternalPluginConfigurationValidator(),
                fixture.Store,
                fixture.Manager,
                new FixedTimeProvider(fixture.Now));
            return fixture;
        }

        public void WriteManifest(string version)
        {
            var manifest = new JsonObject
            {
                ["id"] = PluginId,
                ["name"] = "Example filter",
                ["version"] = version,
                ["apiVersion"] = 1,
                ["type"] = "filter",
                ["rid"] = CurrentRid(),
                ["entryPoint"] = EntryPointName(),
                ["configSchema"] = "config.schema.json",
                ["capabilities"] = new JsonArray(),
            };
            File.WriteAllText(
                Path.Combine(PackagePath, "plugin.json"),
                manifest.ToJsonString());
        }

        public async ValueTask DisposeAsync()
        {
            if (Manager is not null)
            {
                await Manager.DisposeAsync();
            }
            Store.Dispose();
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }

        private void WriteEntryPoint()
        {
            var path = Path.Combine(PackagePath, EntryPointName());
            File.WriteAllBytes(path, [0x00]);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    path,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        private static string EntryPointName() =>
            OperatingSystem.IsWindows() ? "plugin.exe" : "plugin";

        private static string CurrentRid()
        {
            var os = OperatingSystem.IsWindows()
                ? "win"
                : OperatingSystem.IsLinux()
                    ? "linux"
                    : "osx";
            var architecture = System.Runtime.InteropServices.RuntimeInformation
                .ProcessArchitecture.ToString().ToLowerInvariant();
            return $"{os}-{architecture}";
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
