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

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(BangumiSubjectDto))]
[JsonSerializable(typeof(BangumiSubjectRelationDto[]))]
internal sealed partial class BangumiJsonContext : JsonSerializerContext;
