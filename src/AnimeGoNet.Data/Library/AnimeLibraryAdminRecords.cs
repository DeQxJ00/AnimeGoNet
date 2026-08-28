namespace AnimeGoNet.Data.Library;

public enum AnimeLibraryMutationStatus
{
    Created = 1,
    Updated = 2,
    Deleted = 3,
    NotFound = 4,
    AlreadyExists = 5,
    RevisionConflict = 6,
    InUse = 7,
}

public sealed record AnimeLibraryReferenceSummary(
    int TaskFiles,
    int CompletionRecords,
    int EpisodeClaims,
    int MikanWorkRules,
    int FallbackCompletionRecords,
    int PendingNfoRewriteJobs)
{
    public int Total =>
        TaskFiles
        + CompletionRecords
        + EpisodeClaims
        + MikanWorkRules
        + FallbackCompletionRecords
        + PendingNfoRewriteJobs;
}

public sealed record AnimeLibraryMutationResult(
    AnimeLibraryMutationStatus Status,
    int TmdbSeriesId,
    int SeasonNumber,
    string? ResourceRevision,
    bool SeriesRemoved = false,
    AnimeLibraryReferenceSummary? References = null);

public sealed record AnimeMovieReferenceSummary(
    int TaskFiles,
    int CompletionRecords,
    int ActiveClaims)
{
    public int Total => TaskFiles + CompletionRecords + ActiveClaims;
}

public sealed record AnimeMovieMutationResult(
    AnimeLibraryMutationStatus Status,
    int TmdbMovieId,
    string? ResourceRevision,
    AnimeMovieReferenceSummary? References = null);

public sealed record AnimeMovieFileContext(
    int TmdbMovieId,
    string ResourceRevision,
    string? MainMediaPath,
    AnimeMovieReferenceSummary References);
