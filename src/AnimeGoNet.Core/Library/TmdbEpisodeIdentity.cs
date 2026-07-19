namespace AnimeGoNet.Core.Library;

public readonly record struct TmdbEpisodeIdentity
{
    public TmdbEpisodeIdentity(long seriesId, int seasonNumber, int episodeNumber)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(seriesId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(seasonNumber);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(episodeNumber);
        SeriesId = seriesId;
        SeasonNumber = seasonNumber;
        EpisodeNumber = episodeNumber;
    }

    public long SeriesId { get; }

    public int SeasonNumber { get; }

    public int EpisodeNumber { get; }
}
