using System.Text.Json.Serialization;

namespace AnimeGoNet.App.Metadata;

internal sealed record BangumiSubjectDto(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("name_cn")] string? ChineseName,
    [property: JsonPropertyName("date")] string? Date,
    [property: JsonPropertyName("eps")] int EpisodeCount,
    [property: JsonPropertyName("total_episodes")] int TotalEpisodeCount);

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(BangumiSubjectDto))]
internal sealed partial class BangumiJsonContext : JsonSerializerContext;
