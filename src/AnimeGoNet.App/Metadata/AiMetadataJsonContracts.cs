using System.Text.Json.Serialization;
using AnimeGoNet.Core.Metadata;

namespace AnimeGoNet.App.Metadata;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(AiMetadataMatchCandidate))]
internal sealed partial class AiMetadataJsonContext : JsonSerializerContext;
