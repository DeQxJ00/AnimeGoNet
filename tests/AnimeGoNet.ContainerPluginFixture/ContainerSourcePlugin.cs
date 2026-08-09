using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using AnimeGo.Plugin.Abstractions;
using AnimeGo.Plugin.Sdk;

namespace AnimeGoNet.ContainerPluginFixture;

internal sealed class ContainerSourcePlugin : ISourcePluginHandler
{
    public ValueTask<SourceIngestResult> ExecuteAsync(
        AnimeGoPluginExecutionContext<SourceIngestContext> context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var request = context.Request;
        if (string.IsNullOrWhiteSpace(request.TorrentUrl)
            || string.IsNullOrWhiteSpace(request.Title))
        {
            return ValueTask.FromResult(new SourceIngestResult(
                null,
                [new PluginOperationError(
                    "source_input_required",
                    "Torrent URL and title are required.")]));
        }

        if (!PackageDirectoryIsReadOnly())
        {
            return ValueTask.FromResult(new SourceIngestResult(
                null,
                [new PluginOperationError(
                    "package_directory_writable",
                    "The external plugin package directory must be read-only.")]));
        }

        var pluginDataPath = Environment.GetEnvironmentVariable("ANIMEGO_PLUGIN_DATA_PATH");
        if (string.IsNullOrWhiteSpace(pluginDataPath))
        {
            return ValueTask.FromResult(new SourceIngestResult(
                null,
                [new PluginOperationError(
                    "plugin_data_path_missing",
                    "The plugin data path is required.")]));
        }

        Directory.CreateDirectory(pluginDataPath);
        File.WriteAllText(
            Path.Combine(pluginDataPath, "container-smoke.txt"),
            $"uid={UnixIdentity.GetEffectiveUserId()}\npackage_read_only=true\n");

        var normalizedUrl = request.TorrentUrl.Trim();
        var fingerprint = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(normalizedUrl)));
        return ValueTask.FromResult(new SourceIngestResult(
            new SourceNormalizedItem(
                request.Source,
                normalizedUrl,
                fingerprint,
                request.Title.Trim(),
                request.SourceItemId,
                request.SourceWorkId,
                request.MikanId,
                request.BangumiId,
                request.AniDbId,
                request.ImdbId,
                request.PublishedAtRaw,
                request.PublishedAt),
            []));
    }

    private static bool PackageDirectoryIsReadOnly()
    {
        var probe = Path.Combine(AppContext.BaseDirectory, ".container-write-probe");
        try
        {
            File.WriteAllText(probe, "write must fail");
            File.Delete(probe);
            return false;
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or IOException)
        {
            return true;
        }
    }
}

internal static partial class UnixIdentity
{
    [LibraryImport("libc", EntryPoint = "geteuid")]
    internal static partial uint GetEffectiveUserId();
}
