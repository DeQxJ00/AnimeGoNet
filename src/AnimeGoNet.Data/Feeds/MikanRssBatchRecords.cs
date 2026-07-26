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
    string? IngestTaskId);

public sealed record MikanRssWinnerLease(
    string BatchId,
    string CandidateId,
    string LeaseToken,
    DateTimeOffset LeaseExpiresAtUtc);
