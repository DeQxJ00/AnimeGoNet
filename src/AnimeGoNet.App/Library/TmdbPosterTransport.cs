using System.Buffers;
using System.Net.Http.Headers;

namespace AnimeGoNet.App.Library;

public interface ITmdbPosterTransport
{
    Task<byte[]> DownloadAsync(
        Uri uri,
        long maximumBytes,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}

public sealed class HttpTmdbPosterTransport(
    HttpClient httpClient,
    bool ownsHttpClient = false) : ITmdbPosterTransport, IDisposable
{
    public async Task<byte[]> DownloadAsync(
        Uri uri,
        long maximumBytes,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumBytes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("AnimeGoNet", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/avif"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/webp"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/jpeg"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/png"));
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            timeoutSource.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > 0
            && response.Content.Headers.ContentLength > maximumBytes)
        {
            throw new InvalidDataException("TMDB poster exceeded the configured size limit.");
        }

        await using var source = await response.Content
            .ReadAsStreamAsync(timeoutSource.Token)
            .ConfigureAwait(false);
        using var destination = new MemoryStream();
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            while (true)
            {
                var read = await source
                    .ReadAsync(buffer.AsMemory(), timeoutSource.Token)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                if (destination.Length + read > maximumBytes)
                {
                    throw new InvalidDataException("TMDB poster exceeded the configured size limit.");
                }

                destination.Write(buffer, 0, read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return destination.ToArray();
    }

    public void Dispose()
    {
        if (ownsHttpClient)
        {
            httpClient.Dispose();
        }
    }
}
