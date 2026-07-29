using System.Collections.Concurrent;
using AnimeGoNet.App.Library;

namespace AnimeGoNet.App.Tests.Library;

internal sealed class RecordingPosterTransport(
    byte[]? content = null,
    Exception? failure = null,
    TimeSpan? delay = null) : ITmdbPosterTransport
{
    private static readonly byte[] DefaultJpeg = [0xff, 0xd8, 0xff, 0xe0, 0x01, 0x02, 0x03];
    private int _callCount;

    public ConcurrentQueue<Uri> Requests { get; } = new();

    public int CallCount => Volatile.Read(ref _callCount);

    public async Task<byte[]> DownloadAsync(
        Uri uri,
        long maximumBytes,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        _ = maximumBytes;
        _ = timeout;
        Interlocked.Increment(ref _callCount);
        Requests.Enqueue(uri);
        if (delay is not null)
        {
            await Task.Delay(delay.Value, cancellationToken);
        }

        if (failure is not null)
        {
            throw failure;
        }

        return content ?? DefaultJpeg;
    }
}
