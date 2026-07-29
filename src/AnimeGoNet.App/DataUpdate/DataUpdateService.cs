using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.DataUpdate;
using AnimeGoNet.Data.DataUpdate;

namespace AnimeGoNet.App.DataUpdate;

public sealed class DataUpdateService : IDataUpdateService, IDisposable
{
    private const int DownloadBufferBytes = 64 * 1024;
    private const long ProgressCheckpointBytes = 4L * 1024 * 1024;
    private readonly HttpClient _httpClient;
    private readonly AnimeGoOptions _options;
    private readonly DirectoryLayout _layout;
    private readonly DataPackageStore _packages;
    private readonly DataUpdateTransferStore _transfers;
    private readonly TimeProvider _timeProvider;
    private readonly Version _clientVersion;
    private readonly bool _ownsHttpClient;
    private readonly SemaphoreSlim _operationGate = new(1, 1);

    public DataUpdateService(
        HttpClient httpClient,
        AnimeGoOptions options,
        DirectoryLayout layout,
        DataPackageStore packages,
        DataUpdateTransferStore transfers,
        TimeProvider? timeProvider = null,
        Version? clientVersion = null,
        bool ownsHttpClient = false)
    {
        _httpClient = httpClient;
        _options = options;
        _layout = layout;
        _packages = packages;
        _transfers = transfers;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _clientVersion = clientVersion
            ?? Assembly.GetExecutingAssembly().GetName().Version
            ?? new Version(0, 0, 0);
        _ownsHttpClient = ownsHttpClient;
    }

    public async Task<DataUpdateExecutionResult> ExecuteAsync(
        string triggerKind,
        string requestedAction,
        CancellationToken cancellationToken = default)
    {
        if (!await _operationGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            throw Error("data_update_busy", "Another data update operation is already running.");
        }

        var now = _timeProvider.GetUtcNow();
        string? runId = null;
        long downloadedBytes = 0;
        long totalBytes = 0;
        try
        {
            runId = await _transfers.StartAsync(
                triggerKind,
                requestedAction,
                now,
                cancellationToken).ConfigureAwait(false);
            var manifestUrl = _options.DataUpdate.ManifestUrl
                ?? throw Error(
                    "data_manifest_url_missing",
                    "A data update manifest URL is not configured.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.DataUpdate.HttpTimeout);
            var manifestBytes = await DownloadManifestAsync(manifestUrl, timeout.Token)
                .ConfigureAwait(false);
            var manifestSha256 = Convert.ToHexStringLower(SHA256.HashData(manifestBytes));
            DataManifest manifest;
            try
            {
                manifest = DataManifestParser.Parse(manifestBytes);
            }
            catch (DataManifestException exception)
            {
                throw Error(exception.Code, exception.Message, exception);
            }

            var packageStatus = await _packages.GetStatusAsync(timeout.Token).ConfigureAwait(false);
            if (string.Equals(
                    packageStatus.ActiveVersion,
                    manifest.DataVersion,
                    StringComparison.Ordinal))
            {
                await _transfers.CompleteAsync(
                    runId,
                    DataUpdateTransferStatuses.UpToDate,
                    manifest.DataVersion,
                    manifestSha256,
                    0,
                    0,
                    _timeProvider.GetUtcNow(),
                    timeout.Token).ConfigureAwait(false);
                return new DataUpdateExecutionResult(
                    runId,
                    DataUpdateTransferStatuses.UpToDate,
                    manifest.DataVersion,
                    packageStatus.ActiveVersion,
                    false,
                    false);
            }

            if (requestedAction == DataUpdateActions.Check)
            {
                await _transfers.CompleteAsync(
                    runId,
                    DataUpdateTransferStatuses.UpdateAvailable,
                    manifest.DataVersion,
                    manifestSha256,
                    0,
                    0,
                    _timeProvider.GetUtcNow(),
                    timeout.Token).ConfigureAwait(false);
                return new DataUpdateExecutionResult(
                    runId,
                    DataUpdateTransferStatuses.UpdateAvailable,
                    manifest.DataVersion,
                    packageStatus.ActiveVersion,
                    false,
                    false);
            }

            totalBytes = CheckedTotalBytes(manifest);
            await _transfers.SetStageAsync(
                runId,
                DataUpdateTransferStatuses.Downloading,
                manifest.DataVersion,
                manifestSha256,
                0,
                totalBytes,
                timeout.Token).ConfigureAwait(false);
            var download = await _transfers.GetDownloadAsync(manifest.DataVersion, timeout.Token)
                .ConfigureAwait(false);
            string packageDirectory;
            if (download is not null
                && string.Equals(
                    download.ManifestSha256,
                    manifestSha256,
                    StringComparison.Ordinal))
            {
                packageDirectory = ResolveManagedPackageDirectory(download.RelativeDirectory);
                if (!Directory.Exists(packageDirectory))
                {
                    throw Error(
                        "data_download_catalog_missing",
                        "A verified data package is missing from local storage.");
                }
                downloadedBytes = totalBytes;
                await _transfers.SetProgressAsync(
                    runId,
                    downloadedBytes,
                    totalBytes,
                    timeout.Token).ConfigureAwait(false);
            }
            else
            {
                packageDirectory = await DownloadPackageAsync(
                    manifest,
                    manifestBytes,
                    manifestSha256,
                    totalBytes,
                    progress =>
                    {
                        downloadedBytes = progress;
                        return _transfers.SetProgressAsync(
                            runId,
                            progress,
                            totalBytes,
                            timeout.Token);
                    },
                    timeout.Token).ConfigureAwait(false);
                downloadedBytes = totalBytes;
                await _transfers.SaveDownloadAsync(
                    new DownloadedDataPackage(
                        manifest.DataVersion,
                        manifestSha256,
                        Path.GetRelativePath(_layout.DataUpdatePath, packageDirectory),
                        "verified",
                        _timeProvider.GetUtcNow(),
                        null),
                    timeout.Token).ConfigureAwait(false);
            }

            if (requestedAction == DataUpdateActions.Download)
            {
                await _transfers.CompleteAsync(
                    runId,
                    DataUpdateTransferStatuses.Downloaded,
                    manifest.DataVersion,
                    manifestSha256,
                    downloadedBytes,
                    totalBytes,
                    _timeProvider.GetUtcNow(),
                    timeout.Token).ConfigureAwait(false);
                return new DataUpdateExecutionResult(
                    runId,
                    DataUpdateTransferStatuses.Downloaded,
                    manifest.DataVersion,
                    packageStatus.ActiveVersion,
                    true,
                    false);
            }

            await _transfers.SetStageAsync(
                runId,
                DataUpdateTransferStatuses.Importing,
                manifest.DataVersion,
                manifestSha256,
                downloadedBytes,
                totalBytes,
                timeout.Token).ConfigureAwait(false);
            var import = await _packages.ImportAsync(
                new DataPackageImportRequest(
                    manifest,
                    manifestSha256,
                    packageDirectory,
                    _clientVersion,
                    _options.DataUpdate.KeepVersions,
                    _timeProvider.GetUtcNow()),
                timeout.Token).ConfigureAwait(false);
            await _transfers.MarkImportedAsync(
                manifest.DataVersion,
                _timeProvider.GetUtcNow(),
                timeout.Token).ConfigureAwait(false);
            await _transfers.CompleteAsync(
                runId,
                DataUpdateTransferStatuses.Completed,
                manifest.DataVersion,
                manifestSha256,
                downloadedBytes,
                totalBytes,
                _timeProvider.GetUtcNow(),
                timeout.Token).ConfigureAwait(false);
            return new DataUpdateExecutionResult(
                runId,
                DataUpdateTransferStatuses.Completed,
                manifest.DataVersion,
                import.DataVersion,
                true,
                true);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            if (runId is not null)
            {
                await BestEffortFailAsync(
                    runId,
                    "data_update_timeout",
                    downloadedBytes,
                    totalBytes).ConfigureAwait(false);
            }
            throw Error("data_update_timeout", "The data update operation timed out.", exception);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (runId is not null)
            {
                await BestEffortFailAsync(
                    runId,
                    "data_update_cancelled",
                    downloadedBytes,
                    totalBytes).ConfigureAwait(false);
            }
            throw;
        }
        catch (DataUpdateServiceException exception)
        {
            if (runId is not null)
            {
                await BestEffortFailAsync(
                    runId,
                    exception.Code,
                    downloadedBytes,
                    totalBytes).ConfigureAwait(false);
            }
            throw;
        }
        catch (DataPackageException exception)
        {
            if (runId is not null)
            {
                await BestEffortFailAsync(
                    runId,
                    exception.Code,
                    downloadedBytes,
                    totalBytes).ConfigureAwait(false);
            }
            throw Error(exception.Code, exception.Message, exception);
        }
        catch (HttpRequestException exception)
        {
            if (runId is not null)
            {
                await BestEffortFailAsync(
                    runId,
                    "data_update_network_failed",
                    downloadedBytes,
                    totalBytes).ConfigureAwait(false);
            }
            throw Error(
                "data_update_network_failed",
                "The data update HTTP request failed.",
                exception);
        }
        catch (IOException exception)
        {
            if (runId is not null)
            {
                await BestEffortFailAsync(
                    runId,
                    "data_update_storage_failed",
                    downloadedBytes,
                    totalBytes).ConfigureAwait(false);
            }
            throw Error(
                "data_update_storage_failed",
                "The data update package could not be stored.",
                exception);
        }
        catch (Exception exception)
        {
            if (runId is not null)
            {
                await BestEffortFailAsync(
                    runId,
                    "data_update_failed",
                    downloadedBytes,
                    totalBytes).ConfigureAwait(false);
            }
            throw Error("data_update_failed", "The data update operation failed.", exception);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<DataUpdateExecutionResult> ImportDownloadedAsync(
        string dataVersion,
        string triggerKind = DataUpdateTriggerKinds.Manual,
        CancellationToken cancellationToken = default)
    {
        if (!await _operationGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            throw Error("data_update_busy", "Another data update operation is already running.");
        }

        string? runId = null;
        try
        {
            runId = await _transfers.StartAsync(
                triggerKind,
                DataUpdateActions.DownloadImport,
                _timeProvider.GetUtcNow(),
                cancellationToken).ConfigureAwait(false);
            var download = await _transfers.GetDownloadAsync(dataVersion, cancellationToken)
                .ConfigureAwait(false)
                ?? throw Error(
                    "data_download_not_found",
                    "The requested verified data package is not available.");
            var packageDirectory = ResolveManagedPackageDirectory(download.RelativeDirectory);
            var manifestPath = Path.Combine(packageDirectory, "manifest.json");
            var manifestBytes = await ReadLocalManifestAsync(manifestPath, cancellationToken)
                .ConfigureAwait(false);
            var manifestSha = Convert.ToHexStringLower(SHA256.HashData(manifestBytes));
            if (!string.Equals(manifestSha, download.ManifestSha256, StringComparison.Ordinal))
            {
                throw Error(
                    "data_download_manifest_changed",
                    "The downloaded data package manifest has changed.");
            }
            var manifest = DataManifestParser.Parse(manifestBytes);
            if (!string.Equals(manifest.DataVersion, dataVersion, StringComparison.Ordinal))
            {
                throw Error(
                    "data_download_version_mismatch",
                    "The downloaded data package version does not match its catalog entry.");
            }
            var totalBytes = CheckedTotalBytes(manifest);
            await _transfers.SetStageAsync(
                runId,
                DataUpdateTransferStatuses.Importing,
                dataVersion,
                manifestSha,
                totalBytes,
                totalBytes,
                cancellationToken).ConfigureAwait(false);
            var import = await _packages.ImportAsync(
                new DataPackageImportRequest(
                    manifest,
                    manifestSha,
                    packageDirectory,
                    _clientVersion,
                    _options.DataUpdate.KeepVersions,
                    _timeProvider.GetUtcNow()),
                cancellationToken).ConfigureAwait(false);
            await _transfers.MarkImportedAsync(
                dataVersion,
                _timeProvider.GetUtcNow(),
                cancellationToken).ConfigureAwait(false);
            await _transfers.CompleteAsync(
                runId,
                DataUpdateTransferStatuses.Completed,
                dataVersion,
                manifestSha,
                totalBytes,
                totalBytes,
                _timeProvider.GetUtcNow(),
                cancellationToken).ConfigureAwait(false);
            return new DataUpdateExecutionResult(
                runId,
                DataUpdateTransferStatuses.Completed,
                dataVersion,
                import.DataVersion,
                true,
                true);
        }
        catch (DataManifestException exception)
        {
            if (runId is not null)
            {
                await BestEffortFailAsync(runId, exception.Code, 0, 0).ConfigureAwait(false);
            }
            throw Error(exception.Code, exception.Message, exception);
        }
        catch (DataUpdateServiceException exception)
        {
            if (runId is not null)
            {
                await BestEffortFailAsync(runId, exception.Code, 0, 0).ConfigureAwait(false);
            }
            throw;
        }
        catch (DataPackageException exception)
        {
            if (runId is not null)
            {
                await BestEffortFailAsync(runId, exception.Code, 0, 0).ConfigureAwait(false);
            }
            throw Error(exception.Code, exception.Message, exception);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (runId is not null)
            {
                await BestEffortFailAsync(runId, "data_update_cancelled", 0, 0)
                    .ConfigureAwait(false);
            }
            throw;
        }
        catch (IOException exception)
        {
            if (runId is not null)
            {
                await BestEffortFailAsync(runId, "data_update_storage_failed", 0, 0)
                    .ConfigureAwait(false);
            }
            throw Error(
                "data_update_storage_failed",
                "The downloaded data package could not be read.",
                exception);
        }
        catch (Exception exception)
        {
            if (runId is not null)
            {
                await BestEffortFailAsync(runId, "data_update_import_failed", 0, 0)
                    .ConfigureAwait(false);
            }
            throw Error(
                "data_update_import_failed",
                "The downloaded data package could not be imported.",
                exception);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task<byte[]> DownloadManifestAsync(
        Uri manifestUrl,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, manifestUrl);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            throw Error("data_manifest_not_found", "The data update manifest was not found.");
        }
        if (!response.IsSuccessStatusCode)
        {
            throw Error(
                "data_manifest_http_failed",
                "The data update manifest server returned an unsuccessful response.");
        }
        if (response.Content.Headers.ContentLength is > DataManifestParser.MaximumManifestBytes)
        {
            throw Error("data_manifest_size_invalid", "The data manifest is too large.");
        }
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        return await ReadBoundedAsync(
            stream,
            DataManifestParser.MaximumManifestBytes,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> DownloadPackageAsync(
        DataManifest manifest,
        byte[] manifestBytes,
        string manifestSha256,
        long totalBytes,
        Func<long, Task> reportProgress,
        CancellationToken cancellationToken)
    {
        var packagesRoot = Path.Combine(_layout.DataUpdatePath, "packages");
        Directory.CreateDirectory(packagesRoot);
        var temporary = Path.Combine(
            _layout.DataUpdatePath,
            $".partial-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporary);
        var downloaded = 0L;
        try
        {
            foreach (var asset in manifest.Assets)
            {
                var path = Path.Combine(temporary, asset.FileName);
                var assetBytes = await DownloadAssetAsync(
                    asset,
                    path,
                    downloaded,
                    totalBytes,
                    reportProgress,
                    cancellationToken).ConfigureAwait(false);
                downloaded = checked(downloaded + assetBytes);
                await reportProgress(downloaded).ConfigureAwait(false);
            }
            if (downloaded != totalBytes)
            {
                throw Error(
                    "data_download_size_mismatch",
                    "Downloaded data package size does not match the manifest.");
            }
            await File.WriteAllBytesAsync(
                Path.Combine(temporary, "manifest.json"),
                manifestBytes,
                cancellationToken).ConfigureAwait(false);

            var final = Path.Combine(packagesRoot, manifest.DataVersion);
            if (Directory.Exists(final))
            {
                var catalog = await _transfers.GetDownloadAsync(
                    manifest.DataVersion,
                    cancellationToken).ConfigureAwait(false);
                if (catalog is not null
                    && !string.Equals(
                        catalog.ManifestSha256,
                        manifestSha256,
                        StringComparison.Ordinal))
                {
                    throw Error(
                        "data_version_immutable_conflict",
                        "A different package already exists for this data version.");
                }
                var existingManifest = await ReadLocalManifestAsync(
                    Path.Combine(final, "manifest.json"),
                    cancellationToken).ConfigureAwait(false);
                if (!string.Equals(
                        Convert.ToHexStringLower(SHA256.HashData(existingManifest)),
                        manifestSha256,
                        StringComparison.Ordinal))
                {
                    throw Error(
                        "data_version_immutable_conflict",
                        "A different package already exists for this data version.");
                }
                BestEffortDeleteTemporaryDirectory(temporary);
                return final;
            }
            Directory.Move(temporary, final);
            return final;
        }
        catch
        {
            BestEffortDeleteTemporaryDirectory(temporary);
            throw;
        }
    }

    private async Task<long> DownloadAssetAsync(
        DataManifestAsset asset,
        string destinationPath,
        long completedBefore,
        long totalBytes,
        Func<long, Task> reportProgress,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, asset.Url);
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw Error(
                "data_asset_http_failed",
                "A data update asset server returned an unsuccessful response.");
        }
        if (response.Content.Headers.ContentLength is { } contentLength
            && contentLength != asset.SizeBytes)
        {
            throw Error(
                "data_asset_size_mismatch",
                "A data update asset Content-Length does not match the manifest.");
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            DownloadBufferBytes,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var rented = ArrayPool<byte>.Shared.Rent(DownloadBufferBytes);
        long downloaded = 0;
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
                downloaded = checked(downloaded + read);
                if (downloaded > asset.SizeBytes)
                {
                    throw Error(
                        "data_asset_size_mismatch",
                        "A downloaded data asset exceeds its declared size.");
                }
                hash.AppendData(rented, 0, read);
                await destination.WriteAsync(rented.AsMemory(0, read), cancellationToken)
                    .ConfigureAwait(false);
                if (downloaded - lastReported >= ProgressCheckpointBytes)
                {
                    await reportProgress(completedBefore + downloaded).ConfigureAwait(false);
                    lastReported = downloaded;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        if (downloaded != asset.SizeBytes)
        {
            throw Error(
                "data_asset_size_mismatch",
                "A downloaded data asset is shorter than its declared size.");
        }
        if (!string.Equals(
                Convert.ToHexStringLower(hash.GetHashAndReset()),
                asset.Sha256,
                StringComparison.Ordinal))
        {
            throw Error(
                "data_asset_sha256_mismatch",
                "A downloaded data asset SHA-256 does not match the manifest.");
        }
        return downloaded;
    }

    private string ResolveManagedPackageDirectory(string relativeDirectory)
    {
        if (Path.IsPathRooted(relativeDirectory))
        {
            throw Error(
                "data_download_path_invalid",
                "The downloaded package catalog contains an absolute path.");
        }
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_layout.DataUpdatePath));
        var candidate = Path.GetFullPath(Path.Combine(root, relativeDirectory));
        var prefix = root + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(
                prefix,
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal))
        {
            throw Error(
                "data_download_path_invalid",
                "The downloaded package path escapes the data update directory.");
        }
        return candidate;
    }

    private static async Task<byte[]> ReadLocalManifestAsync(
        string manifestPath,
        CancellationToken cancellationToken)
    {
        var file = new FileInfo(manifestPath);
        if (!file.Exists
            || (file.Attributes & FileAttributes.ReparsePoint) != 0
            || file.Length is <= 0 or > DataManifestParser.MaximumManifestBytes)
        {
            throw Error(
                "data_download_manifest_invalid",
                "The downloaded package manifest is missing or invalid.");
        }
        await using var stream = new FileStream(
            manifestPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            DownloadBufferBytes,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await ReadBoundedAsync(
            stream,
            DataManifestParser.MaximumManifestBytes,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<byte[]> ReadBoundedAsync(
        Stream stream,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream(Math.Min(maximumBytes, 64 * 1024));
        var rented = ArrayPool<byte>.Shared.Rent(DownloadBufferBytes);
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
                    throw Error("data_manifest_size_invalid", "The data manifest is too large.");
                }
                await buffer.WriteAsync(rented.AsMemory(0, read), cancellationToken)
                    .ConfigureAwait(false);
            }
            if (buffer.Length == 0)
            {
                throw Error("data_manifest_size_invalid", "The data manifest is empty.");
            }
            return buffer.ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static long CheckedTotalBytes(DataManifest manifest)
    {
        try
        {
            return manifest.Assets.Aggregate(
                0L,
                (total, asset) => checked(total + asset.SizeBytes));
        }
        catch (OverflowException exception)
        {
            throw Error(
                "data_package_size_overflow",
                "The data package total size exceeds supported bounds.",
                exception);
        }
    }

    private async Task BestEffortFailAsync(
        string runId,
        string failureCode,
        long downloadedBytes,
        long totalBytes)
    {
        try
        {
            await _transfers.FailAsync(
                runId,
                failureCode,
                Math.Min(downloadedBytes, totalBytes),
                totalBytes,
                _timeProvider.GetUtcNow()).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Preserve the original transfer error when audit persistence also fails.
        }
    }

    private void BestEffortDeleteTemporaryDirectory(string path)
    {
        try
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_layout.DataUpdatePath));
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
            // Temporary package cleanup is best effort and never masks the transfer result.
        }
    }

    private static DataUpdateServiceException Error(
        string code,
        string message,
        Exception? innerException = null) =>
        new(code, message, innerException);

    public void Dispose()
    {
        _operationGate.Dispose();
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
