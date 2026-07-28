using AnimeGoNet.Core.Metadata;

namespace AnimeGoNet.Data.Metadata;

public sealed record PendingTmdbRecoveryMapping(
    string FallbackCompletionId,
    TmdbSeason Season,
    TmdbEpisode Episode);

public sealed record PendingTmdbRecoveryRequest(
    int BangumiSubjectId,
    TmdbSeries Series,
    IReadOnlyList<PendingTmdbRecoveryMapping> Mappings,
    string ResolutionSource);

public sealed record PendingTmdbRecoveryItemResult(
    string FallbackCompletionId,
    int TmdbSeasonNumber,
    int TmdbEpisodeNumber,
    string State,
    string CompletionId);

public sealed record PendingTmdbRecoveryResult(
    int BangumiSubjectId,
    int TmdbSeriesId,
    IReadOnlyList<PendingTmdbRecoveryItemResult> Items,
    bool HasPendingFallbackRecords);
