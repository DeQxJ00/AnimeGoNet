using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Data.Library;

namespace AnimeGoNet.App.Library;

public sealed record AnimeCoverAsset(
    byte[] Content,
    string ContentType,
    string Source,
    bool CacheHit,
    string? WarningCode);

public sealed class AnimeCoverService(
    AnimeLibraryStore library,
    DirectoryLayout layout,
    ITmdbPosterTransport transport,
    AnimeGoOptions options)
{
    private const long MaximumPosterBytes = 5 * 1024 * 1024;
    private const string PosterSize = "w500";
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> CacheGates =
        new(StringComparer.Ordinal);
    private static readonly byte[] Placeholder = Encoding.UTF8.GetBytes(
        """
        <svg xmlns="http://www.w3.org/2000/svg" width="500" height="750" viewBox="0 0 500 750" role="img" aria-labelledby="title description">
          <title id="title">AnimeGoNet poster placeholder</title>
          <desc id="description">No TMDB poster is currently available.</desc>
          <rect width="500" height="750" fill="#111827"/>
          <rect x="48" y="48" width="404" height="654" rx="28" fill="#1f2937" stroke="#334155" stroke-width="4"/>
          <circle cx="250" cy="292" r="92" fill="#334155"/>
          <path d="M210 244v96l80-48z" fill="#94a3b8"/>
          <text x="250" y="470" text-anchor="middle" fill="#e2e8f0" font-family="system-ui, sans-serif" font-size="34" font-weight="700">AnimeGoNet</text>
          <text x="250" y="518" text-anchor="middle" fill="#94a3b8" font-family="system-ui, sans-serif" font-size="22">TMDB poster unavailable</text>
        </svg>
        """);

    public async Task<AnimeCoverAsset?> GetAsync(
        int tmdbSeriesId,
        int seasonNumber,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tmdbSeriesId, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(seasonNumber, 1);
        var poster = await library
            .GetPosterAsync(tmdbSeriesId, seasonNumber, cancellationToken)
            .ConfigureAwait(false);
        if (poster is null)
        {
            return null;
        }

        return await GetAssetAsync(poster, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AnimeCoverAsset?> GetMovieAsync(
        int tmdbMovieId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tmdbMovieId, 1);
        var poster = await library.GetMoviePosterAsync(tmdbMovieId, cancellationToken)
            .ConfigureAwait(false);
        return poster is null
            ? null
            : await GetAssetAsync(poster, cancellationToken).ConfigureAwait(false);
    }

    private async Task<AnimeCoverAsset> GetAssetAsync(
        AnimePosterProjection poster,
        CancellationToken cancellationToken)
    {

        if (poster.PosterPath is null)
        {
            return PlaceholderAsset();
        }

        if (!TryCreatePosterUri(poster.PosterPath, out var posterUri))
        {
            return PlaceholderAsset("cover_poster_path_invalid");
        }

        var cacheDirectory = Path.Combine(layout.CachePath, "covers");
        Directory.CreateDirectory(cacheDirectory);
        var cacheKey = Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes(
                options.Metadata.Tmdb.ImageBaseUrl.AbsoluteUri
                + "\n"
                + PosterSize
                + "\n"
                + poster.PosterPath)));
        var cachePath = Path.Combine(cacheDirectory, cacheKey + ".bin");
        var gate = CacheGates.GetOrAdd(cacheKey, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var cached = await ReadValidatedCacheAsync(cachePath, cancellationToken).ConfigureAwait(false);
            if (cached is not null)
            {
                return new AnimeCoverAsset(
                    cached.Value.Content,
                    cached.Value.ContentType,
                    poster.Source,
                    CacheHit: true,
                    WarningCode: null);
            }

            byte[] content;
            try
            {
                content = await transport.DownloadAsync(
                    posterUri!,
                    MaximumPosterBytes,
                    options.Metadata.Tmdb.HttpTimeout,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                return PlaceholderAsset("cover_upstream_timeout");
            }
            catch (HttpRequestException)
            {
                return PlaceholderAsset("cover_upstream_unavailable");
            }
            catch (InvalidDataException)
            {
                return PlaceholderAsset("cover_upstream_invalid");
            }

            var contentType = DetectContentType(content);
            if (contentType is null)
            {
                return PlaceholderAsset("cover_upstream_invalid");
            }

            var temporaryPath = cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(temporaryPath, content, cancellationToken)
                    .ConfigureAwait(false);
                File.Move(temporaryPath, cachePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }

            return new AnimeCoverAsset(
                content,
                contentType,
                poster.Source,
                CacheHit: false,
                WarningCode: null);
        }
        finally
        {
            gate.Release();
        }
    }

    private static async Task<(byte[] Content, string ContentType)?> ReadValidatedCacheAsync(
        string cachePath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(cachePath))
        {
            return null;
        }

        var content = await File.ReadAllBytesAsync(cachePath, cancellationToken).ConfigureAwait(false);
        var contentType = DetectContentType(content);
        if (contentType is not null)
        {
            return (content, contentType);
        }

        File.Delete(cachePath);
        return null;
    }

    private bool TryCreatePosterUri(string posterPath, out Uri? uri)
    {
        uri = null;
        if (posterPath.Length is 0 or > 256
            || posterPath[0] != '/'
            || posterPath.Contains('\\')
            || posterPath.Contains('?')
            || posterPath.Contains('#')
            || posterPath.Any(char.IsControl))
        {
            return false;
        }

        var imageBaseUrl = options.Metadata.Tmdb.ImageBaseUrl;
        uri = new Uri(imageBaseUrl, PosterSize + "/" + posterPath.TrimStart('/'));
        return uri.Scheme is "http" or "https"
            && string.Equals(uri.Host, imageBaseUrl.Host, StringComparison.OrdinalIgnoreCase);
    }

    private static string? DetectContentType(ReadOnlySpan<byte> content)
    {
        if (content.Length >= 3
            && content[0] == 0xff
            && content[1] == 0xd8
            && content[2] == 0xff)
        {
            return "image/jpeg";
        }

        ReadOnlySpan<byte> png = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];
        if (content.StartsWith(png))
        {
            return "image/png";
        }

        if (content.Length >= 12
            && content[..4].SequenceEqual("RIFF"u8)
            && content.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        return null;
    }

    private static AnimeCoverAsset PlaceholderAsset(string? warningCode = null) =>
        new(
            Placeholder,
            "image/svg+xml; charset=utf-8",
            "placeholder",
            CacheHit: false,
            warningCode);
}
