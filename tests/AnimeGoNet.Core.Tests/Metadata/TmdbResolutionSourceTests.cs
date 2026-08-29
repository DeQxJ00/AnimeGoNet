using AnimeGoNet.Core.Metadata;

namespace AnimeGoNet.Core.Tests.Metadata;

public sealed class TmdbResolutionSourceTests
{
    public static TheoryData<TmdbResolutionSource, string> StorageValues => new()
    {
        { TmdbResolutionSource.ManualMikanOverride, "manual_mikan_override" },
        { TmdbResolutionSource.TmdbTitle, "tmdb_title" },
        { TmdbResolutionSource.TmdbAirDate, "tmdb_air_date" },
        { TmdbResolutionSource.Backtrace, "backtrace" },
        { TmdbResolutionSource.AiMetadata, "ai_metadata" },
        { TmdbResolutionSource.TitleSeason, "title_season" },
        { TmdbResolutionSource.FirstSeason, "first_season" },
        { TmdbResolutionSource.TrustedMikanOffset, "trusted_mikan_offset" },
        { TmdbResolutionSource.ManualMikanOffset, "manual_mikan_offset" },
        { TmdbResolutionSource.TmdbEpisodeNumber, "tmdb_episode_number" },
        { TmdbResolutionSource.TmdbEpisodeBangumiDate, "tmdb_episode_bangumi_date" },
        { TmdbResolutionSource.TmdbEpisodeBangumiNearestDate, "tmdb_episode_bangumi_nearest_date" },
        { TmdbResolutionSource.SubtitleAssociation, "subtitle_association" },
        { TmdbResolutionSource.U2AniDbMapping, "u2_anidb_mapping" },
        { TmdbResolutionSource.U2AniDbTitleCache, "u2_anidb_title_cache" },
        { TmdbResolutionSource.U2AnitomyTitle, "u2_anitomy_title" },
    };

    [Theory]
    [MemberData(nameof(StorageValues))]
    public void StorageValueRoundTrips(
        TmdbResolutionSource source,
        string expected)
    {
        Assert.Equal(expected, source.ToStorageValue());
        Assert.Equal(source, expected.ParseTmdbResolutionSource());
    }

    [Fact]
    public void UnknownStorageValueIsRejected()
    {
        Assert.Throws<ArgumentException>(
            () => "invented_source".ParseTmdbResolutionSource());
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ((TmdbResolutionSource)999).ToStorageValue());
    }
}
