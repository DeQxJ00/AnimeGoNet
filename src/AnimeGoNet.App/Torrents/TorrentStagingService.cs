using System.Buffers;
using System.Net;
using System.Security.Cryptography;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Torrents;

namespace AnimeGoNet.App.Torrents;

public sealed class TorrentStagingService(
    DirectoryLayout layout,
    TorrentFetchOptions options,
    ITorrentDnsResolver dnsResolver,
    ITorrentHttpTransport transport,
    TimeProvider? timeProvider = null) : ITorrentStagingService
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<StagedTorrent> StageAsync(
        Uri secretUrl,
        TorrentSourcePolicy sourcePolicy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(secretUrl);
        ArgumentNullException.ThrowIfNull(sourcePolicy);
        Directory.CreateDirectory(layout.StagingPath);
        var partPath = Path.Combine(layout.StagingPath, $"stage-{Guid.NewGuid():N}.part");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.Timeout);

        try
        {
            var current = secretUrl;
            for (var redirectCount = 0; ; redirectCount++)
            {
                ValidateUrl(current, sourcePolicy);
                var addresses = await ResolveAndValidateAsync(current.IdnHost, timeout.Token).ConfigureAwait(false);
                await using var response = await SendSafelyAsync(current, addresses, timeout.Token).ConfigureAwait(false);
                if (IsRedirect(response.StatusCode))
                {
                    if (redirectCount >= options.MaxRedirects)
                    {
                        throw Failure(TorrentStagingFailureCode.TooManyRedirects, "Torrent redirect limit was exceeded.");
                    }

                    current = ResolveRedirect(current, response.RedirectLocation);
                    continue;
                }

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    throw Failure(TorrentStagingFailureCode.ResponseRejected, "Torrent source returned an unsuccessful response.");
                }

                if (response.ContentLength is > 0 && response.ContentLength > options.MaxResponseBytes)
                {
                    throw Failure(TorrentStagingFailureCode.ResponseTooLarge, "Torrent response exceeded the configured size limit.");
                }

                await WriteLimitedAsync(response.Content, partPath, timeout.Token).ConfigureAwait(false);
                break;
            }

            TorrentMetadata metadata;
            byte[]? bytes = null;
            try
            {
                bytes = await File.ReadAllBytesAsync(partPath, timeout.Token).ConfigureAwait(false);
                metadata = TorrentMetainfoParser.Parse(bytes);
            }
            catch (TorrentMetainfoException exception)
            {
                throw Failure(TorrentStagingFailureCode.InvalidTorrent, "Torrent response failed metainfo validation.", exception);
            }
            finally
            {
                if (bytes is not null)
                {
                    CryptographicOperations.ZeroMemory(bytes);
                }
            }

            var finalPath = Path.Combine(layout.StagingPath, $"{metadata.InfoHash}-{Guid.NewGuid():N}.torrent");
            File.Move(partPath, finalPath);
            return new StagedTorrent(finalPath, metadata);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            DeleteIfPresent(partPath);
            throw Failure(TorrentStagingFailureCode.Timeout, "Torrent staging timed out.", exception);
        }
        catch
        {
            DeleteIfPresent(partPath);
            throw;
        }
    }

    public Task<int> CleanupExpiredAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(layout.StagingPath))
        {
            return Task.FromResult(0);
        }

        var cutoff = _timeProvider.GetUtcNow() - options.StagingTtl;
        var deleted = 0;
        foreach (var path in Directory.EnumerateFiles(layout.StagingPath, "*", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!path.EndsWith(".part", StringComparison.OrdinalIgnoreCase)
                && !path.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (File.GetLastWriteTimeUtc(path) >= cutoff.UtcDateTime)
            {
                continue;
            }

            try
            {
                File.Delete(path);
                deleted++;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return Task.FromResult(deleted);
    }

    public Task<bool> DeleteAsync(string stagingFileName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(stagingFileName)
            || !string.Equals(stagingFileName, Path.GetFileName(stagingFileName), StringComparison.Ordinal)
            || (!stagingFileName.EndsWith(".part", StringComparison.OrdinalIgnoreCase)
                && !stagingFileName.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Staging file name is invalid.", nameof(stagingFileName));
        }

        var path = Path.Combine(layout.StagingPath, stagingFileName);
        if (!File.Exists(path))
        {
            return Task.FromResult(false);
        }

        File.Delete(path);
        return Task.FromResult(true);
    }

    private async Task<IReadOnlyList<IPAddress>> ResolveAndValidateAsync(string host, CancellationToken cancellationToken)
    {
        IReadOnlyList<IPAddress> addresses;
        try
        {
            addresses = await dnsResolver.ResolveAsync(host, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not TorrentStagingException)
        {
            _ = exception;
            throw Failure(TorrentStagingFailureCode.NetworkFailure, "Torrent host resolution failed.");
        }

        if (addresses.Count == 0 || addresses.Any(address => !TorrentNetworkPolicy.IsPublicAddress(address)))
        {
            throw Failure(TorrentStagingFailureCode.AddressNotAllowed, "Torrent host resolved to a prohibited address.");
        }

        return addresses;
    }

    private async ValueTask<TorrentHttpResponse> SendSafelyAsync(
        Uri uri,
        IReadOnlyList<IPAddress> addresses,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transport.SendAsync(uri, addresses, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not TorrentStagingException)
        {
            _ = exception;
            throw Failure(TorrentStagingFailureCode.NetworkFailure, "Torrent HTTP request failed.");
        }
    }

    private async Task WriteLimitedAsync(Stream source, string path, CancellationToken cancellationToken)
    {
        var streamOptions = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            BufferSize = 64 * 1024,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
        };
        if (!OperatingSystem.IsWindows())
        {
            streamOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        await using var destination = new FileStream(path, streamOptions);
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            long written = 0;
            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                written += read;
                if (written > options.MaxResponseBytes)
                {
                    throw Failure(TorrentStagingFailureCode.ResponseTooLarge, "Torrent response exceeded the configured size limit.");
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }

            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    private static void ValidateUrl(Uri uri, TorrentSourcePolicy sourcePolicy)
    {
        if (!uri.IsAbsoluteUri
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw Failure(TorrentStagingFailureCode.InvalidUrl, "Torrent URL must be an absolute HTTP(S) URL without userinfo or fragment.");
        }

        if (!TorrentNetworkPolicy.IsHostAllowed(uri.IdnHost, sourcePolicy.AllowedHosts))
        {
            throw Failure(TorrentStagingFailureCode.HostNotAllowed, "Torrent host is not allowed by the source profile.");
        }
    }

    private static Uri ResolveRedirect(Uri current, Uri? location)
    {
        if (location is null)
        {
            throw Failure(TorrentStagingFailureCode.RedirectRejected, "Torrent redirect omitted its destination.");
        }

        var redirect = location.IsAbsoluteUri ? location : new Uri(current, location);
        if (current.Scheme == Uri.UriSchemeHttps && redirect.Scheme != Uri.UriSchemeHttps)
        {
            throw Failure(TorrentStagingFailureCode.RedirectRejected, "Torrent redirect attempted to downgrade HTTPS.");
        }

        return redirect;
    }

    private static bool IsRedirect(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.MovedPermanently
            or HttpStatusCode.Redirect
            or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;

    private static TorrentStagingException Failure(
        TorrentStagingFailureCode code,
        string message,
        Exception? innerException = null) =>
        innerException is null
            ? new TorrentStagingException(code, message)
            : new TorrentStagingException(code, message, innerException);

    private static void DeleteIfPresent(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }
}
