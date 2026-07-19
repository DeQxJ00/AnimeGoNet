namespace AnimeGoNet.Core.Ingest;

public sealed record IngestBatchCommand(string Source, IReadOnlyList<IngestItemCommand> Items);

public sealed record IngestItemCommand(string? TorrentUrl, IngestItemInfo Info);

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
    string? ImdbId);

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
    string? ImdbId);

public sealed record IngestValidationResult(NormalizedIngestItem? Item, IReadOnlyList<string> Errors)
{
    public bool IsValid => Item is not null && Errors.Count == 0;
}
