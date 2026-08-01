using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace AnimeGoNet.App.Tests.Plugins;

internal static class ExternalPluginPackageFixture
{
    public static void Write(
        string pluginsRoot,
        string type,
        string? id = null)
    {
        id ??= $"com.example.{type}";
        var directory = Path.Combine(pluginsRoot, type);
        Directory.CreateDirectory(directory);
        var entryName = OperatingSystem.IsWindows() ? "plugin.exe" : "plugin";
        var entryPath = Path.Combine(directory, entryName);
        File.WriteAllBytes(entryPath, [0x00]);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                entryPath,
                UnixFileMode.UserRead
                    | UnixFileMode.UserWrite
                    | UnixFileMode.UserExecute);
        }
        File.WriteAllText(
            Path.Combine(directory, "config.schema.json"),
            "{\"type\":\"object\",\"additionalProperties\":false}");
        File.WriteAllText(
            Path.Combine(directory, "plugin.json"),
            new JsonObject
            {
                ["id"] = id,
                ["name"] = $"External {type}",
                ["version"] = "1.0.0",
                ["apiVersion"] = 1,
                ["type"] = type,
                ["rid"] = CurrentRid(),
                ["entryPoint"] = entryName,
                ["configSchema"] = "config.schema.json",
                ["capabilities"] = new JsonArray(),
            }.ToJsonString());
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
