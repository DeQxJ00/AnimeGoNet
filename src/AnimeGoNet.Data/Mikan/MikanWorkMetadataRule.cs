namespace AnimeGoNet.Data.Mikan;

public sealed record MikanWorkMetadataRule(
    int MikanId,
    int? BangumiSubjectId,
    int? TmdbSeriesId,
    int? TmdbSeasonNumber,
    int? EpisodeOffset,
    bool Enabled,
    long Revision,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record MikanWorkMetadataRuleUpdate(
    int MikanId,
    int? BangumiSubjectId,
    int? TmdbSeriesId,
    int? TmdbSeasonNumber,
    int? EpisodeOffset,
    bool Enabled = true);

public sealed class MikanWorkMetadataRuleRevisionException(int mikanId, long expectedRevision)
    : InvalidOperationException($"Mikan work metadata rule revision conflict ({mikanId}, {expectedRevision}).")
{
    public int MikanId { get; } = mikanId;

    public long ExpectedRevision { get; } = expectedRevision;
}
