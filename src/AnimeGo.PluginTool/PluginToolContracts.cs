using System.Text.Json;
using System.Text.Json.Serialization;
using AnimeGoNet.App.Plugins;

namespace AnimeGo.PluginTool;

internal enum PluginToolCommandKind
{
    Help,
    Validate,
    Run,
    Pack,
}

internal sealed record PluginToolCommand(
    PluginToolCommandKind Kind,
    string? PackageDirectory,
    string? ExpectedRid,
    string? FixturePath,
    string? DataPath,
    string? OutputPath,
    TimeSpan ExecuteTimeout,
    bool Force);

internal static class PluginToolCommandParser
{
    public const string Usage = """
        Usage:
          animego-plugin validate <package-directory> [--rid <rid>]
          animego-plugin run <package-directory> --fixture <fixture.json> [--rid <rid>] [--data-path <path>] [--timeout-seconds <1..3600>]
          animego-plugin pack <package-directory> --output <package.zip> [--rid <rid>] [--force]
        """;

    public static PluginToolCommand Parse(IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0 || arguments[0] is "--help" or "-h" or "help")
        {
            return new PluginToolCommand(
                PluginToolCommandKind.Help,
                null,
                null,
                null,
                null,
                null,
                TimeSpan.FromSeconds(120),
                false);
        }
        var kind = arguments[0] switch
        {
            "validate" => PluginToolCommandKind.Validate,
            "run" => PluginToolCommandKind.Run,
            "pack" => PluginToolCommandKind.Pack,
            _ => throw UsageError("plugin_tool_command_invalid"),
        };
        string? package = null;
        string? rid = null;
        string? fixture = null;
        string? dataPath = null;
        string? output = null;
        var timeout = TimeSpan.FromSeconds(120);
        var force = false;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 1; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (argument is "--help" or "-h")
            {
                return new PluginToolCommand(
                    PluginToolCommandKind.Help,
                    null,
                    null,
                    null,
                    null,
                    null,
                    timeout,
                    false);
            }
            if (!argument.StartsWith("--", StringComparison.Ordinal))
            {
                if (package is not null)
                {
                    throw UsageError("plugin_tool_package_duplicate");
                }
                package = RequiredValue(argument, "plugin_tool_package_required");
                continue;
            }
            if (!seen.Add(argument))
            {
                throw UsageError("plugin_tool_option_duplicate");
            }
            switch (argument)
            {
                case "--force":
                    force = true;
                    break;
                case "--rid":
                    rid = NextValue(arguments, ref index, "plugin_tool_rid_required");
                    if (!ExternalPluginProtocol.SupportedRids.Contains(rid))
                    {
                        throw UsageError("plugin_tool_rid_invalid");
                    }
                    break;
                case "--fixture":
                    fixture = NextValue(arguments, ref index, "plugin_tool_fixture_required");
                    break;
                case "--data-path":
                    dataPath = NextValue(arguments, ref index, "plugin_tool_data_path_required");
                    break;
                case "--output":
                    output = NextValue(arguments, ref index, "plugin_tool_output_required");
                    break;
                case "--timeout-seconds":
                    var text = NextValue(arguments, ref index, "plugin_tool_timeout_required");
                    if (!int.TryParse(
                            text,
                            System.Globalization.NumberStyles.None,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out var seconds)
                        || seconds is < 1 or > 3600)
                    {
                        throw UsageError("plugin_tool_timeout_invalid");
                    }
                    timeout = TimeSpan.FromSeconds(seconds);
                    break;
                default:
                    throw UsageError("plugin_tool_option_unknown");
            }
        }

        if (package is null)
        {
            throw UsageError("plugin_tool_package_required");
        }
        if (kind == PluginToolCommandKind.Validate
            && (fixture is not null || dataPath is not null || output is not null || force))
        {
            throw UsageError("plugin_tool_option_not_allowed");
        }
        if (kind == PluginToolCommandKind.Run
            && (fixture is null || output is not null || force))
        {
            throw UsageError(fixture is null
                ? "plugin_tool_fixture_required"
                : "plugin_tool_option_not_allowed");
        }
        if (kind == PluginToolCommandKind.Pack
            && (output is null || fixture is not null || dataPath is not null || timeout != TimeSpan.FromSeconds(120)))
        {
            throw UsageError(output is null
                ? "plugin_tool_output_required"
                : "plugin_tool_option_not_allowed");
        }
        return new PluginToolCommand(kind, package, rid, fixture, dataPath, output, timeout, force);
    }

    private static string NextValue(
        IReadOnlyList<string> arguments,
        ref int index,
        string code)
    {
        if (++index >= arguments.Count || arguments[index].StartsWith("--", StringComparison.Ordinal))
        {
            throw UsageError(code);
        }
        return RequiredValue(arguments[index], code);
    }

    private static string RequiredValue(string value, string code) =>
        string.IsNullOrWhiteSpace(value) || value.Length > 4096
            ? throw UsageError(code)
            : value;

    private static PluginToolException UsageError(string code) =>
        new(code, "The command line is invalid. Run with --help for usage.", 2);
}

internal sealed record PluginRunFixture(
    string? Operation,
    JsonElement Payload,
    JsonElement Config);

internal sealed record PluginToolPackageOutput(
    string Id,
    string Name,
    string Version,
    int ApiVersion,
    string Type,
    string Rid,
    string EntryPoint,
    string ConfigSchema,
    IReadOnlyList<string> Capabilities,
    int FileCount,
    long TotalBytes,
    string ContentSha256);

internal sealed record PluginValidateOutput(
    bool Ok,
    string Command,
    PluginToolPackageOutput Package);

internal sealed record PluginRunOutput(
    bool Ok,
    string Command,
    PluginToolPackageOutput Package,
    bool Healthy,
    JsonElement Result);

internal sealed record PluginPackOutput(
    bool Ok,
    string Command,
    PluginToolPackageOutput Package,
    string OutputPath,
    long ArchiveBytes,
    string ArchiveSha256);

internal sealed record PluginToolErrorOutput(
    bool Ok,
    string Command,
    string Code,
    string Message);

internal sealed class PluginToolException(
    string code,
    string message,
    int exitCode,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;

    public int ExitCode { get; } = exitCode;
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    GenerationMode = JsonSourceGenerationMode.Default,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(PluginRunFixture))]
[JsonSerializable(typeof(PluginValidateOutput))]
[JsonSerializable(typeof(PluginRunOutput))]
[JsonSerializable(typeof(PluginPackOutput))]
[JsonSerializable(typeof(PluginToolErrorOutput))]
internal sealed partial class PluginToolJsonContext : JsonSerializerContext;
