using System.Runtime.InteropServices;
using System.Globalization;
using System.Text.Json.Nodes;
using AnimeGoNet.App.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeGoNet.App.Tests.Plugins;

public sealed class ExternalPluginManifestLoaderTests
{
    [Fact]
    public void ProtocolPublishesExactlyTheFiveReleaseRidsAndSixCategories()
    {
        Assert.Equal(
            ["linux-arm64", "linux-x64", "osx-arm64", "win-arm64", "win-x64"],
            ExternalPluginProtocol.SupportedRids.Order(StringComparer.Ordinal));
        Assert.Equal(
            ["feed", "filter", "parser", "rename", "schedule", "source"],
            ExternalPluginProtocol.SupportedTypes.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ValidRidSpecificPackageLoadsWithoutAssemblyScanning()
    {
        using var fixture = new PluginRootFixture();
        var packagePath = fixture.CreatePackage(
            "resolution",
            "com.example.resolution-filter",
            version: "1.2.3-beta-preview.1+build-7.2",
            capabilities: ["filter.resolution", "metadata.read"]);
        var loader = new ExternalPluginManifestLoader(fixture.RootPath, fixture.Rid);

        var package = await loader.LoadPackageAsync(packagePath);

        Assert.Equal("com.example.resolution-filter", package.Manifest.Id);
        Assert.Equal("1.2.3-beta-preview.1+build-7.2", package.Manifest.Version);
        Assert.Equal("filter", package.Manifest.Type);
        Assert.Equal(fixture.Rid, package.Manifest.Rid);
        Assert.Equal(["filter.resolution", "metadata.read"], package.Manifest.Capabilities);
        Assert.Equal(Path.Combine(packagePath, fixture.EntryPointName), package.EntryPointPath);
        Assert.Equal(Path.Combine(packagePath, "config.schema.json"), package.ConfigSchemaPath);
        Assert.False(package.ManifestPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ApplicationRegistersLoaderAgainstItsPrivatePluginDirectory()
    {
        await using var app = await RunningApp.StartAsync();

        var loader = app.App.Services.GetRequiredService<ExternalPluginManifestLoader>();
        var discovery = app.App.Services.GetRequiredService<ExternalPluginDiscoveryResult>();
        var refreshed = await loader.DiscoverAsync();
        var status = JsonNode.Parse(await app.Client.GetStringAsync("/api/v1/status"));

        Assert.Empty(discovery.Packages);
        Assert.Empty(discovery.Errors);
        Assert.Empty(refreshed.Packages);
        Assert.Empty(status!["external_plugins"]!["packages"]!.AsArray());
        Assert.Empty(status["external_plugins"]!["errors"]!.AsArray());
        Assert.Empty(status["external_plugins"]!["runtimes"]!.AsArray());
        Assert.True(Directory.Exists(Path.Combine(app.RootPath, "data", "plugins")));
        Assert.True(Directory.Exists(Path.Combine(app.RootPath, "data", "plugin-data")));
    }

    [Theory]
    [InlineData("id", "Mikan", "plugin_id_invalid")]
    [InlineData("id", "mikan", "plugin_id_invalid")]
    [InlineData("version", "1.0", "plugin_version_invalid")]
    [InlineData("version", "01.0.0", "plugin_version_invalid")]
    [InlineData("apiVersion", "2", "plugin_api_version_unsupported")]
    [InlineData("type", "core", "plugin_type_invalid")]
    [InlineData("rid", "unsupported-x64", "plugin_rid_unsupported")]
    [InlineData("entryPoint", "../escape.exe", "plugin_entry_point_invalid")]
    [InlineData("configSchema", "../schema.json", "plugin_config_schema_invalid")]
    public async Task InvalidManifestFieldFailsWithStableCode(
        string field,
        string value,
        string expectedCode)
    {
        using var fixture = new PluginRootFixture();
        var packagePath = fixture.CreatePackage("invalid", "com.example.invalid");
        var manifest = PluginRootFixture.ReadManifest(packagePath);
        manifest[field] = field == "apiVersion"
            ? int.Parse(value, CultureInfo.InvariantCulture)
            : value;
        PluginRootFixture.WriteManifest(packagePath, manifest);
        var loader = new ExternalPluginManifestLoader(fixture.RootPath, fixture.Rid);

        var exception = await Assert.ThrowsAsync<ExternalPluginManifestException>(
            () => loader.LoadPackageAsync(packagePath));

        Assert.Equal(expectedCode, exception.Code);
    }

    [Fact]
    public async Task SupportedButDifferentRidIsRejectedBeforeExecution()
    {
        using var fixture = new PluginRootFixture();
        var differentRid = fixture.Rid == "linux-arm64" ? "linux-x64" : "linux-arm64";
        var packagePath = fixture.CreatePackage(
            "wrong-rid",
            "com.example.wrong-rid",
            rid: differentRid);
        var loader = new ExternalPluginManifestLoader(fixture.RootPath, fixture.Rid);

        var exception = await Assert.ThrowsAsync<ExternalPluginManifestException>(
            () => loader.LoadPackageAsync(packagePath));

        Assert.Equal("plugin_rid_mismatch", exception.Code);
    }

    [Theory]
    [InlineData("{\"id\":", "plugin_manifest_json_invalid")]
    [InlineData("{\"id\":\"com.example.one\",\"id\":\"com.example.two\"}", "plugin_manifest_duplicate_field")]
    [InlineData("{\"unknown\":true}", "plugin_manifest_unknown_field")]
    public async Task MalformedAmbiguousOrUnknownManifestJsonFailsClosed(
        string content,
        string expectedCode)
    {
        using var fixture = new PluginRootFixture();
        var packagePath = fixture.CreatePackage("bad-json", "com.example.bad-json");
        await File.WriteAllTextAsync(Path.Combine(packagePath, "plugin.json"), content);
        var loader = new ExternalPluginManifestLoader(fixture.RootPath, fixture.Rid);

        var exception = await Assert.ThrowsAsync<ExternalPluginManifestException>(
            () => loader.LoadPackageAsync(packagePath));

        Assert.Equal(expectedCode, exception.Code);
    }

    [Fact]
    public async Task DuplicateCapabilitiesAndSchemaDuplicatesFailClosed()
    {
        using var fixture = new PluginRootFixture();
        var capabilityPackage = fixture.CreatePackage(
            "duplicate-capability",
            "com.example.duplicate-capability",
            capabilities: ["metadata.read", "metadata.read"]);
        var schemaPackage = fixture.CreatePackage(
            "duplicate-schema",
            "com.example.duplicate-schema");
        var invalidCapabilityPackage = fixture.CreatePackage(
            "invalid-capability",
            "com.example.invalid-capability",
            capabilities: ["metadata..read"]);
        await File.WriteAllTextAsync(
            Path.Combine(schemaPackage, "config.schema.json"),
            "{\"type\":\"object\",\"type\":\"array\"}");
        var loader = new ExternalPluginManifestLoader(fixture.RootPath, fixture.Rid);

        var capabilityError = await Assert.ThrowsAsync<ExternalPluginManifestException>(
            () => loader.LoadPackageAsync(capabilityPackage));
        var schemaError = await Assert.ThrowsAsync<ExternalPluginManifestException>(
            () => loader.LoadPackageAsync(schemaPackage));
        var invalidCapabilityError = await Assert.ThrowsAsync<ExternalPluginManifestException>(
            () => loader.LoadPackageAsync(invalidCapabilityPackage));

        Assert.Equal("plugin_capabilities_invalid", capabilityError.Code);
        Assert.Equal("plugin_config_schema_invalid", schemaError.Code);
        Assert.Equal("plugin_capabilities_invalid", invalidCapabilityError.Code);
    }

    [Theory]
    [InlineData(true, false, "plugin_entry_point_missing")]
    [InlineData(false, true, "plugin_config_schema_missing")]
    public async Task MissingDeclaredFilesFailClosed(
        bool deleteEntryPoint,
        bool deleteSchema,
        string expectedCode)
    {
        using var fixture = new PluginRootFixture();
        var packagePath = fixture.CreatePackage("missing", "com.example.missing");
        if (deleteEntryPoint)
        {
            File.Delete(Path.Combine(packagePath, fixture.EntryPointName));
        }
        if (deleteSchema)
        {
            File.Delete(Path.Combine(packagePath, "config.schema.json"));
        }
        var loader = new ExternalPluginManifestLoader(fixture.RootPath, fixture.Rid);

        var exception = await Assert.ThrowsAsync<ExternalPluginManifestException>(
            () => loader.LoadPackageAsync(packagePath));

        Assert.Equal(expectedCode, exception.Code);
    }

    [Theory]
    [InlineData("{\"type\":\"array\",\"items\":{\"type\":\"string\"}}")]
    [InlineData("{\"type\":\"object\",\"required\":[\"missing\"],\"properties\":{}}")]
    [InlineData("{\"type\":\"object\",\"properties\":{\"token\":{\"type\":\"string\",\"writeOnly\":\"yes\"}}}")]
    public async Task UnsupportedConfigurationSchemaFailsDuringDiscovery(string schema)
    {
        using var fixture = new PluginRootFixture();
        var packagePath = fixture.CreatePackage("schema", "com.example.schema");
        File.WriteAllText(Path.Combine(packagePath, "config.schema.json"), schema);
        var loader = new ExternalPluginManifestLoader(fixture.RootPath, fixture.Rid);

        var result = await loader.DiscoverAsync();

        Assert.Empty(result.Packages);
        Assert.Equal(
            "plugin_config_schema_invalid",
            Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task DiscoveryRejectsEveryDuplicateIdAndKeepsIndependentValidPackages()
    {
        using var fixture = new PluginRootFixture();
        fixture.CreatePackage("a", "com.example.duplicate");
        fixture.CreatePackage("b", "com.example.duplicate");
        fixture.CreatePackage("valid", "com.example.valid");
        Directory.CreateDirectory(Path.Combine(fixture.RootPath, "missing-manifest"));
        var loader = new ExternalPluginManifestLoader(fixture.RootPath, fixture.Rid);

        var result = await loader.DiscoverAsync();

        Assert.Equal("com.example.valid", Assert.Single(result.Packages).Manifest.Id);
        Assert.Equal(3, result.Errors.Count);
        Assert.Equal(2, result.Errors.Count(error => error.Code == "plugin_id_duplicate"));
        Assert.Contains(result.Errors, error =>
            error.PackageDirectoryName == "missing-manifest"
            && error.Code == "plugin_manifest_missing");
    }

    [Fact]
    public async Task PackageOutsideConfiguredRootIsRejected()
    {
        using var fixture = new PluginRootFixture();
        var outsideRoot = Path.Combine(fixture.ParentPath, "outside");
        Directory.CreateDirectory(outsideRoot);
        var packagePath = fixture.CreatePackageAt(
            outsideRoot,
            "package",
            "com.example.outside");
        var loader = new ExternalPluginManifestLoader(fixture.RootPath, fixture.Rid);

        var exception = await Assert.ThrowsAsync<ExternalPluginManifestException>(
            () => loader.LoadPackageAsync(packagePath));

        Assert.Equal("plugin_package_path_invalid", exception.Code);
    }

    [Fact]
    public async Task SymlinkEntryPointCannotEscapePackage()
    {
        using var fixture = new PluginRootFixture();
        var packagePath = fixture.CreatePackage("linked", "com.example.linked");
        var entryPoint = Path.Combine(packagePath, fixture.EntryPointName);
        var outside = Path.Combine(fixture.ParentPath, fixture.EntryPointName);
        File.WriteAllBytes(outside, [0x00]);
        File.Delete(entryPoint);
        try
        {
            File.CreateSymbolicLink(entryPoint, outside);
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException
                or PlatformNotSupportedException
                or IOException
            && OperatingSystem.IsWindows())
        {
            Assert.True(OperatingSystem.IsWindows());
            return;
        }
        var loader = new ExternalPluginManifestLoader(fixture.RootPath, fixture.Rid);

        var error = await Assert.ThrowsAsync<ExternalPluginManifestException>(
            () => loader.LoadPackageAsync(packagePath));

        Assert.Equal("plugin_path_link_disallowed", error.Code);
    }

    [Fact]
    public async Task GroupWritablePackageIsRejectedOnUnix()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.True(OperatingSystem.IsWindows());
            return;
        }

        using var fixture = new PluginRootFixture();
        var packagePath = fixture.CreatePackage("writable", "com.example.writable");
        File.SetUnixFileMode(
            packagePath,
            UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead
                | UnixFileMode.GroupWrite
                | UnixFileMode.GroupExecute);
        var loader = new ExternalPluginManifestLoader(fixture.RootPath, fixture.Rid);

        var error = await Assert.ThrowsAsync<ExternalPluginManifestException>(
            () => loader.LoadPackageAsync(packagePath));

        Assert.Equal("plugin_permissions_unsafe", error.Code);
    }

    [Theory]
    [InlineData(true, "plugin_manifest_size_invalid")]
    [InlineData(false, "plugin_config_schema_size_invalid")]
    public async Task OversizedMetadataFilesAreRejectedBeforeParsing(
        bool manifest,
        string expectedCode)
    {
        using var fixture = new PluginRootFixture();
        var packagePath = fixture.CreatePackage("oversized", "com.example.oversized");
        var filePath = Path.Combine(
            packagePath,
            manifest ? "plugin.json" : "config.schema.json");
        await File.WriteAllTextAsync(filePath, new string(' ', manifest ? 65_537 : 262_145));
        var loader = new ExternalPluginManifestLoader(fixture.RootPath, fixture.Rid);

        var error = await Assert.ThrowsAsync<ExternalPluginManifestException>(
            () => loader.LoadPackageAsync(packagePath));

        Assert.Equal(expectedCode, error.Code);
    }

    [Fact]
    public async Task CancellationIsPropagatedWithoutDowngradingToPackageError()
    {
        using var fixture = new PluginRootFixture();
        var packagePath = fixture.CreatePackage("cancel", "com.example.cancel");
        var loader = new ExternalPluginManifestLoader(fixture.RootPath, fixture.Rid);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<TaskCanceledException>(
            () => loader.LoadPackageAsync(packagePath, cancellation.Token));
    }

    private sealed class PluginRootFixture : IDisposable
    {
        public PluginRootFixture()
        {
            ParentPath = Path.Combine(
                Path.GetTempPath(),
                "animegonet-plugin-manifest-tests",
                Guid.NewGuid().ToString("N"));
            RootPath = Path.Combine(ParentPath, "plugins");
            Directory.CreateDirectory(RootPath);
            Rid = CurrentRid();
            EntryPointName = OperatingSystem.IsWindows() ? "Plugin.exe" : "Plugin";
        }

        public string ParentPath { get; }

        public string RootPath { get; }

        public string Rid { get; }

        public string EntryPointName { get; }

        public string CreatePackage(
            string directoryName,
            string id,
            string version = "1.0.0",
            string? rid = null,
            IReadOnlyList<string>? capabilities = null) =>
            CreatePackageAt(
                RootPath,
                directoryName,
                id,
                version,
                rid,
                capabilities);

        public string CreatePackageAt(
            string root,
            string directoryName,
            string id,
            string version = "1.0.0",
            string? rid = null,
            IReadOnlyList<string>? capabilities = null)
        {
            var packagePath = Path.Combine(root, directoryName);
            Directory.CreateDirectory(packagePath);
            var entryPoint = Path.Combine(packagePath, EntryPointName);
            File.WriteAllBytes(entryPoint, [0x00]);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    entryPoint,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            File.WriteAllText(
                Path.Combine(packagePath, "config.schema.json"),
                "{\"type\":\"object\",\"additionalProperties\":false}");
            WriteManifest(packagePath, new JsonObject
            {
                ["id"] = id,
                ["name"] = "Test Plugin",
                ["version"] = version,
                ["apiVersion"] = 1,
                ["type"] = "filter",
                ["rid"] = rid ?? Rid,
                ["entryPoint"] = EntryPointName,
                ["configSchema"] = "config.schema.json",
                ["capabilities"] = new JsonArray(
                    (capabilities ?? [])
                        .Select(capability => (JsonNode?)JsonValue.Create(capability))
                        .ToArray()),
            });
            return packagePath;
        }

        public static JsonObject ReadManifest(string packagePath) =>
            JsonNode.Parse(File.ReadAllText(Path.Combine(packagePath, "plugin.json")))!
                .AsObject();

        public static void WriteManifest(string packagePath, JsonObject manifest) =>
            File.WriteAllText(
                Path.Combine(packagePath, "plugin.json"),
                manifest.ToJsonString());

        public void Dispose()
        {
            if (Directory.Exists(ParentPath))
            {
                Directory.Delete(ParentPath, recursive: true);
            }
        }

        private static string CurrentRid()
        {
            var architecture = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "x64",
                Architecture.Arm64 => "arm64",
                _ => throw new PlatformNotSupportedException(),
            };
            if (OperatingSystem.IsWindows()) return $"win-{architecture}";
            if (OperatingSystem.IsLinux()) return $"linux-{architecture}";
            if (OperatingSystem.IsMacOS() && architecture == "arm64") return "osx-arm64";
            throw new PlatformNotSupportedException();
        }
    }
}
