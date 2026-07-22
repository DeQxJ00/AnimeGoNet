using System.Globalization;
using AnimeGoNet.App.Ingest;
using AnimeGoNet.Core.Feeds;
using AnimeGoNet.Core.Ingest;
using AnimeGoNet.Core.Rules;
using AnimeGoNet.Data.Feeds;
using AnimeGoNet.Data.Rules;
using AnimeGoNet.Data.Sources;

namespace AnimeGoNet.App.Feeds;

public sealed record MikanRssIngestItemResult(
    string CandidateId,
    MikanRssDecisionKind DecisionKind,
    string DecisionReason,
    string Status,
    string? IngestTaskId,
    IReadOnlyList<string> Errors);

public sealed record MikanRssIngestResult(
    string BatchId,
    int? MikanId,
    long RuleRevision,
    IReadOnlyList<MikanRssIngestItemResult> Items);

public sealed class MikanRssIngestProcessor(
    SourceProfileStore profiles,
    MikanRssRuleStore rules,
    MikanRssBatchStore batches,
    UnifiedIngestProcessor ingest)
{
    private static readonly TimeSpan WinnerLeaseDuration = TimeSpan.FromMinutes(10);

    public async Task<MikanRssIngestResult> ProcessAsync(
        RssFeedDocument feed,
        string sourceProfileId = "mikan",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(feed);
        var profile = await profiles.GetEnabledAsync(
            sourceProfileId.Trim().ToLowerInvariant(), cancellationToken).ConfigureAwait(false)
            ?? throw new RssFeedException("rss_source_profile_missing", "Enabled RSS source profile was not found.");
        if (!string.Equals(profile.Adapter, "mikan", StringComparison.OrdinalIgnoreCase))
        {
            throw new RssFeedException("rss_source_profile_invalid", "RSS source profile is not a Mikan adapter.");
        }

        var ruleSnapshot = await rules.GetAsync(profile.Id, cancellationToken).ConfigureAwait(false)
            ?? throw new RssFeedException("rss_rules_missing", "RSS rules were not initialized.");
        var plan = MikanRssBatchPlanner.Create(feed, ruleSnapshot.Rules, profile.RssPriorityEnabled);
        var stored = await batches.SaveAsync(
            profile.Id, ruleSnapshot.Revision, profile.RssPriorityEnabled,
            plan, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
        var results = new List<MikanRssIngestItemResult>(plan.Items.Count);

        foreach (var item in plan.Items)
        {
            if (item.Decision.Kind != MikanRssDecisionKind.Winner)
            {
                results.Add(Result(item, "blocked", null, []));
                continue;
            }

            var audit = stored.Entries.Single(entry => entry.CandidateId == item.Candidate.Id);
            if (audit.EffectState == "ingested")
            {
                results.Add(Result(item, "already_ingested", audit.IngestTaskId, []));
                continue;
            }

            var lease = await batches.TryClaimWinnerAsync(
                stored.Id, item.Candidate.Id, DateTimeOffset.UtcNow,
                WinnerLeaseDuration, cancellationToken).ConfigureAwait(false);
            if (lease is null)
            {
                results.Add(Result(item, "already_claimed", null, []));
                continue;
            }

            var command = new IngestItemCommand(
                item.FeedItem.TorrentUrl,
                new IngestItemInfo(
                    item.FeedItem.Title, null, item.Candidate.Id,
                    feed.MikanId?.ToString(CultureInfo.InvariantCulture),
                    item.FeedItem.MikanUrl, null, feed.MikanId,
                    null, null, null));
            UnifiedIngestItemResult outcome;
            try
            {
                outcome = await ingest.ProcessRssWinnerAsync(
                    profile.Id, command, lease, cancellationToken).ConfigureAwait(false);
                if (!outcome.Accepted)
                {
                    _ = await batches.ReleaseWinnerAsync(lease, cancellationToken).ConfigureAwait(false);
                }
            }
            catch
            {
                _ = await batches.ReleaseWinnerAsync(lease, CancellationToken.None).ConfigureAwait(false);
                throw;
            }

            results.Add(Result(
                item, outcome.Status, outcome.IngestId, outcome.Errors));
        }

        return new MikanRssIngestResult(stored.Id, feed.MikanId, ruleSnapshot.Revision, results);
    }

    private static MikanRssIngestItemResult Result(
        MikanRssPlannedItem item,
        string status,
        string? ingestTaskId,
        IReadOnlyList<string> errors) =>
        new(item.Candidate.Id, item.Decision.Kind, item.Decision.Reason, status, ingestTaskId, errors);
}
