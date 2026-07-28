using System.Text.Json.Serialization;

namespace AnimeGoNet.App.Metadata;

internal sealed record BangumiSubjectDto(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("name_cn")] string? ChineseName,
    [property: JsonPropertyName("date")] string? Date,
    [property: JsonPropertyName("eps")] int EpisodeCount,
    [property: JsonPropertyName("total_episodes")] int TotalEpisodeCount);

internal sealed record BangumiSubjectRelationDto(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("type")] int Type,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("name_cn")] string? ChineseName,
    [property: JsonPropertyName("relation")] string? Relation);

internal sealed record BangumiEpisodePageDto(
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("limit")] int Limit,
    [property: JsonPropertyName("offset")] int Offset,
    [property: JsonPropertyName("data")] BangumiEpisodeDto[]? Data);

internal sealed record BangumiEpisodeDto(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("type")] int Type,
    [property: JsonPropertyName("ep")] decimal? EpisodeNumber,
    [property: JsonPropertyName("airdate")] string? AirDate);

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(BangumiSubjectDto))]
[JsonSerializable(typeof(BangumiSubjectRelationDto[]))]
[JsonSerializable(typeof(BangumiEpisodePageDto))]
internal sealed partial class BangumiJsonContext : JsonSerializerContext;
