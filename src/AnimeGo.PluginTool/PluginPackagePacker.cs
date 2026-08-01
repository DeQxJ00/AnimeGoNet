using System.IO.Compression;
using System.Security.Cryptography;

namespace AnimeGo.PluginTool;

internal sealed record PluginArchiveResult(
    string OutputPath,
    long Length,
    string Sha256);

internal sealed class PluginPackagePacker
{
    private readonly DateTimeOffset _stableTimestamp =
        new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public async Task<PluginArchiveResult> PackAsync(
        AuditedPluginPackage package,
        string outputPath,
        bool force,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        var output = ResolveOutputPath(outputPath);
        if (!string.Equals(Path.GetExtension(output), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid("plugin_pack_extension_invalid");
        }
        var packageRoot = Path.GetFullPath(package.Package.DirectoryPath);
        if (IsWithin(packageRoot, output))
        {
            throw Invalid("plugin_pack_output_inside_package");
        }
        if (File.Exists(output) && !force)
        {
            throw Invalid("plugin_pack_output_exists");
        }
        var outputDirectory = Path.GetDirectoryName(output)
            ?? throw Invalid("plugin_pack_output_invalid");
        Directory.CreateDirectory(outputDirectory);
        var temporary = Path.Combine(
            outputDirectory,
            $".animego-plugin-pack-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
                {
                    foreach (var file in package.Files)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        EnsureUnchanged(packageRoot, file);
                        var entry = archive.CreateEntry(
                            file.EntryName,
                            CompressionLevel.NoCompression);
                        entry.LastWriteTime = _stableTimestamp;
                        entry.ExternalAttributes = 0;
                        await using var source = new FileStream(
                            file.FullPath,
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.Read,
                            64 * 1024,
                            FileOptions.Asynchronous | FileOptions.SequentialScan);
                        await using var destination = entry.Open();
                        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                        var buffer = new byte[64 * 1024];
                        long copied = 0;
                        int read;
                        while ((read = await source.ReadAsync(buffer, cancellationToken)
                                   .ConfigureAwait(false)) > 0)
                        {
                            await destination.WriteAsync(
                                    buffer.AsMemory(0, read),
                                    cancellationToken)
                                .ConfigureAwait(false);
                            hash.AppendData(buffer, 0, read);
                            copied += read;
                        }
                        if (copied != file.Length
                            || !CryptographicOperations.FixedTimeEquals(
                                hash.GetHashAndReset(),
                                file.Sha256))
                        {
                            throw Invalid("plugin_package_changed");
                        }
                    }
                }
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            byte[] archiveHash;
            long archiveLength;
            await using (var completed = new FileStream(
                temporary,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                archiveHash = await SHA256.HashDataAsync(completed, cancellationToken)
                    .ConfigureAwait(false);
                archiveLength = completed.Length;
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, output, force);
            return new PluginArchiveResult(
                output,
                archiveLength,
                Convert.ToHexStringLower(archiveHash));
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static void EnsureUnchanged(string packageRoot, AuditedPluginFile file)
    {
        EnsureLinkFreePath(packageRoot, file.EntryName);
        var attributes = File.GetAttributes(file.FullPath);
        var info = new FileInfo(file.FullPath);
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0
            || !info.Exists
            || info.Length != file.Length)
        {
            throw Invalid("plugin_package_changed");
        }
    }

    private static void EnsureLinkFreePath(string packageRoot, string entryName)
    {
        var current = Path.GetFullPath(packageRoot);
        if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
        {
            throw Invalid("plugin_package_changed");
        }
        foreach (var segment in entryName.Split('/'))
        {
            current = Path.Combine(current, segment);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw Invalid("plugin_package_changed");
            }
        }
    }

    private static bool IsWithin(string root, string candidate)
    {
        var relative = Path.GetRelativePath(root, candidate);
        return relative != "."
            && !Path.IsPathRooted(relative)
            && relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static string ResolveOutputPath(string outputPath)
    {
        try
        {
            return Path.GetFullPath(outputPath);
        }
        catch (Exception exception) when (
            exception is IOException or ArgumentException or NotSupportedException)
        {
            throw new PluginToolException(
                "plugin_pack_output_invalid",
                "The plugin archive output path is invalid.",
                6,
                exception);
        }
    }

    private static PluginToolException Invalid(string code) =>
        new(code, "The plugin archive could not be created safely.", 6);
}
