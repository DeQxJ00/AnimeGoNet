using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using AnimeGoNet.App.Plugins;
using Microsoft.Extensions.Logging;

namespace AnimeGoNet.App.Tests.Plugins;

public sealed class ExternalPluginSystemProcessTests
{
    private const string SecretVariable = "ANIMEGO_PROTOCOL_TEST_SECRET";

    [Fact]
    public async Task RealProcessUsesJsonLinesLifecycleAndDoesNotInheritHostSecrets()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "animegonet-real-plugin-tests",
            Guid.NewGuid().ToString("N"));
        try
        {
            var packagePath = CreatePackage(root);
            var rid = CurrentRid();
            var loader = new ExternalPluginManifestLoader(
                Path.Combine(root, "plugins"),
                rid);
            var package = await loader.LoadPackageAsync(packagePath);
            var dataPath = Path.Combine(root, "data", "plugin-state");
            var logger = new CollectingLogger();
            var previousSecret = Environment.GetEnvironmentVariable(SecretVariable);
            Environment.SetEnvironmentVariable(SecretVariable, "must-not-leak");
            try
            {
                await using var session = new ExternalPluginProcessSession(
                    loader,
                    package,
                    dataPath,
                    new ExternalPluginSessionOptions
                    {
                        InitializeTimeout = TimeSpan.FromSeconds(10),
                        ExecuteTimeout = TimeSpan.FromSeconds(10),
                        HealthTimeout = TimeSpan.FromSeconds(10),
                        ShutdownTimeout = TimeSpan.FromSeconds(10),
                    },
                    new SystemExternalPluginProcessFactory(),
                    static () => Guid.NewGuid().ToString("N"),
                    logger,
                    TimeProvider.System);

                await session.StartAsync("1.0.0-test");
                var result = await session.ExecuteAsync(
                    "filter.environment",
                    JsonSerializer.SerializeToElement(new { test = true }),
                    JsonSerializer.SerializeToElement(new { }));

                Assert.Equal("filter.environment", result.GetProperty("operation").GetString());
                Assert.Equal("com.example.real-process", result.GetProperty("pluginId").GetString());
                Assert.Equal("1", result.GetProperty("apiVersion").GetString());
                Assert.Equal(Path.GetFullPath(dataPath), result.GetProperty("dataPath").GetString());
                Assert.Equal(JsonValueKind.Null, result.GetProperty("inheritedSecret").ValueKind);
                var pluginEnvironmentKeys = result.GetProperty("environmentKeys")
                    .EnumerateArray()
                    .Select(item => item.GetString())
                    .Where(item => item is not null
                        && item.StartsWith("ANIMEGO_", StringComparison.Ordinal));
                Assert.Equal(
                    [
                        "ANIMEGO_PLUGIN_API_VERSION",
                        "ANIMEGO_PLUGIN_DATA_PATH",
                        "ANIMEGO_PLUGIN_ID",
                    ],
                    pluginEnvironmentKeys);
                Assert.True(await session.HealthAsync());

                await session.ShutdownAsync("integration_test_complete");
                await session.DisposeAsync();

                Assert.Equal(ExternalPluginSessionState.Stopped, session.State);
                Assert.True(Directory.Exists(dataPath));
                var stderr = Assert.Single(
                    logger.Entries,
                    entry => entry.EventId.Id == 1901);
                Assert.Contains(
                    "fixture diagnostic password=<redacted>",
                    stderr.Message,
                    StringComparison.Ordinal);
                Assert.DoesNotContain(
                    "fixture-stderr-secret",
                    stderr.Message,
                    StringComparison.Ordinal);
            }
            finally
            {
                Environment.SetEnvironmentVariable(SecretVariable, previousSecret);
            }
        }
        finally
        {
            await DeleteDirectoryEventuallyAsync(root);
        }
    }

    private sealed record LogEntry(EventId EventId, string Message);

    private sealed class CollectingLogger : ILogger
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add(new LogEntry(eventId, formatter(state, exception)));
    }

    private static async Task DeleteDirectoryEventuallyAsync(string path)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (!Directory.Exists(path))
            {
                return;
            }
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (Exception exception) when (
                attempt < 49
                && exception is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(100);
            }
        }
    }

    private static string CreatePackage(string root)
    {
        var sourceDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "plugin-protocol-fixture");
        if (!Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException(
                $"The plugin protocol fixture was not copied to '{sourceDirectory}'.");
        }

        var packagePath = Path.Combine(root, "plugins", "real-process");
        Directory.CreateDirectory(packagePath);
        foreach (var source in Directory.EnumerateFiles(sourceDirectory))
        {
            File.Copy(source, Path.Combine(packagePath, Path.GetFileName(source)));
        }

        var entryPoint = OperatingSystem.IsWindows()
            ? "AnimeGoNet.PluginProtocol.Fixture.exe"
            : "AnimeGoNet.PluginProtocol.Fixture";
        var entryPointPath = Path.Combine(packagePath, entryPoint);
        if (!File.Exists(entryPointPath))
        {
            throw new FileNotFoundException(
                "The plugin protocol fixture apphost was not built.",
                entryPointPath);
        }
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                entryPointPath,
                UnixFileMode.UserRead
                    | UnixFileMode.UserWrite
                    | UnixFileMode.UserExecute);
        }

        File.WriteAllText(
            Path.Combine(packagePath, "config.schema.json"),
            "{\"type\":\"object\",\"additionalProperties\":false}");
        File.WriteAllText(
            Path.Combine(packagePath, "plugin.json"),
            new JsonObject
            {
                ["id"] = "com.example.real-process",
                ["name"] = "Real process fixture",
                ["version"] = "1.0.0",
                ["apiVersion"] = 1,
                ["type"] = "filter",
                ["rid"] = CurrentRid(),
                ["entryPoint"] = entryPoint,
                ["configSchema"] = "config.schema.json",
                ["capabilities"] = new JsonArray(),
            }.ToJsonString());
        return packagePath;
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
