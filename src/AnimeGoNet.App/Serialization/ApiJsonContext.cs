using System.Text.Json.Serialization;
using AnimeGoNet.App.Api;

namespace AnimeGoNet.App.Serialization;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(LegacyApiResponse<PingData>))]
[JsonSerializable(typeof(LegacyApiResponse<string>))]
[JsonSerializable(typeof(RuntimeStatus))]
[JsonSerializable(typeof(IngestBatchRequest))]
[JsonSerializable(typeof(IngestBatchResponse))]
[JsonSerializable(typeof(LegacyApiResponse<IngestBatchResponse?>))]
internal sealed partial class ApiJsonContext : JsonSerializerContext;
