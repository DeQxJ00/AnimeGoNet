using System.Net;
using System.Net.Http.Headers;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Data.Metadata;

namespace AnimeGoNet.App.Metadata;

public sealed record AnidbTitleCacheRefreshResult(
    string Status,
    long AnimeCount,
    long TitleCount,
    long SourceSizeBytes,
    DateTimeOffset NextCheckAtUtc);

public interface IAnidbTitleCacheService
{
    Task<AnidbTitleCacheRefreshResult> RefreshAsync(
        bool force,
        CancellationToken cancellationToken = default);
}

public sealed class AnidbTitleCacheException(
    string code,
    string message,
    Exception? innerException = null) : InvalidOperationException(message, innerException)
{
    public string Code { get; } = code;
}

public sealed class AnidbTitleCacheService : IAnidbTitleCacheService, IDisposable
{
    public const string DefaultSourceUrl = "https://anidb.net/api/anime-titles.xml.gz";
    public const int DefaultRefreshIntervalHours = 24;
    private readonly HttpClient _httpClient;
    private readonly DirectoryLayout _layout;
    private readonly AnidbTitleCacheStore _store;
    private readonly bool _ownsHttpClient;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AnidbTitleCacheService(
        HttpClient httpClient,
        DirectoryLayout layout,
        AnidbTitleCacheStore store,
        bool ownsHttpClient = false)
    {
        _httpClient = httpClient;
        _layout = layout;
        _store = store;
        _ownsHttpClient = ownsHttpClient;
    }

    public async Task<AnidbTitleCacheRefreshResult> RefreshAsync(
        bool force,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var status = await _store.GetStatusAsync(cancellationToken).ConfigureAwait(false);
            var now = DateTimeOffset.UtcNow;
            var refreshInterval = TimeSpan.FromHours(status.RefreshIntervalHours);
            if (!force && status.NextCheckAtUtc is { } next && next > now)
            {
                return new AnidbTitleCacheRefreshResult(
                    "not_due", status.AnimeCount, status.TitleCount,
                    status.SourceSizeBytes, next);
            }

            var sourceUrl = string.IsNullOrWhiteSpace(status.SourceUrl)
                ? DefaultSourceUrl
                : status.SourceUrl;
            await _store.MarkCheckingAsync(sourceUrl, now, cancellationToken)
                .ConfigureAwait(false);
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, sourceUrl);
                if (!force && EntityTagHeaderValue.TryParse(status.ETag, out var etag))
                {
                    request.Headers.IfNoneMatch.Add(etag);
                }
                if (!force && DateTimeOffset.TryParse(status.LastModified, out var modified))
                {
                    request.Headers.IfModifiedSince = modified;
                }
                using var response = await _httpClient.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
                var nextCheck = now.Add(refreshInterval);
                if (response.StatusCode == HttpStatusCode.NotModified)
                {
                    await _store.MarkNotModifiedAsync(now, nextCheck, cancellationToken)
                        .ConfigureAwait(false);
                    return new AnidbTitleCacheRefreshResult(
                        "not_modified", status.AnimeCount, status.TitleCount,
                        status.SourceSizeBytes, nextCheck);
                }
                response.EnsureSuccessStatusCode();

                var directory = Path.Combine(_layout.CachePath, "anidb");
                Directory.CreateDirectory(directory);
                var target = Path.Combine(directory, "anime-titles.xml.gz");
                var temporary = Path.Combine(directory, $"anime-titles-{Guid.NewGuid():N}.tmp");
                try
                {
                    await using (var output = new FileStream(
                        temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                        128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
                    await using (var input = await response.Content
                        .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                    {
                        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                    }
                    var size = new FileInfo(temporary).Length;
                    AnidbTitleImportResult imported;
                    await using (var input = new FileStream(
                        temporary, FileMode.Open, FileAccess.Read, FileShare.Read,
                        128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
                    {
                        imported = await _store.ImportGzipAsync(
                            input,
                            sourceUrl,
                            response.Headers.ETag?.ToString(),
                            response.Content.Headers.LastModified?.ToString("R"),
                            size,
                            now,
                            nextCheck,
                            cancellationToken).ConfigureAwait(false);
                    }
                    File.Move(temporary, target, overwrite: true);
                    return new AnidbTitleCacheRefreshResult(
                        "completed", imported.AnimeCount, imported.TitleCount,
                        size, nextCheck);
                }
                finally
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is HttpRequestException
                or IOException or InvalidDataException or System.Xml.XmlException)
            {
                var code = exception switch
                {
                    HttpRequestException => "anidb_title_download_failed",
                    InvalidDataException or System.Xml.XmlException => "anidb_title_archive_invalid",
                    _ => "anidb_title_cache_io_failed",
                };
                await _store.MarkFailedAsync(
                    code, now, now.Add(refreshInterval), cancellationToken).ConfigureAwait(false);
                throw new AnidbTitleCacheException(
                    code, "AniDB title cache refresh failed.", exception);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _gate.Dispose();
        if (_ownsHttpClient) _httpClient.Dispose();
    }
}

public sealed partial class AnidbTitleCacheWorker(
    IAnidbTitleCacheService service,
    ILogger<AnidbTitleCacheWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken).ConfigureAwait(false);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await service.RefreshAsync(force: false, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (AnidbTitleCacheException exception)
            {
                LogRefreshFailed(logger, exception.Code);
            }
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken).ConfigureAwait(false);
        }
    }

    [LoggerMessage(
        EventId = 7101,
        Level = LogLevel.Warning,
        Message = "AniDB title cache refresh failed with {FailureCode}.")]
    private static partial void LogRefreshFailed(ILogger logger, string failureCode);
}
