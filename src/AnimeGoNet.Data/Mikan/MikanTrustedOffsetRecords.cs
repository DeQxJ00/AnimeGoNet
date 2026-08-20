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

public sealed record MikanOffsetCandidateState(
    int MikanId,
    int GroupId,
    int TmdbSeriesId,
    int TmdbSeasonNumber,
    int EpisodeOffset,
    int DistinctEpisodeCount,
    string State,
    DateTimeOffset UpdatedAtUtc);

public static class MikanTrustedOffsetBlacklistScope
{
    public const string MikanId = "mikanid";
    public const string GroupId = "groupid";
    public const string Pair = "pair";

    public static bool IsValid(string value) =>
        value is MikanId or GroupId or Pair;
}

public sealed record MikanTrustedOffsetBlacklistEntry(
    string Scope,
    int? MikanId,
    int? GroupId,
    DateTimeOffset CreatedAtUtc);
