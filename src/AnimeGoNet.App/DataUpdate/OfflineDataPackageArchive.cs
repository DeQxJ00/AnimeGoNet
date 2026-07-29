using System.Buffers;
using System.IO.Compression;
using System.Security.Cryptography;
using AnimeGoNet.Core.DataUpdate;

namespace AnimeGoNet.App.DataUpdate;

internal sealed record OfflineDataPackage(
    DataManifest Manifest,
    string ManifestSha256,
    string TemporaryDirectory,
    long ArchiveBytes);

internal static class OfflineDataPackageArchive
{
    private const int BufferBytes = 64 * 1024;
    private const long ProgressCheckpointBytes = 4L * 1024 * 1024;
    private const long ArchiveOverheadBytes = 64L * 1024 * 1024;

    public const long MaximumArchiveBytes =
        (DataManifestParser.MaximumAssets * DataManifestParser.MaximumAssetBytes)
        + ArchiveOverheadBytes;

    public static async Task<OfflineDataPackage> ExtractAsync(
        Stream source,
        long? contentLength,
        string dataUpdateRoot,
        Func<long, Task> reportProgress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataUpdateRoot);
        ArgumentNullException.ThrowIfNull(reportProgress);
        if (!source.CanRead)
        {
            throw Error(
                "data_offline_archive_unreadable",
                "The offline data archive stream is not readable.");
        }
        if (contentLength is <= 0 or > MaximumArchiveBytes)
        {
            throw Error(
                "data_offline_archive_size_invalid",
                "The offline data archive size is invalid.");
        }

        Directory.CreateDirectory(dataUpdateRoot);
        var temporary = Path.Combine(dataUpdateRoot, $".partial-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporary);
        var archivePath = Path.Combine(temporary, "package.zip");
        try
        {
            var archiveBytes = await CopyArchiveAsync(
                source,
                archivePath,
                contentLength,
                reportProgress,
                cancellationToken).ConfigureAwait(false);
            byte[] manifestBytes;
            DataManifest manifest;
            await using (var archiveFile = new FileStream(
                archivePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                BufferBytes,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                try
                {
                    using var archive = new ZipArchive(
                        archiveFile,
                        ZipArchiveMode.Read,
                        leaveOpen: true);
                    (manifest, manifestBytes) = await ExtractEntriesAsync(
                        archive,
                        temporary,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (DataUpdateServiceException)
                {
                    throw;
                }
                catch (InvalidDataException exception)
                {
                    throw Error(
                        "data_offline_archive_invalid",
                        "The offline data archive is not a valid ZIP package.",
                        exception);
                }
                catch (NotSupportedException exception)
                {
                    throw Error(
                        "data_offline_archive_invalid",
                        "The offline data archive uses an unsupported ZIP feature.",
                        exception);
                }
            }

            File.Delete(archivePath);
            await File.WriteAllBytesAsync(
                Path.Combine(temporary, "manifest.json"),
                manifestBytes,
                cancellationToken).ConfigureAwait(false);
            return new OfflineDataPackage(
                manifest,
                Convert.ToHexStringLower(SHA256.HashData(manifestBytes)),
                temporary,
                archiveBytes);
        }
        catch
        {
            BestEffortDelete(temporary, dataUpdateRoot);
            throw;
        }
    }

    private static async Task<long> CopyArchiveAsync(
        Stream source,
        string archivePath,
        long? contentLength,
        Func<long, Task> reportProgress,
        CancellationToken cancellationToken)
    {
        await using var destination = new FileStream(
            archivePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            BufferBytes,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var rented = ArrayPool<byte>.Shared.Rent(BufferBytes);
        long copied = 0;
        long lastReported = 0;
        try
        {
            while (true)
            {
                var read = await source.ReadAsync(rented, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }
                copied = checked(copied + read);
                if (copied > MaximumArchiveBytes)
                {
                    throw Error(
                        "data_offline_archive_size_invalid",
                        "The offline data archive exceeds the supported size.");
                }
                await destination.WriteAsync(
                    rented.AsMemory(0, read),
                    cancellationToken).ConfigureAwait(false);
                if (copied - lastReported >= ProgressCheckpointBytes)
                {
                    await reportProgress(copied).ConfigureAwait(false);
                    lastReported = copied;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        if (copied == 0 || (contentLength is not null && copied != contentLength.Value))
        {
            throw Error(
                "data_offline_archive_size_invalid",
                "The offline data archive length does not match the request.");
        }
        await reportProgress(copied).ConfigureAwait(false);
        return copied;
    }

    private static async Task<(DataManifest Manifest, byte[] ManifestBytes)> ExtractEntriesAsync(
        ZipArchive archive,
        string destination,
        CancellationToken cancellationToken)
    {
        if (archive.Entries.Count is < 3 or > DataManifestParser.MaximumAssets + 1)
        {
            throw Error(
                "data_offline_archive_entries_invalid",
                "The offline data archive entry count is invalid.");
        }
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.FullName)
                || entry.FullName.Length > 256
                || !string.Equals(entry.FullName, entry.Name, StringComparison.Ordinal)
                || entry.FullName.Contains('\\', StringComparison.Ordinal)
                || entry.FullName.Contains('/', StringComparison.Ordinal)
                || entry.FullName.Any(char.IsControl)
                || !entries.TryAdd(entry.FullName, entry))
            {
                throw Error(
                    "data_offline_archive_path_invalid",
                    "The offline data archive contains an unsafe or duplicate entry path.");
            }
        }
        if (!entries.TryGetValue("manifest.json", out var manifestEntry)
            || manifestEntry.Length is <= 0 or > DataManifestParser.MaximumManifestBytes)
        {
            throw Error(
                "data_offline_manifest_invalid",
                "The offline data archive manifest is missing or invalid.");
        }

        byte[] manifestBytes;
        await using (var manifestStream = manifestEntry.Open())
        {
            manifestBytes = await ReadBoundedAsync(
                manifestStream,
                DataManifestParser.MaximumManifestBytes,
                cancellationToken).ConfigureAwait(false);
        }
        DataManifest manifest;
        try
        {
            manifest = DataManifestParser.Parse(manifestBytes);
        }
        catch (DataManifestException exception)
        {
            throw Error(
                "data_offline_manifest_invalid",
                "The offline data archive manifest is invalid.",
                exception);
        }

        var expectedEntries = new HashSet<string>(
            manifest.Assets.Select(asset => asset.FileName),
            StringComparer.Ordinal)
        {
            "manifest.json",
        };
        if (entries.Count != expectedEntries.Count
            || expectedEntries.Any(expected => !entries.ContainsKey(expected)))
        {
            throw Error(
                "data_offline_archive_entries_invalid",
                "The offline data archive must contain exactly its manifest and declared assets.");
        }

        foreach (var asset in manifest.Assets)
        {
            var entry = entries[asset.FileName];
            if (entry.Length != asset.SizeBytes)
            {
                throw Error(
                    "data_offline_asset_size_mismatch",
                    "An offline data asset size does not match the manifest.");
            }
            await ExtractAssetAsync(
                entry,
                asset,
                Path.Combine(destination, asset.FileName),
                cancellationToken).ConfigureAwait(false);
        }
        return (manifest, manifestBytes);
    }

    private static async Task ExtractAssetAsync(
        ZipArchiveEntry entry,
        DataManifestAsset asset,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        await using var source = entry.Open();
        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            BufferBytes,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var rented = ArrayPool<byte>.Shared.Rent(BufferBytes);
        long extracted = 0;
        try
        {
            while (true)
            {
                var read = await source.ReadAsync(rented, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }
                extracted = checked(extracted + read);
                if (extracted > asset.SizeBytes)
                {
                    throw Error(
                        "data_offline_asset_size_mismatch",
                        "An offline data asset exceeds its declared size.");
                }
                hash.AppendData(rented, 0, read);
                await destination.WriteAsync(
                    rented.AsMemory(0, read),
                    cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        if (extracted != asset.SizeBytes)
        {
            throw Error(
                "data_offline_asset_size_mismatch",
                "An offline data asset length does not match the manifest.");
        }
        if (!string.Equals(
                Convert.ToHexStringLower(hash.GetHashAndReset()),
                asset.Sha256,
                StringComparison.Ordinal))
        {
            throw Error(
                "data_offline_asset_sha256_mismatch",
                "An offline data asset SHA-256 does not match the manifest.");
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(
        Stream stream,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream(Math.Min(maximumBytes, BufferBytes));
        var rented = ArrayPool<byte>.Shared.Rent(BufferBytes);
        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(rented, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }
                if (buffer.Length + read > maximumBytes)
                {
                    throw Error(
                        "data_offline_manifest_invalid",
                        "The offline data archive manifest is too large.");
                }
                await buffer.WriteAsync(
                    rented.AsMemory(0, read),
                    cancellationToken).ConfigureAwait(false);
            }
            if (buffer.Length == 0)
            {
                throw Error(
                    "data_offline_manifest_invalid",
                    "The offline data archive manifest is empty.");
            }
            return buffer.ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static void BestEffortDelete(string path, string dataUpdateRoot)
    {
        try
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataUpdateRoot));
            var target = Path.GetFullPath(path);
            var prefix = root + Path.DirectorySeparatorChar;
            if (target.StartsWith(
                    prefix,
                    OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal)
                && Path.GetFileName(target).StartsWith(".partial-", StringComparison.Ordinal))
            {
                Directory.Delete(target, recursive: true);
            }
        }
        catch (Exception)
        {
            // Cleanup is best effort and never masks the archive validation result.
        }
    }

    private static DataUpdateServiceException Error(
        string code,
        string message,
        Exception? innerException = null) =>
        new(code, message, innerException);
}
