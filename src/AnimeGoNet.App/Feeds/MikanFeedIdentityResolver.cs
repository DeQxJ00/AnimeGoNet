using AnimeGoNet.Core.Feeds;

namespace AnimeGoNet.App.Feeds;

public sealed record MikanFeedIdentityResolution(
    int ItemIndex,
    MikanEpisodeIdentity? Identity,
    string? FailureCode);

public sealed class MikanFeedIdentityResolver(
    IRssFeedHttpClient httpClient,
    MikanEpisodeIdentityCache? persistentCache = null)
{
    public async Task<IReadOnlyList<MikanFeedIdentityResolution>> ResolveAsync(
        RssFeedDocument feed,
        string sourceProfileId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(feed);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceProfileId);

        var cache = new Dictionary<string, IdentityLookup>(StringComparer.Ordinal);
        var results = new MikanFeedIdentityResolution[feed.Items.Count];
        for (var index = 0; index < feed.Items.Count; index++)
        {
            var lookup = await LookupAsync(
                feed.Items[index].MikanUrl,
                sourceProfileId,
                cache,
                cancellationToken).ConfigureAwait(false);
            results[index] = new MikanFeedIdentityResolution(
                index,
                lookup.Identity,
                lookup.FailureCode);
        }

        return results;
    }

    public async Task<MikanFeedIdentityResolution> ResolveFreshAsync(
        Uri episodeUri,
        string sourceProfileId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(episodeUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceProfileId);
        var lookup = await LookupAsync(
            episodeUri.AbsoluteUri,
            sourceProfileId,
            new Dictionary<string, IdentityLookup>(StringComparer.Ordinal),
            cancellationToken,
            bypassPersistentCache: true).ConfigureAwait(false);
        return new MikanFeedIdentityResolution(0, lookup.Identity, lookup.FailureCode);
    }

    private async Task<IdentityLookup> LookupAsync(
        string value,
        string sourceProfileId,
        Dictionary<string, IdentityLookup> cache,
        CancellationToken cancellationToken,
        bool bypassPersistentCache = false)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return new IdentityLookup(null, "mikan_identity_url_invalid");
        }

        var key = uri.AbsoluteUri;
        if (cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        if (!bypassPersistentCache && persistentCache is not null)
        {
            var persistent = await persistentCache.GetAsync(uri, cancellationToken)
                .ConfigureAwait(false);
            if (persistent is not null)
            {
                var hit = new IdentityLookup(persistent, null);
                cache.Add(key, hit);
                return hit;
            }
        }

        IdentityLookup result;
        try
        {
            var html = httpClient is ISourceProfileRssFeedHttpClient profileClient
                ? await profileClient
                    .GetAsync(uri, sourceProfileId, cancellationToken)
                    .ConfigureAwait(false)
                : await httpClient.GetAsync(uri, cancellationToken).ConfigureAwait(false);
            var identity = MikanEpisodeIdentityParser.Parse(html);
            if (persistentCache is not null && identity.SubGroupId > 0)
            {
                await persistentCache.PutAsync(uri, identity, cancellationToken)
                    .ConfigureAwait(false);
            }
            result = new IdentityLookup(identity, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (MikanEpisodeIdentityException exception)
        {
            result = new IdentityLookup(null, exception.Code);
        }
        catch (RssFeedException exception)
        {
            result = new IdentityLookup(null, exception.Code);
        }
        catch (HttpRequestException)
        {
            result = new IdentityLookup(null, "mikan_identity_request_failed");
        }

        cache.Add(key, result);
        return result;
    }

    private sealed record IdentityLookup(MikanEpisodeIdentity? Identity, string? FailureCode);
}
