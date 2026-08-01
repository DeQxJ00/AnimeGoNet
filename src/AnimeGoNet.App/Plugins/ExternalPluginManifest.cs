using System.Collections.Frozen;

namespace AnimeGoNet.App.Plugins;

public static class ExternalPluginProtocol
{
    public const int CurrentApiVersion = 1;

    public static FrozenSet<string> SupportedRids { get; } =
        new[] { "win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-arm64" }
            .ToFrozenSet(StringComparer.Ordinal);

    public static FrozenSet<string> SupportedTypes { get; } =
        new[] { "source", "feed", "parser", "filter", "rename", "schedule" }
            .ToFrozenSet(StringComparer.Ordinal);
}

public sealed record ExternalPluginManifest(
    string Id,
    string Name,
    string Version,
    int ApiVersion,
    string Type,
    string Rid,
    string EntryPoint,
    string ConfigSchema,
    IReadOnlyList<string> Capabilities);

public sealed record ExternalPluginPackage(
    ExternalPluginManifest Manifest,
    string DirectoryPath,
    string ManifestPath,
    string EntryPointPath,
    string ConfigSchemaPath);

public sealed record ExternalPluginPackageError(
    string PackageDirectoryName,
    string Code,
    string Message);

public sealed record ExternalPluginDiscoveryResult(
    IReadOnlyList<ExternalPluginPackage> Packages,
    IReadOnlyList<ExternalPluginPackageError> Errors);

public sealed class ExternalPluginManifestException(
    string code,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;
}
