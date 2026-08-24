namespace AnimeGoNet.Core.Metadata;

public enum MetadataFailureKind
{
    SemanticNoMatch = 1,
    Network = 2,
    RemoteService = 3,
    Authentication = 4,
    Configuration = 5,
    Protocol = 6,
    InvalidInput = 7,
    Ambiguous = 8,
    Cancelled = 9,
}

public sealed record TmdbSeries(
    int Id,
    string Name,
    string OriginalName,
    DateOnly? FirstAirDate,
    string? PosterPath = null);

public sealed record TmdbSeason(
    int Id,
    int SeriesId,
    int SeasonNumber,
    string Name,
    DateOnly? AirDate,
    int EpisodeCount,
    string? PosterPath = null,
    IReadOnlyList<TmdbEpisode>? Episodes = null);

public sealed record TmdbEpisode(
    int Id,
    int SeriesId,
    int SeasonNumber,
    int EpisodeNumber,
    string Name,
    DateOnly? AirDate);

public sealed record TmdbSeriesDetails(
    TmdbSeries Series,
    IReadOnlyList<TmdbSeason> Seasons);

public sealed record TmdbMovie(
    int Id,
    string Title,
    string OriginalTitle,
    DateOnly? ReleaseDate,
    string? PosterPath = null);

public sealed record BangumiSubject(
    int Id,
    string Name,
    string ChineseName,
    DateOnly? AirDate,
    int EpisodeCount);

public sealed record BangumiSubjectRelation(
    int Id,
    int Type,
    string Name,
    string ChineseName,
    string Relation);

public sealed record BangumiEpisode(
    int Id,
    int Type,
    decimal? EpisodeNumber,
    DateOnly? AirDate,
    decimal? SortNumber = null);

public sealed record TmdbCanonicalEpisode(
    TmdbSeries Series,
    TmdbSeason Season,
    TmdbEpisode Episode,
    string CanonicalSeriesName);

public sealed record MetadataFailure(
    MetadataFailureKind Kind,
    string Code,
    bool TmdbAccessConfirmed);

public sealed record TmdbValidationResult(
    TmdbCanonicalEpisode? Value,
    MetadataFailure? Failure)
{
    public bool IsSuccess => Value is not null && Failure is null;
}

public interface ITmdbClient
{
    Task<IReadOnlyList<TmdbSeries>> SearchSeriesAsync(
        string title,
        CancellationToken cancellationToken = default);

    Task<TmdbSeries?> GetSeriesAsync(
        int seriesId,
        CancellationToken cancellationToken = default);

    Task<TmdbSeriesDetails?> GetSeriesDetailsAsync(
        int seriesId,
        CancellationToken cancellationToken = default);

    Task<TmdbSeason?> GetSeasonAsync(
        int seriesId,
        int seasonNumber,
        CancellationToken cancellationToken = default);

    Task<TmdbEpisode?> GetEpisodeAsync(
        int seriesId,
        int seasonNumber,
        int episodeNumber,
        CancellationToken cancellationToken = default);
}

public interface ITmdbRefreshClient : ITmdbClient
{
    Task<IReadOnlyList<TmdbSeries>> RefreshSeriesSearchAsync(
        string title,
        CancellationToken cancellationToken = default);

    Task<TmdbSeriesDetails?> RefreshSeriesDetailsAsync(
        int seriesId,
        CancellationToken cancellationToken = default);

    Task<TmdbSeason?> RefreshSeasonAsync(
        int seriesId,
        int seasonNumber,
        CancellationToken cancellationToken = default);

    Task<TmdbEpisode?> RefreshEpisodeAsync(
        int seriesId,
        int seasonNumber,
        int episodeNumber,
        CancellationToken cancellationToken = default);
}

public interface ITmdbMovieClient
{
    Task<IReadOnlyList<TmdbMovie>> SearchMoviesAsync(
        string title,
        CancellationToken cancellationToken = default);

    Task<TmdbMovie?> GetMovieAsync(
        int movieId,
        CancellationToken cancellationToken = default);
}

public interface IBangumiSubjectClient
{
    Task<BangumiSubject?> GetSubjectAsync(
        int subjectId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BangumiSubjectRelation>> GetRelatedSubjectsAsync(
        int subjectId,
        CancellationToken cancellationToken = default);
}

public interface IBangumiEpisodeClient
{
    Task<IReadOnlyList<BangumiEpisode>> GetEpisodesAsync(
        int subjectId,
        CancellationToken cancellationToken = default);
}

public interface IBangumiEpisodeRefreshClient : IBangumiEpisodeClient
{
    Task<IReadOnlyList<BangumiEpisode>> RefreshEpisodesAsync(
        int subjectId,
        CancellationToken cancellationToken = default);
}
