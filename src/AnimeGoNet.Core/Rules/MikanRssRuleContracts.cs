namespace AnimeGoNet.Core.Rules;

public sealed record MikanRssCandidate(
    string Id,
    string Title,
    int? MikanId,
    string? SourceEpisodeKind,
    string? SourceEpisode);

public sealed record NamedMatchArray(
    string Id,
    string Name,
    bool Enabled,
    IReadOnlyList<string> Values);

public sealed record PriorityGroup(
    string Id,
    string Name,
    IReadOnlyList<NamedMatchArray> Arrays);

public sealed record MikanRssRuleSet(
    IReadOnlyList<NamedMatchArray> Whitelist,
    IReadOnlyList<NamedMatchArray> Blacklist,
    IReadOnlyList<PriorityGroup> PriorityGroups);

public enum MikanRssDecisionKind
{
    Winner,
    RejectedByBlacklist,
    RejectedByWhitelist,
    SuppressedByHigherPriority,
}

public sealed record MikanRssDecision(
    string CandidateId,
    MikanRssDecisionKind Kind,
    string Reason,
    string? WinnerId,
    IReadOnlyList<string> EvaluatedPriorityGroups);
