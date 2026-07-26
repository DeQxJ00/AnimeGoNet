using System.Globalization;
using System.Text.Json.Serialization;
using AnimeGoNet.App.Ingest;
using AnimeGoNet.Core.Feeds;
using AnimeGoNet.Core.Ingest;
using AnimeGoNet.Core.Rules;
using AnimeGoNet.Data.Feeds;
using AnimeGoNet.Data.Rules;
using AnimeGoNet.Data.Sources;

namespace AnimeGoNet.App.Feeds;

public sealed record MikanRssIngestItemResult(
    [property: JsonPropertyName("candidate_id")] string CandidateId,
    [property: JsonPropertyName("decision_kind"), JsonConverter(typeof(JsonStringEnumConverter<MikanRssDecisionKind>))]
    MikanRssDecisionKind DecisionKind,
    [property: JsonPropertyName("decision_reason")] string DecisionReason,
    [property: JsonPropertyName("legacy_filter_state"), JsonConverter(typeof(JsonStringEnumConverter<MikanLegacyFilterState>))]
    MikanLegacyFilterState LegacyFilterState,
    [property: JsonPropertyName("legacy_filter_reason")] string LegacyFilterReason,
    [property: JsonPropertyName("legacy_filter_scope")] string? LegacyFilterScope,
    [property: JsonPropertyName("legacy_filter_key")] string? LegacyFilterKey,
    [property: JsonPropertyName("identity_mikanid")] int? IdentityMikanId,
    [property: JsonPropertyName("identity_groupid")] int? IdentityGroupId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("ingest_task_id")] string? IngestTaskId,
    [property: JsonPropertyName("errors")] IReadOnlyList<string> Errors);

public sealed record MikanRssIngestResult(
    [property: JsonPropertyName("batch_id")] string BatchId,
    [property: JsonPropertyName("mikanid")] int? MikanId,
    [property: JsonPropertyName("rule_revision")] long RuleRevision,
    [property: JsonPropertyName("legacy_filter_revision")] long LegacyFilterRevision,
    [property: JsonPropertyName("legacy_filter_enabled")] bool LegacyFilterEnabled,
    [property: JsonPropertyName("items")] IReadOnlyList<MikanRssIngestItemResult> Items);

public sealed class MikanRssIngestProcessor(
    SourceProfileStore profiles,
    MikanRssRuleStore rules,
    MikanRssBatchStore batches,
    MikanLegacyFilterProcessor legacyFilter,
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
        var legacy = await legacyFilter.EvaluateAsync(feed, profile, cancellationToken).ConfigureAwait(false);
        var plan = MikanRssBatchPlanner.Create(
            feed,
            ruleSnapshot.Rules,
            profile.RssPriorityEnabled,
            legacy.Audits,
            legacy.Revision,
            legacy.Enabled);
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

            var publishedAt = MikanPublishedAtParser.Parse(item.FeedItem.PublishedDate);
            var command = new IngestItemCommand(
                item.FeedItem.TorrentUrl,
                new IngestItemInfo(
                    item.FeedItem.Title, null, item.Candidate.Id,
                    feed.MikanId?.ToString(CultureInfo.InvariantCulture),
                    item.FeedItem.MikanUrl, null, feed.MikanId,
                    null, null, null),
                string.IsNullOrWhiteSpace(item.FeedItem.PublishedDate)
                    ? null
                    : new IngestSourceEvidence(
                        item.FeedItem.PublishedDate,
                        publishedAt));
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

        return new MikanRssIngestResult(
            stored.Id,
            feed.MikanId,
            ruleSnapshot.Revision,
            legacy.Revision,
            legacy.Enabled,
            results);
    }

    private static MikanRssIngestItemResult Result(
        MikanRssPlannedItem item,
        string status,
        string? ingestTaskId,
        IReadOnlyList<string> errors) =>
        new(
            item.Candidate.Id,
            item.Decision.Kind,
            item.Decision.Reason,
            item.LegacyFilterAudit.State,
            item.LegacyFilterAudit.Reason,
            item.LegacyFilterAudit.MatchedScope,
            item.LegacyFilterAudit.MatchedKey,
            item.LegacyFilterAudit.IdentityMikanId,
            item.LegacyFilterAudit.IdentityGroupId,
            status,
            ingestTaskId,
            errors);
}
