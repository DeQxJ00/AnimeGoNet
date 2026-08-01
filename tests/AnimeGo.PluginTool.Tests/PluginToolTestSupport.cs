using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using AnimeGoNet.App.Plugins;

namespace AnimeGo.PluginTool.Tests;

internal sealed class PluginToolTestPackage : IDisposable
{
    private static readonly System.Text.UTF8Encoding Utf8WithoutBom = new(false);

    public PluginToolTestPackage(
        string type = "filter",
        string schema = "{\"type\":\"object\",\"additionalProperties\":false}")
    {
        RootPath = Path.Combine(
            Path.GetTempPath(),
            $"AnimeGoPluginToolTests-{Guid.NewGuid():N}");
        PackagePath = Path.Combine(RootPath, "package");
        Directory.CreateDirectory(PackagePath);
        Type = type;
        Rid = CurrentRid();
        EntryPoint = Rid.StartsWith("win-", StringComparison.Ordinal)
            ? "fixture.exe"
            : "fixture";
        File.WriteAllBytes(Path.Combine(PackagePath, EntryPoint), [0x01, 0x02, 0x03]);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                Path.Combine(PackagePath, EntryPoint),
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        File.WriteAllText(
            Path.Combine(PackagePath, "config.schema.json"),
            schema,
            Utf8WithoutBom);
        WriteManifest();
    }

    public string RootPath { get; }

    public string PackagePath { get; }

    public string Type { get; }

    public string Rid { get; }

    public string EntryPoint { get; }

    public string WriteFixture(string json, string name = "fixture.json")
    {
        var path = Path.Combine(RootPath, name);
        File.WriteAllText(path, json, Utf8WithoutBom);
        return path;
    }

    public string WriteFilterFixture(
        string config = "{}",
        string name = "fixture.json") => WriteFixture($$"""
        {
          "operation": "filter.all",
          "payload": {
            "sourceProfileId": "fixture",
            "items": [{
              "index": 7,
              "title": "Fixture episode",
              "torrentUrl": "https://tracker.example/fixture.torrent",
              "sourceUrl": null,
              "sourceItemId": "fixture-7",
              "sourceWorkId": null,
              "contentType": "application/x-bittorrent",
              "length": 123,
              "publishedAtRaw": null
            }],
            "arguments": {},
            "sourceProfileSnapshot": null
          },
          "config": {{config}}
        }
        """, name);

    public ExternalPluginManifestLoader CreateLoader() =>
        new(RootPath, Rid);

    public void WriteManifest(string version = "1.0.0")
    {
        var manifest = new JsonObject
        {
            ["id"] = $"com.example.{Type}",
            ["name"] = $"Example {Type}",
            ["version"] = version,
            ["apiVersion"] = 1,
            ["type"] = Type,
            ["rid"] = Rid,
            ["entryPoint"] = EntryPoint,
            ["configSchema"] = "config.schema.json",
            ["capabilities"] = new JsonArray(),
        };
        File.WriteAllText(
            Path.Combine(PackagePath, "plugin.json"),
            manifest.ToJsonString(),
            Utf8WithoutBom);
    }

    public void Dispose()
    {
        if (Directory.Exists(RootPath))
        {
            Directory.Delete(RootPath, recursive: true);
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

internal sealed record ToolInvocation(int ExitCode, string Output, string Error)
{
    public JsonElement OutputJson => Parse(Output);

    public JsonElement ErrorJson => Parse(Error);

    private static JsonElement Parse(string value)
    {
        using var document = JsonDocument.Parse(value);
        return document.RootElement.Clone();
    }
}

internal static class PluginToolTestDriver
{
    public static async Task<ToolInvocation> InvokeAsync(
        IReadOnlyList<string> arguments,
        PluginToolApplication? application = null,
        CancellationToken cancellationToken = default)
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        var exitCode = await (application ?? new PluginToolApplication()).RunAsync(
            arguments,
            output,
            error,
            cancellationToken);
        return new ToolInvocation(exitCode, output.ToString(), error.ToString());
    }

    public static JsonElement Json(string value)
    {
        using var document = JsonDocument.Parse(value);
        return document.RootElement.Clone();
    }
}

internal sealed class RecordingSessionFactory(JsonElement result) : IPluginToolSessionFactory
{
    public int CreateCount { get; private set; }

    public string? DataPath { get; private set; }

    public RecordingSession Session { get; } = new(result);

    public IPluginToolSession Create(
        ExternalPluginManifestLoader loader,
        ExternalPluginPackage package,
        string dataPath,
        TimeSpan executeTimeout)
    {
        _ = loader;
        _ = package;
        CreateCount++;
        DataPath = dataPath;
        Session.ExecuteTimeout = executeTimeout;
        Session.DataPathExistedAtCreation = Directory.Exists(dataPath);
        return Session;
    }
}

internal sealed class RecordingSession(JsonElement result) : IPluginToolSession
{
    public bool DataPathExistedAtCreation { get; set; }

    public TimeSpan ExecuteTimeout { get; set; }

    public bool Started { get; private set; }

    public bool Executed { get; private set; }

    public bool HealthChecked { get; private set; }

    public bool Shutdown { get; private set; }

    public bool Disposed { get; private set; }

    public bool Healthy { get; set; } = true;

    public Exception? ExecuteException { get; set; }

    public string? Operation { get; private set; }

    public JsonElement Config { get; private set; }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Started = true;
        return Task.CompletedTask;
    }

    public Task<JsonElement> ExecuteAsync(
        string operation,
        JsonElement payload,
        JsonElement config,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Executed = true;
        Operation = operation;
        Config = config.Clone();
        return ExecuteException is null
            ? Task.FromResult(result.Clone())
            : Task.FromException<JsonElement>(ExecuteException);
    }

    public Task<bool> HealthAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        HealthChecked = true;
        return Task.FromResult(Healthy);
    }

    public Task ShutdownAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Shutdown = true;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}
