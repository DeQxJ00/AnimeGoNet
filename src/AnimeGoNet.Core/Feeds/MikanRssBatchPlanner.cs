using System.Security.Cryptography;
using System.Text;
using AnimeGoNet.Core.Metadata;
using AnimeGoNet.Core.Rules;

namespace AnimeGoNet.Core.Feeds;

public sealed record MikanRssPlannedItem(
    RssFeedItem FeedItem,
    MikanRssCandidate Candidate,
    MikanRssDecision Decision);

public sealed record MikanRssBatchPlan(
    int? MikanId,
    IReadOnlyList<MikanRssPlannedItem> Items)
{
    public IReadOnlyList<MikanRssPlannedItem> Winners =>
        Items.Where(item => item.Decision.Kind == MikanRssDecisionKind.Winner).ToArray();
}

public static class MikanRssBatchPlanner
{
    public static MikanRssBatchPlan Create(
        RssFeedDocument feed,
        MikanRssRuleSet rules,
        bool priorityEnabled = true)
    {
        ArgumentNullException.ThrowIfNull(feed);
        ArgumentNullException.ThrowIfNull(rules);

        var usedIds = new HashSet<string>(StringComparer.Ordinal);
        var candidates = new MikanRssCandidate[feed.Items.Count];
        for (var index = 0; index < feed.Items.Count; index++)
        {
            var item = feed.Items[index];
            var episode = MikanRssEpisodeParser.Parse(item.Title);
            candidates[index] = new MikanRssCandidate(
                UniqueId(CreateStableId(item), usedIds),
                item.Title,
                feed.MikanId,
                Kind(episode.Kind),
                episode.SourceEpisode);
        }

        IReadOnlyList<MikanRssDecision> decisions = priorityEnabled
            ? MikanRssRuleEngine.Evaluate(candidates, rules)
            : candidates.Select(candidate => new MikanRssDecision(
                candidate.Id,
                MikanRssDecisionKind.Winner,
                "SkippedByConfiguration",
                candidate.Id,
                [])).ToArray();
        var decisionsById = decisions.ToDictionary(decision => decision.CandidateId, StringComparer.Ordinal);
        var planned = new MikanRssPlannedItem[feed.Items.Count];
        for (var index = 0; index < feed.Items.Count; index++)
        {
            planned[index] = new MikanRssPlannedItem(
                feed.Items[index],
                candidates[index],
                decisionsById[candidates[index].Id]);
        }

        return new MikanRssBatchPlan(feed.MikanId, planned);
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
