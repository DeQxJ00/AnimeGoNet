using AnimeGoNet.Core.Feeds;

namespace AnimeGoNet.App.Feeds;

public interface IRssFeedHttpClient
{
    Task<ReadOnlyMemory<byte>> GetAsync(Uri uri, CancellationToken cancellationToken = default);
}

public interface ISourceProfileRssFeedHttpClient : IRssFeedHttpClient
{
    Task<ReadOnlyMemory<byte>> GetAsync(
        Uri uri,
        string sourceProfileId,
        CancellationToken cancellationToken = default);
}

public sealed class RssFeedHttpClient(HttpClient httpClient) : IRssFeedHttpClient
{
    public async Task<ReadOnlyMemory<byte>> GetAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync(
            uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > RssFeedParser.MaximumBytes)
        {
            throw new RssFeedException("rss_too_large", "RSS response exceeds the size limit.");
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var target = new MemoryStream();
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (target.Length + read > RssFeedParser.MaximumBytes)
            {
                throw new RssFeedException("rss_too_large", "RSS response exceeds the size limit.");
            }

            target.Write(buffer, 0, read);
        }

        return target.ToArray();
    }
}

public sealed class RssFeedReader(IRssFeedHttpClient httpClient)
{
    public RssFeedDocument ParseRaw(ReadOnlyMemory<byte> raw, string? sourceUrl = null) =>
        RssFeedParser.Parse(raw, sourceUrl);

    public async Task<RssFeedDocument> ParseFileAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new RssFeedException("rss_empty", "RSS file path is empty.");
        }

        try
        {
            await using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length > RssFeedParser.MaximumBytes)
            {
                throw new RssFeedException("rss_too_large", "RSS file exceeds the size limit.");
            }

            var raw = new byte[(int)stream.Length];
            await stream.ReadExactlyAsync(raw, cancellationToken).ConfigureAwait(false);
            return RssFeedParser.Parse(raw);
        }
        catch (RssFeedException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new RssFeedException("rss_file_open_failed", "RSS file could not be opened.", exception);
        }
    }

    public async Task<RssFeedDocument> ParseUrlAsync(
        string value,
        CancellationToken cancellationToken = default) =>
        await ParseUrlAsync(
            value,
            sourceProfileId: null,
            cancellationToken).ConfigureAwait(false);

    public async Task<RssFeedDocument> ParseUrlAsync(
        string value,
        string? sourceProfileId,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            throw new RssFeedException("rss_url_invalid", "RSS URL must use HTTP or HTTPS.");
        }

        try
        {
            var raw = sourceProfileId is not null
                && httpClient is ISourceProfileRssFeedHttpClient profileClient
                ? await profileClient
                    .GetAsync(uri, sourceProfileId, cancellationToken)
                    .ConfigureAwait(false)
                : await httpClient
                    .GetAsync(uri, cancellationToken)
                    .ConfigureAwait(false);
            return RssFeedParser.Parse(raw, uri.AbsoluteUri);
        }
        catch (RssFeedException)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw new RssFeedException("rss_request_failed", "RSS request failed.", exception);
        }
    }
}
