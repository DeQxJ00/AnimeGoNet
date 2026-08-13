namespace AnimeGoNet.Data.Library;

public sealed record ExternalMediaImportItem(
    int TmdbSeriesId,
    int TmdbSeasonNumber,
    int? TmdbEpisodeNumber,
    string RelativePath,
    string Status,
    string? ReasonCode);

public sealed record ExternalMediaImportResult(
    int ScannedSeasonCount,
    int CandidateFileCount,
    int ImportedCount,
    int AlreadyRecordedCount,
    int SkippedCount,
    IReadOnlyList<ExternalMediaImportItem> Items);
