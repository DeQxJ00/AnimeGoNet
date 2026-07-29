using System.Text.Json.Serialization;

namespace AnimeGoNet.App.Metadata;

internal sealed record TmdbSearchResponse(
    [property: JsonPropertyName("total_results")] int TotalResults,
    [property: JsonPropertyName("results")] TmdbSeriesDto[]? Results);

internal sealed record TmdbSeriesDto(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("original_name")] string? OriginalName,
    [property: JsonPropertyName("first_air_date")] string? FirstAirDate,
    [property: JsonPropertyName("poster_path")] string? PosterPath,
    [property: JsonPropertyName("seasons")] TmdbSeasonDto[]? Seasons);

internal sealed record TmdbSeasonDto(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("season_number")] int SeasonNumber,
    [property: JsonPropertyName("air_date")] string? AirDate,
    [property: JsonPropertyName("episode_count")] int EpisodeCount,
    [property: JsonPropertyName("poster_path")] string? PosterPath,
    [property: JsonPropertyName("episodes")] TmdbEpisodeDto[]? Episodes);

internal sealed record TmdbEpisodeDto(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("air_date")] string? AirDate,
    [property: JsonPropertyName("season_number")] int SeasonNumber,
    [property: JsonPropertyName("episode_number")] int EpisodeNumber);

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = false)]
[JsonSerializable(typeof(TmdbSearchResponse))]
[JsonSerializable(typeof(TmdbSeriesDto))]
[JsonSerializable(typeof(TmdbSeasonDto))]
[JsonSerializable(typeof(TmdbEpisodeDto))]
internal sealed partial class TmdbJsonContext : JsonSerializerContext;
