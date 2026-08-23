using System.Globalization;
using System.Text.Json.Serialization;
using AnimeGo.Plugin.Abstractions;
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
    [property: JsonPropertyName("bgmid")] int? BangumiSubjectId,
    [property: JsonPropertyName("bgmid_discovery_state")] string BangumiDiscoveryState,
    [property: JsonPropertyName("bgmid_discovery_failure_code")] string? BangumiDiscoveryFailureCode,
    [property: JsonPropertyName("rule_revision")] long RuleRevision,
    [property: JsonPropertyName("legacy_filter_revision")] long LegacyFilterRevision,
    [property: JsonPropertyName("legacy_filter_enabled")] bool LegacyFilterEnabled,
    [property: JsonPropertyName("items")] IReadOnlyList<MikanRssIngestItemResult> Items,
    [property: JsonPropertyName("batches"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<MikanRssIngestResult>? Batches = null);

public sealed class MikanRssIngestProcessor(
    SourceProfileStore profiles,
    MikanRssRuleStore rules,
    MikanRssBatchStore batches,
    TitleParserManager parsers,
    OrderedFeedFilterManager filters,
    MikanFeedIdentityResolver identityResolver,
    MikanBangumiSubjectResolver bangumiResolver,
    MikanRssMultiFileCandidatePreflight multiFilePreflight,
    UnifiedIngestProcessor ingest,
    IHostApplicationLifetime applicationLifetime,
    DuplicateHitNotifier duplicateNotifier)
{
    private static readonly TimeSpan WinnerLeaseDuration = TimeSpan.FromMinutes(10);

    public Task<MikanRssIngestResult> ProcessAsync(
        RssFeedDocument feed,
        string sourceProfileId = "mikan",
        CancellationToken cancellationToken = default) =>
        ProcessAggregateAwareAsync(feed, sourceProfileId, null, cancellationToken);

    public Task<MikanRssIngestResult> ProcessScheduledAsync(
        RssFeedDocument feed,
        string sourceProfileId,
        long expectedSourceProfileRevision,
        CancellationToken cancellationToken = default) =>
        ProcessAggregateAwareAsync(
            feed,
            sourceProfileId,
            expectedSourceProfileRevision,
            cancellationToken);

    private async Task<MikanRssIngestResult> ProcessAggregateAwareAsync(
        RssFeedDocument feed,
        string sourceProfileId,
        long? expectedSourceProfileRevision,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(feed);
        if (feed.MikanId is > 0 || feed.Items.Count == 0)
        {
            return await ProcessCoreAsync(
                feed,
                sourceProfileId,
                expectedSourceProfileRevision,
                null,
                null,
                cancellationToken).ConfigureAwait(false);
        }

        var identities = await identityResolver
            .ResolveAsync(feed, sourceProfileId, cancellationToken)
            .ConfigureAwait(false);
        var work = identities
            .Where(item => item.Identity is not null)
            .GroupBy(item => item.Identity!.MikanId)
            .Select(group => new AggregateWork(
                group.Key,
                group.Select(item => item.ItemIndex).Order().ToArray(),
                group.OrderBy(item => item.ItemIndex).Select(item => item.Identity).ToArray(),
                null))
            .Concat(identities
                .Where(item => item.Identity is null)
                .Select(item => new AggregateWork(
                    null,
                    [item.ItemIndex],
                    [null],
                    item.FailureCode ?? "mikan_identity_unknown_failure")))
            .OrderBy(group => group.ItemIndexes[0])
            .ToArray();

        var completed = new List<AggregateResult>(work.Length);
        foreach (var group in work)
        {
            var childFeed = new RssFeedDocument(
                group.ItemIndexes.Select(index => feed.Items[index]).ToArray(),
                group.MikanId);
            var result = await ProcessCoreAsync(
                childFeed,
                sourceProfileId,
                expectedSourceProfileRevision,
                group.IdentityFailureCode,
                group.Identities,
                cancellationToken).ConfigureAwait(false);
            completed.Add(new AggregateResult(group.ItemIndexes, result));
        }

        if (completed.Count == 1)
        {
            return completed[0].Result;
        }

        var orderedItems = completed
            .SelectMany(group => group.ItemIndexes.Zip(
                group.Result.Items,
                (index, item) => (Index: index, Item: item)))
            .OrderBy(item => item.Index)
            .Select(item => item.Item)
            .ToArray();
        var first = completed[0].Result;
        return new MikanRssIngestResult(
            first.BatchId,
            null,
            null,
            "Multiple",
            null,
            first.RuleRevision,
            first.LegacyFilterRevision,
            first.LegacyFilterEnabled,
            orderedItems,
            completed.Select(group => group.Result).ToArray());
    }

    private async Task<MikanRssIngestResult> ProcessCoreAsync(
        RssFeedDocument feed,
        string sourceProfileId,
        long? expectedSourceProfileRevision,
        string? identityFailureCode,
        IReadOnlyList<MikanEpisodeIdentity?>? resolvedIdentities,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(feed);
        var profile = await profiles.GetEnabledAsync(
            sourceProfileId.Trim().ToLowerInvariant(), cancellationToken).ConfigureAwait(false)
            ?? throw new RssFeedException("rss_source_profile_missing", "Enabled RSS source profile was not found.");
        if (!string.Equals(profile.Adapter, "mikan", StringComparison.OrdinalIgnoreCase))
        {
            throw new RssFeedException("rss_source_profile_invalid", "RSS source profile is not a Mikan adapter.");
        }
        if (expectedSourceProfileRevision is { } expectedRevision
            && profile.Revision != expectedRevision)
        {
            throw new RssFeedException(
                "rss_source_profile_stale",
                "The RSS source profile changed before the scheduled feed could be processed.");
        }

        var ruleSnapshot = await rules.GetAsync(profile.Id, cancellationToken).ConfigureAwait(false)
            ?? throw new RssFeedException("rss_rules_missing", "RSS rules were not initialized.");
        var filterExecution = await filters.ExecuteAsync(
            new FilterContext(
                profile.Id,
                feed.Items.Select((item, index) => new FilterItem(
                    index,
                    item.Title,
                    item.TorrentUrl,
                    item.MikanUrl,
                    null,
                    feed.MikanId?.ToString(CultureInfo.InvariantCulture),
                    item.ContentType,
                    item.Length,
                    item.PublishedDate)).ToArray(),
                EmptyArguments,
                new FilterSourceProfileSnapshot(
                    profile.Revision,
                    profile.RssFilterEnabled,
                    profile.RssPriorityEnabled)),
            MikanFilterChain,
            cancellationToken).ConfigureAwait(false);
        if (!filterExecution.Succeeded)
        {
            var filterError = filterExecution.Errors[0];
            throw new RssFeedException(filterError.Code, filterError.Message);
        }
        if (filterExecution.Runs.Count != 1)
        {
            throw InvalidFilterResult();
        }

        var filterResult = filterExecution.Runs[0].Result;
        var legacy = WithResolvedIdentities(
            ToLegacyFilterBatch(filterResult, feed.Items.Count),
            resolvedIdentities);
        var plan = await MikanRssBatchPlanner.CreateAsync(
            feed,
            ruleSnapshot.Rules,
            parsers,
            "mikan-title",
            profile.RssPriorityEnabled,
            legacy.Audits,
            legacy.Revision,
            legacy.Enabled,
            cancellationToken).ConfigureAwait(false);
        MikanBangumiDiscovery? preflightDiscovery = null;
        MikanRssCandidatePreflightResult preflightResult;
        if (MikanRssMultiFileCandidatePreflight.ShouldRun(
                plan,
                profile.RssPriorityEnabled))
        {
            preflightDiscovery = identityFailureCode is null
                ? await bangumiResolver.ResolveAsync(feed, cancellationToken).ConfigureAwait(false)
                : new MikanBangumiDiscovery(
                    null,
                    MikanBangumiDiscoveryStates.NotApplicable,
                    identityFailureCode);
            preflightResult = preflightDiscovery.IsResolved
                ? await multiFilePreflight.RefineAsync(
                    feed,
                    plan,
                    ruleSnapshot.Rules,
                    profile,
                    preflightDiscovery.BangumiSubjectId!.Value,
                    cancellationToken).ConfigureAwait(false)
                : new MikanRssCandidatePreflightResult(plan);
        }
        else
        {
            preflightResult = new MikanRssCandidatePreflightResult(plan);
        }

        await using var stagedCandidateLease = preflightResult;
        plan = preflightResult.Plan;
        var stored = await batches.SaveAsync(
            profile.Id, ruleSnapshot.Revision, profile.RssPriorityEnabled,
            plan, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
        var earlyCompleted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in plan.Items.Where(item =>
                     item.Decision.Kind == MikanRssDecisionKind.Winner))
        {
            var audit = stored.Entries.Single(entry => entry.CandidateId == item.Candidate.Id);
            if (audit.EffectState == "ingested"
                || !TryGetEarlyCompletionIdentity(
                    item, stored.MikanId, out var sourceWorkId, out var sourceEpisode))
            {
                continue;
            }

            if (await batches.TryRecordCompletedWinnerAsync(
                    stored.Id,
                    item.Candidate.Id,
                    DateTimeOffset.UtcNow,
                    profile.Id,
                    sourceWorkId,
                    sourceEpisode,
                    cancellationToken).ConfigureAwait(false))
            {
                earlyCompleted.Add(item.Candidate.Id);
            }
        }

        var hasWinner = plan.Items.Any(item => item.Decision.Kind == MikanRssDecisionKind.Winner);
        var hasPendingWinner = plan.Items.Any(item =>
            item.Decision.Kind == MikanRssDecisionKind.Winner
            && !earlyCompleted.Contains(item.Candidate.Id)
            && stored.Entries.Single(entry => entry.CandidateId == item.Candidate.Id).EffectState != "ingested");
        if (!hasWinner)
        {
            if (stored.BangumiDiscovery.State == MikanBangumiDiscoveryStates.NotAttempted)
            {
                stored = await batches.SetBangumiDiscoveryAsync(
                    stored.Id,
                    new MikanBangumiDiscovery(
                        null,
                        MikanBangumiDiscoveryStates.NotApplicable,
                        "mikan_bgmid_no_winner"),
                    cancellationToken).ConfigureAwait(false);
            }
        }
        else if (!hasPendingWinner && !stored.BangumiDiscovery.IsResolved)
        {
            stored = await batches.SetBangumiDiscoveryAsync(
                stored.Id,
                new MikanBangumiDiscovery(
                    null,
                    MikanBangumiDiscoveryStates.NotApplicable,
                    "mikan_bgmid_no_pending_winner"),
                cancellationToken).ConfigureAwait(false);
        }
        else if (hasPendingWinner && !stored.BangumiDiscovery.IsResolved)
        {
            var discovery = preflightDiscovery
                ?? (identityFailureCode is null
                    ? await bangumiResolver.ResolveAsync(feed, cancellationToken).ConfigureAwait(false)
                    : new MikanBangumiDiscovery(
                        null,
                        MikanBangumiDiscoveryStates.NotApplicable,
                        identityFailureCode));
            stored = await batches.SetBangumiDiscoveryAsync(
                stored.Id, discovery, cancellationToken).ConfigureAwait(false);
        }
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

            if (earlyCompleted.Contains(item.Candidate.Id))
            {
                if (TryGetEarlyCompletionIdentity(
                        item,
                        stored.MikanId,
                        out var completedWorkId,
                        out var completedEpisode))
                {
                    NotifyDuplicate(
                        profile,
                        stored.Id,
                        completedWorkId,
                        completedEpisode,
                        "rss_completion_alias");
                }
                results.Add(Result(item, "already_completed", null, []));
                continue;
            }

            if (!stored.BangumiDiscovery.IsResolved)
            {
                if (TryGetEarlyCompletionIdentity(
                        item,
                        stored.MikanId,
                        out var failedDiscoveryWorkId,
                        out var failedDiscoveryEpisode)
                    && await batches.TryRecordCompletedWinnerAsync(
                        stored.Id,
                        item.Candidate.Id,
                        DateTimeOffset.UtcNow,
                        profile.Id,
                        failedDiscoveryWorkId,
                        failedDiscoveryEpisode,
                        cancellationToken).ConfigureAwait(false))
                {
                    NotifyDuplicate(
                        profile,
                        stored.Id,
                        failedDiscoveryWorkId,
                        failedDiscoveryEpisode,
                        "rss_completion_alias");
                    results.Add(Result(item, "already_completed", null, []));
                    continue;
                }

                results.Add(Result(
                    item,
                    "bgmid_discovery_failed",
                    null,
                    [stored.BangumiDiscovery.FailureCode ?? "mikan_bgmid_discovery_failed"]));
                continue;
            }

            MikanRssWinnerClaimResult claim;
            if (TryGetEarlyCompletionIdentity(
                    item, stored.MikanId, out var sourceWorkId, out var sourceEpisode))
            {
                claim = await batches.TryClaimWinnerWithCompletionCheckAsync(
                    stored.Id,
                    item.Candidate.Id,
                    DateTimeOffset.UtcNow,
                    WinnerLeaseDuration,
                    profile.Id,
                    sourceWorkId,
                    sourceEpisode,
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var basicLease = await batches.TryClaimWinnerAsync(
                    stored.Id,
                    item.Candidate.Id,
                    DateTimeOffset.UtcNow,
                    WinnerLeaseDuration,
                    cancellationToken).ConfigureAwait(false);
                claim = new MikanRssWinnerClaimResult(
                    basicLease is null
                        ? MikanRssWinnerClaimState.Unavailable
                        : MikanRssWinnerClaimState.Claimed,
                    basicLease,
                    null,
                    null);
            }
            if (claim.State == MikanRssWinnerClaimState.AlreadyCompleted)
            {
                NotifyDuplicate(
                    profile,
                    stored.Id,
                    sourceWorkId,
                    sourceEpisode,
                    "rss_completion_alias");
                results.Add(Result(item, "already_completed", null, []));
                continue;
            }

            var lease = claim.Lease;
            if (lease is null)
            {
                NotifyDuplicate(
                    profile,
                    stored.Id,
                    null,
                    null,
                    "rss_winner_already_claimed");
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
                    stored.BangumiDiscovery.BangumiSubjectId, null, null),
                string.IsNullOrWhiteSpace(item.FeedItem.PublishedDate)
                    ? null
                    : new IngestSourceEvidence(
                        item.FeedItem.PublishedDate,
                        publishedAt));
            UnifiedIngestItemResult outcome;
            try
            {
                var preStagedTorrent = preflightResult.TakeStagedTorrent(item.Candidate.Id);
                outcome = preStagedTorrent is null
                    ? await ingest.ProcessRssWinnerAsync(
                        profile,
                        command,
                        lease,
                        cancellationToken).ConfigureAwait(false)
                    : await ingest.ProcessRssWinnerAsync(
                        profile,
                        command,
                        lease,
                        preStagedTorrent,
                        cancellationToken).ConfigureAwait(false);
                if (!outcome.Accepted)
                {
                    _ = await batches.ReleaseWinnerAsync(lease, cancellationToken).ConfigureAwait(false);
                }
            }
            catch
            {
                try
                {
                    _ = await batches.ReleaseWinnerAsync(
                        lease,
                        applicationLifetime.ApplicationStopping).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                    when (applicationLifetime.ApplicationStopping.IsCancellationRequested)
                {
                    // The lease expires and becomes reclaimable after shutdown.
                }
                throw;
            }

            results.Add(Result(
                item, outcome.Status, outcome.IngestId, outcome.Errors));
        }

        return new MikanRssIngestResult(
            stored.Id,
            feed.MikanId,
            stored.BangumiDiscovery.BangumiSubjectId,
            stored.BangumiDiscovery.State,
            stored.BangumiDiscovery.FailureCode,
            ruleSnapshot.Revision,
            legacy.Revision,
            legacy.Enabled,
            results);
    }

    private sealed record AggregateWork(
        int? MikanId,
        IReadOnlyList<int> ItemIndexes,
        IReadOnlyList<MikanEpisodeIdentity?> Identities,
        string? IdentityFailureCode);

    private sealed record AggregateResult(
        IReadOnlyList<int> ItemIndexes,
        MikanRssIngestResult Result);

    private void NotifyDuplicate(
        SourceProfileRecord profile,
        string batchId,
        string? sourceWorkId,
        string? sourceEpisode,
        string reason)
    {
        var scope = sourceWorkId is null || sourceEpisode is null
            ? $"rss-batch:{batchId}"
            : $"source-work:{sourceWorkId}:ep:{sourceEpisode}:batch:{batchId}";
        duplicateNotifier.Notify(
            profile.DuplicateNotificationEnabled,
            profile.Id,
            profile.Id,
            scope,
            reason);
    }

    private static bool TryGetEarlyCompletionIdentity(
        MikanRssPlannedItem item,
        int? mikanId,
        out string sourceWorkId,
        out string sourceEpisode)
    {
        sourceWorkId = string.Empty;
        sourceEpisode = string.Empty;
        if (!string.Equals(item.Candidate.SourceEpisodeKind, "normal", StringComparison.Ordinal)
            || mikanId is not > 0
            || !int.TryParse(
                item.Candidate.SourceEpisode,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var parsedEpisode)
            || parsedEpisode <= 0)
        {
            return false;
        }

        sourceWorkId = mikanId.Value.ToString(CultureInfo.InvariantCulture);
        sourceEpisode = parsedEpisode.ToString(CultureInfo.InvariantCulture);
        return true;
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

    private static readonly Dictionary<string, string> EmptyArguments =
        new(StringComparer.Ordinal);
    private static readonly string[] MikanFilterChain = ["mikan-tool"];

    private static MikanLegacyFilterBatch ToLegacyFilterBatch(
        FilterResult result,
        int expectedItemCount)
    {
        var error = result.Errors.Count > 0 ? result.Errors[0] : null;
        if (error is not null)
        {
            throw new RssFeedException(error.Code, error.Message);
        }

        if (!result.Metadata.TryGetValue("revision", out var revisionValue)
            || !long.TryParse(
                revisionValue,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var revision)
            || revision < 1
            || !result.Metadata.TryGetValue("enabled", out var enabledValue)
            || !bool.TryParse(enabledValue, out var enabled)
            || result.Decisions.Count != expectedItemCount)
        {
            throw InvalidFilterResult();
        }

        var audits = new MikanLegacyFilterAudit[expectedItemCount];
        var usedIndexes = new HashSet<int>();
        foreach (var decision in result.Decisions)
        {
            if (decision.Index < 0
                || decision.Index >= expectedItemCount
                || !usedIndexes.Add(decision.Index)
                || !Enum.TryParse<MikanLegacyFilterState>(
                    decision.Outcome,
                    ignoreCase: false,
                    out var state)
                || decision.Accepted != new MikanLegacyFilterAudit(state, decision.Reason).Eligible)
            {
                throw InvalidFilterResult();
            }

            decision.Metadata.TryGetValue("matched_scope", out var matchedScope);
            decision.Metadata.TryGetValue("matched_key", out var matchedKey);
            decision.Metadata.TryGetValue("identity_mikanid", out var identityMikanId);
            decision.Metadata.TryGetValue("identity_groupid", out var identityGroupId);
            audits[decision.Index] = new MikanLegacyFilterAudit(
                state,
                decision.Reason,
                matchedScope,
                matchedKey,
                ParseNullablePositiveInt(identityMikanId),
                ParseNullablePositiveInt(identityGroupId));
        }

        return new MikanLegacyFilterBatch(revision, enabled, audits);
    }

    private static MikanLegacyFilterBatch WithResolvedIdentities(
        MikanLegacyFilterBatch batch,
        IReadOnlyList<MikanEpisodeIdentity?>? identities)
    {
        if (identities is null)
        {
            return batch;
        }
        if (identities.Count != batch.Audits.Count)
        {
            throw InvalidFilterResult();
        }

        return batch with
        {
            Audits = batch.Audits.Select((audit, index) => identities[index] is { } identity
                ? audit with
                {
                    IdentityMikanId = identity.MikanId,
                    IdentityGroupId = identity.SubGroupId > 0 ? identity.SubGroupId : null,
                }
                : audit).ToArray(),
        };
    }

    private static int? ParseNullablePositiveInt(string? value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
        && parsed > 0
            ? parsed
            : null;

    private static RssFeedException InvalidFilterResult() =>
        new("plugin_filter_invalid_result", "Mikan filter plugin returned an invalid result.");
}
