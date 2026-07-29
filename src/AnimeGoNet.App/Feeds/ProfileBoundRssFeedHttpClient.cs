using System.Net;
using AnimeGoNet.App.Torrents;
using AnimeGoNet.Core.Feeds;
using AnimeGoNet.Data.Sources;

namespace AnimeGoNet.App.Feeds;

public sealed class ProfileBoundRssFeedHttpClient(
    SourceProfileStore profiles,
    ITorrentDnsResolver dnsResolver,
    ITorrentHttpTransport transport) : ISourceProfileRssFeedHttpClient
{
    private const int MaximumRedirects = 5;

    public async Task<ReadOnlyMemory<byte>> GetAsync(
        Uri uri,
        CancellationToken cancellationToken = default) =>
        await GetAsync(
            uri,
            "mikan",
            cancellationToken).ConfigureAwait(false);

    public async Task<ReadOnlyMemory<byte>> GetAsync(
        Uri uri,
        string sourceProfileId,
        CancellationToken cancellationToken = default)
    {
        var profile = await profiles
            .GetEnabledAsync(sourceProfileId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new RssFeedException("rss_source_profile_missing", "Enabled Mikan source profile was not found.");
        if (!string.Equals(
            profile.Adapter,
            "mikan",
            StringComparison.OrdinalIgnoreCase))
        {
            throw new RssFeedException(
                "rss_source_profile_invalid",
                "RSS source profile must use the Mikan adapter.");
        }
        var current = uri;
        for (var redirects = 0; redirects <= MaximumRedirects; redirects++)
        {
            Validate(current, profile.AllowedTorrentHosts);
            IReadOnlyList<IPAddress> addresses;
            try
            {
                addresses = await dnsResolver.ResolveAsync(current.IdnHost, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new RssFeedException("rss_request_failed", "RSS host resolution failed.", exception);
            }

            if (addresses.Count == 0 || addresses.Any(address => !TorrentNetworkPolicy.IsPublicAddress(address)))
            {
                throw new RssFeedException("rss_address_not_allowed", "RSS host resolved to a prohibited address.");
            }

            TorrentHttpResponse response;
            try
            {
                var requestOptions = string.Equals(
                        current.IdnHost,
                        uri.IdnHost,
                        StringComparison.OrdinalIgnoreCase)
                    ? new TorrentHttpRequestOptions(
                        profile.MikanIdentityCookie)
                    : new TorrentHttpRequestOptions();
                response = await transport
                    .SendAsync(
                        current,
                        addresses,
                        requestOptions,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new RssFeedException("rss_request_failed", "RSS request failed.", exception);
            }
            await using var responseOwner = response;
            if ((int)response.StatusCode is >= 300 and < 400)
            {
                if (redirects == MaximumRedirects || response.RedirectLocation is null)
                {
                    throw new RssFeedException("rss_redirect_rejected", "RSS redirect was rejected.");
                }

                current = response.RedirectLocation.IsAbsoluteUri
                    ? response.RedirectLocation
                    : new Uri(current, response.RedirectLocation);
                if (uri.Scheme == Uri.UriSchemeHttps && current.Scheme != Uri.UriSchemeHttps)
                {
                    throw new RssFeedException("rss_redirect_rejected", "RSS redirect attempted to downgrade HTTPS.");
                }
                continue;
            }

            if ((int)response.StatusCode is < 200 or >= 300)
            {
                throw new RssFeedException("rss_request_failed", "RSS server returned an unsuccessful response.");
            }

            if (response.ContentLength is > RssFeedParser.MaximumBytes)
            {
                throw new RssFeedException("rss_too_large", "RSS response exceeds the size limit.");
            }

            using var output = new MemoryStream();
            var buffer = new byte[64 * 1024];
            while (true)
            {
                var read = await response.Content.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                if (output.Length + read > RssFeedParser.MaximumBytes)
                {
                    throw new RssFeedException("rss_too_large", "RSS response exceeds the size limit.");
                }
                output.Write(buffer, 0, read);
            }
            return output.ToArray();
        }

        throw new RssFeedException("rss_redirect_rejected", "RSS redirect limit was exceeded.");
    }

    private static void Validate(Uri uri, IReadOnlyList<string> allowedHosts)
    {
        if (!uri.IsAbsoluteUri
            || uri.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new RssFeedException("rss_url_invalid", "RSS URL must be an absolute HTTP(S) URL without userinfo or fragment.");
        }
        if (!TorrentNetworkPolicy.IsHostAllowed(uri.IdnHost, allowedHosts))
        {
            throw new RssFeedException("rss_host_not_allowed", "RSS host is not allowed by the source profile.");
        }
    }
}
