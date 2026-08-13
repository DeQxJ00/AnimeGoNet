using System.Globalization;
using AnimeGoNet.Core.Feeds;

namespace AnimeGoNet.App.Feeds;

public sealed class MikanBangumiSubjectResolver(
    IRssFeedHttpClient httpClient,
    MikanBangumiIdentityCache? persistentCache = null)
{
    public async Task<MikanBangumiDiscovery> ResolveAsync(
        RssFeedDocument feed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(feed);
        if (feed.MikanId is not > 0)
        {
            return new MikanBangumiDiscovery(
                null,
                MikanBangumiDiscoveryStates.NotApplicable,
                "mikan_bgmid_mikanid_missing");
        }

        if (persistentCache is not null)
        {
            var cached = await persistentCache.GetAsync(
                feed.MikanId.Value,
                cancellationToken).ConfigureAwait(false);
            if (cached is not null)
            {
                return new MikanBangumiDiscovery(
                    cached,
                    MikanBangumiDiscoveryStates.Resolved,
                    null);
            }
        }

        var origin = feed.Items
            .Select(item => ParsePageOrigin(item.MikanUrl))
            .FirstOrDefault(uri => uri is not null);
        if (origin is null)
        {
            return new MikanBangumiDiscovery(
                null,
                MikanBangumiDiscoveryStates.Failed,
                "mikan_bgmid_page_origin_missing");
        }

        var page = new Uri(
            origin,
            $"/Home/Bangumi/{feed.MikanId.Value.ToString(CultureInfo.InvariantCulture)}");
        try
        {
            var html = await httpClient.GetAsync(page, cancellationToken).ConfigureAwait(false);
            var bangumiId = MikanBangumiSubjectParser.Parse(html);
            if (persistentCache is not null)
            {
                await persistentCache.PutAsync(
                    feed.MikanId.Value,
                    bangumiId,
                    cancellationToken).ConfigureAwait(false);
            }
            return new MikanBangumiDiscovery(
                bangumiId,
                MikanBangumiDiscoveryStates.Resolved,
                null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (MikanBangumiSubjectException exception)
        {
            return new MikanBangumiDiscovery(
                null,
                exception.Code == "mikan_bgmid_link_missing"
                    ? MikanBangumiDiscoveryStates.NotFound
                    : MikanBangumiDiscoveryStates.Failed,
                exception.Code);
        }
        catch (RssFeedException exception)
        {
            return new MikanBangumiDiscovery(
                null,
                MikanBangumiDiscoveryStates.Failed,
                exception.Code);
        }
        catch
        {
            return new MikanBangumiDiscovery(
                null,
                MikanBangumiDiscoveryStates.Failed,
                "mikan_bgmid_discovery_failed");
        }
    }

    private static Uri? ParsePageOrigin(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return null;
        }

        return new UriBuilder(uri.Scheme, uri.IdnHost, uri.IsDefaultPort ? -1 : uri.Port).Uri;
    }
}
