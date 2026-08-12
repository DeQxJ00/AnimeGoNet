namespace AnimeGoNet.Core.Metadata;

public enum TmdbResolutionSource
{
    ManualMikanOverride,
    TmdbTitle,
    TmdbAirDate,
    Backtrace,
    AiMetadata,
    TitleSeason,
    FirstSeason,
    TrustedMikanOffset,
    ManualMikanOffset,
    TmdbEpisodeNumber,
    TmdbEpisodeBangumiDate,
    TmdbEpisodeBangumiNearestDate,
    SubtitleAssociation,
}

public sealed record TmdbResolutionEvidence(
    TmdbResolutionSource Source,
    string RunId,
    string AttemptId)
{
    public string Strategy => Source.ToStorageValue();
}

public static class TmdbResolutionSourceExtensions
{
    public static string ToStorageValue(this TmdbResolutionSource source) =>
        source switch
        {
            TmdbResolutionSource.ManualMikanOverride =>
                "manual_mikan_override",
            TmdbResolutionSource.TmdbTitle => "tmdb_title",
            TmdbResolutionSource.TmdbAirDate => "tmdb_air_date",
            TmdbResolutionSource.Backtrace => "backtrace",
            TmdbResolutionSource.AiMetadata => "ai_metadata",
            TmdbResolutionSource.TitleSeason => "title_season",
            TmdbResolutionSource.FirstSeason => "first_season",
            TmdbResolutionSource.TrustedMikanOffset =>
                "trusted_mikan_offset",
            TmdbResolutionSource.ManualMikanOffset =>
                "manual_mikan_offset",
            TmdbResolutionSource.TmdbEpisodeNumber =>
                "tmdb_episode_number",
            TmdbResolutionSource.TmdbEpisodeBangumiDate =>
                "tmdb_episode_bangumi_date",
            TmdbResolutionSource.TmdbEpisodeBangumiNearestDate =>
                "tmdb_episode_bangumi_nearest_date",
            TmdbResolutionSource.SubtitleAssociation =>
                "subtitle_association",
            _ => throw new ArgumentOutOfRangeException(nameof(source)),
        };

    public static TmdbResolutionSource ParseTmdbResolutionSource(
        this string value) =>
        value switch
        {
            "manual_mikan_override" =>
                TmdbResolutionSource.ManualMikanOverride,
            "tmdb_title" => TmdbResolutionSource.TmdbTitle,
            "tmdb_air_date" => TmdbResolutionSource.TmdbAirDate,
            "backtrace" => TmdbResolutionSource.Backtrace,
            "ai_metadata" => TmdbResolutionSource.AiMetadata,
            "title_season" => TmdbResolutionSource.TitleSeason,
            "first_season" => TmdbResolutionSource.FirstSeason,
            "trusted_mikan_offset" =>
                TmdbResolutionSource.TrustedMikanOffset,
            "manual_mikan_offset" =>
                TmdbResolutionSource.ManualMikanOffset,
            "tmdb_episode_number" =>
                TmdbResolutionSource.TmdbEpisodeNumber,
            "tmdb_episode_bangumi_date" =>
                TmdbResolutionSource.TmdbEpisodeBangumiDate,
            "tmdb_episode_bangumi_nearest_date" =>
                TmdbResolutionSource.TmdbEpisodeBangumiNearestDate,
            "subtitle_association" =>
                TmdbResolutionSource.SubtitleAssociation,
            _ => throw new ArgumentException(
                "TMDB resolution source is not recognized.",
                nameof(value)),
        };
}
