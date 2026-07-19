using System.Text.Json.Serialization;
using AnimeGoNet.App.Api;

namespace AnimeGoNet.App.Serialization;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(LegacyApiResponse<PingData>))]
[JsonSerializable(typeof(LegacyApiResponse<string>))]
[JsonSerializable(typeof(RuntimeStatus))]
internal sealed partial class ApiJsonContext : JsonSerializerContext;
