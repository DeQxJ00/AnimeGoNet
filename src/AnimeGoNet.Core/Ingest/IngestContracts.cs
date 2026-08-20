using System.Text.Json.Serialization;

namespace AnimeGoNet.Core.Ingest;

public sealed record IngestBatchCommand(string Source, IReadOnlyList<IngestItemCommand> Items);

public sealed record IngestItemCommand(
    string? TorrentUrl,
    IngestItemInfo Info,
    [property: JsonIgnore] IngestSourceEvidence? SourceEvidence = null);

public sealed record IngestSourceEvidence(
    string? PublishedAtRaw,
    DateTimeOffset? PublishedAt);

public sealed record IngestItemInfo(
    string? Title,
    string? LegacyName,
    string? SourceItemId,
    string? SourceWorkId,
    string? MikanUrl,
    string? LegacyUrl,
    int? MikanId,
    int? BangumiId,
    int? AniDbId,
    string? ImdbId,
    int? GroupId = null);

public sealed record NormalizedIngestItem(
    string Source,
    Uri TorrentUrl,
    string TorrentUrlFingerprint,
    string Title,
    string? SourceItemId,
    string? SourceWorkId,
    int? MikanId,
    int? BangumiId,
    int? AniDbId,
    string? ImdbId,
    string? PublishedAtRaw = null,
    DateTimeOffset? PublishedAt = null,
    string? SourcePageUrl = null,
    int? GroupId = null);

public sealed record IngestValidationResult(NormalizedIngestItem? Item, IReadOnlyList<string> Errors)
{
    public bool IsValid => Item is not null && Errors.Count == 0;
}
