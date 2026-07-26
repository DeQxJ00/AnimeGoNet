using System.Text.Json.Serialization;
using AnimeGoNet.App.Api;
using AnimeGoNet.App.Downloads;
using AnimeGoNet.App.Feeds;

namespace AnimeGoNet.App.Serialization;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(LegacyApiResponse<PingData>))]
[JsonSerializable(typeof(LegacyApiResponse<string>))]
[JsonSerializable(typeof(LegacyPluginConfigUploadRequest))]
[JsonSerializable(typeof(LegacyApiResponse<LegacyPluginResponse?>))]
[JsonSerializable(typeof(LegacyApiResponse<LegacyPluginConfigResponse?>))]
[JsonSerializable(typeof(RuntimeStatus))]
[JsonSerializable(typeof(IngestBatchRequest))]
[JsonSerializable(typeof(LegacyRssRequest))]
[JsonSerializable(typeof(LegacyApiResponse<MikanRssIngestResult?>))]
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
[JsonSerializable(typeof(RssRuleSetRequest))]
[JsonSerializable(typeof(RssRuleSetResponse))]
[JsonSerializable(typeof(RssRulePreviewRequest))]
[JsonSerializable(typeof(RssRulePreviewResponse))]
[JsonSerializable(typeof(SourceProfileCreateRequest))]
[JsonSerializable(typeof(SourceProfileUpdateRequest))]
[JsonSerializable(typeof(SourceProfileResponse))]
[JsonSerializable(typeof(SourceProfileListResponse))]
[JsonSerializable(typeof(SourceProfileDeleteResponse))]
[JsonSerializable(typeof(DownloaderInstanceResponse))]
[JsonSerializable(typeof(DownloaderInstanceListResponse))]
[JsonSerializable(typeof(DownloaderConnectionTestResponse))]
[JsonSerializable(typeof(QbittorrentTorrentInfo[]))]
[JsonSerializable(typeof(QbittorrentTorrentFile[]))]
internal sealed partial class ApiJsonContext : JsonSerializerContext;
