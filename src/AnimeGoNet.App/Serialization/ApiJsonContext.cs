using System.Text.Json.Serialization;
using AnimeGoNet.App.Api;
using AnimeGoNet.App.Downloads;

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
[JsonSerializable(typeof(DownloadListResponse))]
[JsonSerializable(typeof(MikanWorkRuleRequest))]
[JsonSerializable(typeof(MikanWorkRuleResponse))]
[JsonSerializable(typeof(MetadataRetryResponse))]
[JsonSerializable(typeof(MetadataTaskListResponse))]
[JsonSerializable(typeof(ApiErrorResponse))]
[JsonSerializable(typeof(DeletePreviewResponse))]
[JsonSerializable(typeof(CreateDeleteExecutionRequest))]
[JsonSerializable(typeof(CreateDeleteExecutionResponse))]
[JsonSerializable(typeof(DeleteExecutionStatusResponse))]
[JsonSerializable(typeof(QbittorrentTorrentInfo[]))]
[JsonSerializable(typeof(QbittorrentTorrentFile[]))]
internal sealed partial class ApiJsonContext : JsonSerializerContext;
