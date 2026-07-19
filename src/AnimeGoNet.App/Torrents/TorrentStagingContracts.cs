using System.Net;
using AnimeGoNet.Core.Torrents;

namespace AnimeGoNet.App.Torrents;

public sealed record TorrentSourcePolicy(string SourceProfileId, IReadOnlyList<string> AllowedHosts);

public enum TorrentStagingFailureCode
{
    InvalidUrl,
    HostNotAllowed,
    AddressNotAllowed,
    RedirectRejected,
    TooManyRedirects,
    ResponseRejected,
    ResponseTooLarge,
    NetworkFailure,
    Timeout,
    InvalidTorrent,
}

public sealed class TorrentStagingException : Exception
{
    public TorrentStagingException(TorrentStagingFailureCode code, string message)
        : base(message)
    {
        Code = code;
    }

    public TorrentStagingException(TorrentStagingFailureCode code, string message, Exception innerException)
        : base(message, innerException)
    {
        Code = code;
    }

    public TorrentStagingFailureCode Code { get; }
}

public interface ITorrentDnsResolver
{
    ValueTask<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken);
}

public interface ITorrentHttpTransport
{
    ValueTask<TorrentHttpResponse> SendAsync(
        Uri uri,
        IReadOnlyList<IPAddress> validatedAddresses,
        CancellationToken cancellationToken);
}

public sealed class TorrentHttpResponse(
    HttpStatusCode statusCode,
    Uri? redirectLocation,
    long? contentLength,
    Stream content,
    IAsyncDisposable? owner = null) : IAsyncDisposable
{
    public HttpStatusCode StatusCode { get; } = statusCode;

    public Uri? RedirectLocation { get; } = redirectLocation;

    public long? ContentLength { get; } = contentLength;

    public Stream Content { get; } = content;

    public async ValueTask DisposeAsync()
    {
        await Content.DisposeAsync().ConfigureAwait(false);
        if (owner is not null)
        {
            await owner.DisposeAsync().ConfigureAwait(false);
        }
    }
}

public sealed class StagedTorrent(string filePath, TorrentMetadata metadata) : IAsyncDisposable
{
    public string FilePath { get; } = filePath;

    public TorrentMetadata Metadata { get; } = metadata;

    public FileStream OpenRead() => new(
        FilePath,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        bufferSize: 64 * 1024,
        FileOptions.Asynchronous | FileOptions.SequentialScan);

    public ValueTask DisposeAsync()
    {
        try
        {
            File.Delete(FilePath);
        }
        catch (DirectoryNotFoundException)
        {
        }

        return ValueTask.CompletedTask;
    }
}
