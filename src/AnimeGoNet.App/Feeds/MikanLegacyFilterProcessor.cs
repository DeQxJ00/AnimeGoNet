using AnimeGoNet.Core.Feeds;
using AnimeGoNet.Core.Rules;
using AnimeGoNet.Data.Rules;
using AnimeGoNet.Data.Sources;

namespace AnimeGoNet.App.Feeds;

public sealed record MikanLegacyFilterBatch(
    long Revision,
    bool Enabled,
    IReadOnlyList<MikanLegacyFilterAudit> Audits);

public sealed class MikanLegacyFilterProcessor(
    LegacyMikanFilterStore filters,
    IRssFeedHttpClient httpClient)
{
    public async Task<MikanLegacyFilterBatch> EvaluateAsync(
        RssFeedDocument feed,
        SourceProfileRecord profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return await EvaluateAsync(
            feed,
            profile.Id,
            profile.RssFilterEnabled,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<MikanLegacyFilterBatch> EvaluateAsync(
        RssFeedDocument feed,
        string sourceProfileId,
        bool rssFilterEnabled,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(feed);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceProfileId);
        var profileId = sourceProfileId.Trim().ToLowerInvariant();
        var snapshot = await filters.GetAsync(profileId, cancellationToken).ConfigureAwait(false)
            ?? throw new RssFeedException("legacy_filter_missing", "Legacy Mikan filter was not initialized.");
        if (!rssFilterEnabled)
        {
            return new MikanLegacyFilterBatch(
                snapshot.Revision,
                false,
                feed.Items.Select(_ => new MikanLegacyFilterAudit(
                    MikanLegacyFilterState.SkippedByConfiguration,
                    "SkippedByConfiguration")).ToArray());
        }

        var needsIdentity = snapshot.Config.Filiter1.Count > 0
            || snapshot.Config.Filiter2.Count > 0
            || snapshot.Config.Filiter3.Count > 0;
        var identityCache = new Dictionary<string, IdentityLookup>(StringComparer.Ordinal);
        var audits = new MikanLegacyFilterAudit[feed.Items.Count];
        for (var index = 0; index < feed.Items.Count; index++)
        {
            var item = feed.Items[index];
            MikanEpisodeIdentity? identity = null;
            if (needsIdentity)
            {
                var lookup = await LookupIdentityAsync(
                    item.MikanUrl, identityCache, cancellationToken).ConfigureAwait(false);
                if (lookup.Identity is null)
                {
                    audits[index] = new MikanLegacyFilterAudit(
                        MikanLegacyFilterState.FilterEvaluationFailed,
                        lookup.FailureCode ?? "mikan_identity_unknown_failure");
                    continue;
                }
                identity = lookup.Identity;
            }

            try
            {
                var result = LegacyMikanFilterEngine.Evaluate(
                    new LegacyMikanFilterCandidate(
                        item.Title,
                        identity?.MikanId,
                        identity?.SubGroupId,
                        LegacyMikanFilterEngine.ParseGroupName(item.Title)),
                    snapshot.Config);
                audits[index] = new MikanLegacyFilterAudit(
                    result.Accepted ? MikanLegacyFilterState.Accepted : MikanLegacyFilterState.Rejected,
                    result.Reason,
                    result.MatchedScope,
                    result.MatchedKey,
                    identity?.MikanId,
                    identity?.SubGroupId);
            }
            catch (Exception exception) when (exception is ArgumentException or FormatException)
            {
                audits[index] = new MikanLegacyFilterAudit(
                    MikanLegacyFilterState.FilterEvaluationFailed,
                    "legacy_filter_evaluation_failed",
                    IdentityMikanId: identity?.MikanId,
                    IdentityGroupId: identity?.SubGroupId);
            }
        }

        return new MikanLegacyFilterBatch(snapshot.Revision, true, audits);
    }

    private async Task<IdentityLookup> LookupIdentityAsync(
        string value,
        Dictionary<string, IdentityLookup> cache,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            return new IdentityLookup(null, "mikan_identity_url_invalid");
        }
        var key = uri.AbsoluteUri;
        if (cache.TryGetValue(key, out var cached)) return cached;

        IdentityLookup result;
        try
        {
            var html = await httpClient.GetAsync(uri, cancellationToken).ConfigureAwait(false);
            result = new IdentityLookup(MikanEpisodeIdentityParser.Parse(html), null);
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
