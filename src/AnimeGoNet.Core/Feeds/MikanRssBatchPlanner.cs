using System.Security.Cryptography;
using System.Text;
using AnimeGo.Plugin.Abstractions;
using AnimeGoNet.Core.Metadata;
using AnimeGoNet.Core.Rules;

namespace AnimeGoNet.Core.Feeds;

public sealed record MikanRssPlannedItem(
    RssFeedItem FeedItem,
    MikanRssCandidate Candidate,
    MikanRssDecision Decision,
    MikanLegacyFilterAudit LegacyFilterAudit);

public sealed record MikanRssBatchPlan(
    int? MikanId,
    IReadOnlyList<MikanRssPlannedItem> Items,
    long LegacyFilterRevision,
    bool LegacyFilterEnabled)
{
    public IReadOnlyList<MikanRssPlannedItem> Winners =>
        Items.Where(item => item.Decision.Kind == MikanRssDecisionKind.Winner).ToArray();
}

public static class MikanRssBatchPlanner
{
    public static MikanRssBatchPlan Create(
        RssFeedDocument feed,
        MikanRssRuleSet rules,
        bool priorityEnabled = true,
        IReadOnlyList<MikanLegacyFilterAudit>? legacyFilterAudits = null,
        long legacyFilterRevision = 1,
        bool legacyFilterEnabled = false)
    {
        ArgumentNullException.ThrowIfNull(feed);
        var parsedEpisodes = feed.Items
            .Select(item => MikanRssEpisodeParser.Parse(item.Title))
            .ToArray();
        return CreateCore(
            feed,
            rules,
            parsedEpisodes,
            priorityEnabled,
            legacyFilterAudits,
            legacyFilterRevision,
            legacyFilterEnabled);
    }

    public static async ValueTask<MikanRssBatchPlan> CreateAsync(
        RssFeedDocument feed,
        MikanRssRuleSet rules,
        ITitleParserPlugin parser,
        bool priorityEnabled = true,
        IReadOnlyList<MikanLegacyFilterAudit>? legacyFilterAudits = null,
        long legacyFilterRevision = 1,
        bool legacyFilterEnabled = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(feed);
        ArgumentNullException.ThrowIfNull(parser);
        var parsedEpisodes = new TorrentEpisodeCandidate[feed.Items.Count];
        for (var index = 0; index < feed.Items.Count; index++)
        {
            var result = await parser.ParseAsync(
                new TitleParseContext(
                    feed.Items[index].Title,
                    null,
                    "mikan",
                    EmptyArguments),
                cancellationToken).ConfigureAwait(false);
            parsedEpisodes[index] = ToCandidate(result);
        }

        return CreateCore(
            feed,
            rules,
            parsedEpisodes,
            priorityEnabled,
            legacyFilterAudits,
            legacyFilterRevision,
            legacyFilterEnabled);
    }

    private static readonly Dictionary<string, string> EmptyArguments =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private static MikanRssBatchPlan CreateCore(
        RssFeedDocument feed,
        MikanRssRuleSet rules,
        TorrentEpisodeCandidate[] parsedEpisodes,
        bool priorityEnabled,
        IReadOnlyList<MikanLegacyFilterAudit>? legacyFilterAudits,
        long legacyFilterRevision,
        bool legacyFilterEnabled)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentOutOfRangeException.ThrowIfLessThan(legacyFilterRevision, 1);
        if (parsedEpisodes.Length != feed.Items.Count)
        {
            throw new ArgumentException("Parsed episode count must match feed item count.", nameof(parsedEpisodes));
        }
        if (legacyFilterAudits is not null && legacyFilterAudits.Count != feed.Items.Count)
        {
            throw new ArgumentException("Legacy filter audit count must match feed item count.", nameof(legacyFilterAudits));
        }

        var usedIds = new HashSet<string>(StringComparer.Ordinal);
        var candidates = new MikanRssCandidate[feed.Items.Count];
        for (var index = 0; index < feed.Items.Count; index++)
        {
            var item = feed.Items[index];
            var episode = parsedEpisodes[index];
            candidates[index] = new MikanRssCandidate(
                UniqueId(CreateStableId(item), usedIds),
                item.Title,
                feed.MikanId,
                Kind(episode.Kind),
                episode.SourceEpisode);
        }

        var audits = legacyFilterAudits?.ToArray()
            ?? Enumerable.Repeat(MikanLegacyFilterAudit.NotEvaluated, candidates.Length).ToArray();
        var eligibleCandidates = candidates.Where((_, index) => audits[index].Eligible).ToArray();
        IReadOnlyList<MikanRssDecision> eligibleDecisions = priorityEnabled
            ? MikanRssRuleEngine.Evaluate(eligibleCandidates, rules)
            : eligibleCandidates.Select(candidate => new MikanRssDecision(
                candidate.Id,
                MikanRssDecisionKind.Winner,
                "SkippedByConfiguration",
                candidate.Id,
                [])).ToArray();
        var decisionsById = eligibleDecisions.ToDictionary(decision => decision.CandidateId, StringComparer.Ordinal);
        var planned = new MikanRssPlannedItem[feed.Items.Count];
        for (var index = 0; index < feed.Items.Count; index++)
        {
            var audit = audits[index];
            var decision = audit.State switch
            {
                MikanLegacyFilterState.Rejected => new MikanRssDecision(
                    candidates[index].Id, MikanRssDecisionKind.RejectedByLegacyFilter,
                    audit.Reason, null, []),
                MikanLegacyFilterState.FilterEvaluationFailed => new MikanRssDecision(
                    candidates[index].Id, MikanRssDecisionKind.FilterEvaluationFailed,
                    audit.Reason, null, []),
                _ => decisionsById[candidates[index].Id],
            };
            planned[index] = new MikanRssPlannedItem(
                feed.Items[index],
                candidates[index],
                decision,
                audit);
        }

        return new MikanRssBatchPlan(feed.MikanId, planned, legacyFilterRevision, legacyFilterEnabled);
    }

    private static TorrentEpisodeCandidate ToCandidate(TitleParseResult result)
    {
        var kind = result.EpisodeKind switch
        {
            "normal" => TorrentEpisodeCandidateKind.Normal,
            "fractional" => TorrentEpisodeCandidateKind.Fractional,
            "special" => TorrentEpisodeCandidateKind.Special,
            _ => TorrentEpisodeCandidateKind.Unknown,
        };
        var normalEpisode = kind == TorrentEpisodeCandidateKind.Normal
            && result.Episode is > 0
            && decimal.Truncate(result.Episode.Value) == result.Episode.Value
            && result.Episode <= int.MaxValue
                ? decimal.ToInt32(result.Episode.Value)
                : (int?)null;
        if (kind == TorrentEpisodeCandidateKind.Normal && normalEpisode is null)
        {
            kind = TorrentEpisodeCandidateKind.Unknown;
        }

        return new TorrentEpisodeCandidate(
            kind,
            result.EpisodeText,
            normalEpisode,
            kind == TorrentEpisodeCandidateKind.Unknown
                ? result.Errors.Count > 0 ? result.Errors[0].Code : "episode_not_parsed"
                : null);
    }

    private static string CreateStableId(RssFeedItem item)
    {
        var identity = string.IsNullOrWhiteSpace(item.MikanUrl)
            ? item.TorrentUrl
            : item.MikanUrl;
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return Convert.ToHexStringLower(digest);
    }

    private static string UniqueId(string baseId, HashSet<string> usedIds)
    {
        if (usedIds.Add(baseId))
        {
            return baseId;
        }

        for (var suffix = 2; ; suffix++)
        {
            var candidate = string.Concat(baseId, "-", suffix.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (usedIds.Add(candidate))
            {
                return candidate;
            }
        }
    }

    private static string? Kind(TorrentEpisodeCandidateKind kind) => kind switch
    {
        TorrentEpisodeCandidateKind.Normal => "normal",
        TorrentEpisodeCandidateKind.Fractional => "fractional",
        TorrentEpisodeCandidateKind.Special => "special",
        _ => null,
    };
}
