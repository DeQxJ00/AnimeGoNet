namespace AnimeGoNet.Data.Mikan;

public sealed record MikanOffsetEvidenceObservation(
    int MikanId,
    int GroupId,
    int SourceEpisode,
    int TmdbSeriesId,
    int TmdbSeasonNumber,
    int EpisodeOffset);

public sealed record MikanTrustedOffset(
    int MikanId,
    int GroupId,
    int TmdbSeriesId,
    int TmdbSeasonNumber,
    int EpisodeOffset,
    int DistinctEpisodeCount,
    bool IsTrusted,
    DateTimeOffset UpdatedAtUtc);

public sealed record MikanTrustedEpisodeResolution(
    int MikanId,
    int GroupId,
    int TmdbSeriesId,
    int TmdbSeasonNumber,
    int SourceEpisode,
    int TmdbEpisodeNumber,
    int EpisodeOffset);
