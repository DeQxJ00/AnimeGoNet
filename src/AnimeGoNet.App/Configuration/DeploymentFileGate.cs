using System.Collections.Concurrent;

namespace AnimeGoNet.App.Configuration;

internal static class DeploymentFileGate
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    internal static SemaphoreSlim ForPath(string path) =>
        Gates.GetOrAdd(Path.GetFullPath(path), static _ => new SemaphoreSlim(1, 1));
}
