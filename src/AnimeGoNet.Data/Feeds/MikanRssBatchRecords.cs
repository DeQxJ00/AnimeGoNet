using AnimeGoNet.Core.Feeds;
using AnimeGoNet.Core.Rules;

namespace AnimeGoNet.Data.Feeds;

public sealed record MikanRssBatchRecord(
    string Id,
    string SourceProfileId,
    long RuleRevision,
    string Fingerprint,
    int? MikanId,
    bool PriorityEnabled,
    long LegacyFilterRevision,
    bool LegacyFilterEnabled,
    MikanBangumiDiscovery BangumiDiscovery,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<MikanRssBatchEntryRecord> Entries);

public sealed record MikanRssBatchEntryRecord(
    string CandidateId,
    string Title,
    string MikanUrl,
    string TorrentUrlFingerprint,
    string ContentType,
    long LengthBytes,
    string? PublishedDate,
    string? SourceEpisodeKind,
    string? SourceEpisode,
    MikanRssDecision Decision,
    MikanLegacyFilterAudit LegacyFilterAudit,
    string EffectState,
    string? IngestTaskId,
    string? EarlyCompletionId,
    string? EarlyCompletionAliasId,
    DateTimeOffset? EarlyCompletionCheckedAtUtc);

public sealed record MikanRssWinnerLease(
    string BatchId,
    string CandidateId,
    string LeaseToken,
    DateTimeOffset LeaseExpiresAtUtc);

public enum MikanRssWinnerClaimState
{
    Claimed,
    AlreadyCompleted,
    Unavailable,
}

public sealed record MikanRssWinnerClaimResult(
    MikanRssWinnerClaimState State,
    MikanRssWinnerLease? Lease,
    string? CompletionId,
    string? CompletionAliasId);
