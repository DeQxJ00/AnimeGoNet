using System.Net;
using AnimeGoNet.Core.Torrents;

namespace AnimeGoNet.App.Torrents;

public sealed class TorrentSourcePolicy(
    string sourceProfileId,
    IReadOnlyList<string> allowedHosts,
    string? mikanIdentityCookie = null,
    IReadOnlyList<string>? trustedPrivateHosts = null)
{
    public string SourceProfileId { get; } = sourceProfileId;

    public IReadOnlyList<string> AllowedHosts { get; } = allowedHosts;

    internal string? MikanIdentityCookie { get; } = mikanIdentityCookie;

    internal IReadOnlyList<string> TrustedPrivateHosts { get; } = trustedPrivateHosts ?? [];

    public bool CredentialsConfigured => MikanIdentityCookie is not null;

    public override string ToString() =>
        $"TorrentSourcePolicy {{ SourceProfileId = {SourceProfileId}, "
        + $"AllowedHostCount = {AllowedHosts.Count}, "
        + $"TrustedPrivateHostCount = {TrustedPrivateHosts.Count}, "
        + $"CredentialsConfigured = {CredentialsConfigured} }}";
}

public sealed class TorrentHttpRequestOptions(
    string? mikanIdentityCookie = null)
{
    internal string? MikanIdentityCookie { get; } = mikanIdentityCookie;

    public bool CredentialsConfigured => MikanIdentityCookie is not null;

    public override string ToString() =>
        $"TorrentHttpRequestOptions {{ CredentialsConfigured = {CredentialsConfigured} }}";
}

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

    ValueTask<TorrentHttpResponse> SendAsync(
        Uri uri,
        IReadOnlyList<IPAddress> validatedAddresses,
        TorrentHttpRequestOptions requestOptions,
        CancellationToken cancellationToken) =>
        requestOptions.CredentialsConfigured
            ? ValueTask.FromException<TorrentHttpResponse>(
                new NotSupportedException(
                    "This HTTP transport does not support source credentials."))
            : SendAsync(uri, validatedAddresses, cancellationToken);
}

public interface ITorrentStagingService
{
    Task<StagedTorrent> StageAsync(
        Uri secretUrl,
        TorrentSourcePolicy sourcePolicy,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string stagingFileName, CancellationToken cancellationToken = default);

    FileStream OpenRead(string stagingFileName);

    Task<int> CleanupExpiredAsync(CancellationToken cancellationToken = default);
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

    public string StagingFileName { get; } = Path.GetFileName(filePath);

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
