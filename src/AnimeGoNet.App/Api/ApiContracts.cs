using System.Text.Json.Serialization;

namespace AnimeGoNet.App.Api;

public sealed record LegacyApiResponse<T>(int Code, string Msg, T Data);

public sealed record PingData(string Version, long Time);

public sealed record RuntimeStatus(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("database_schema_version")] int DatabaseSchemaVersion,
    [property: JsonPropertyName("native_aot")] bool NativeAot,
    [property: JsonPropertyName("runtime_identifier")] string RuntimeIdentifier,
    [property: JsonPropertyName("paths")] RuntimePaths Paths,
    [property: JsonPropertyName("capabilities")] RuntimeCapabilities Capabilities);

public sealed record RuntimePaths(
    [property: JsonPropertyName("data_path")] string DataPath,
    [property: JsonPropertyName("download_path")] string DownloadPath,
    [property: JsonPropertyName("save_path")] string SavePath);

public sealed record RuntimeCapabilities(
    [property: JsonPropertyName("configuration")] bool Configuration,
    [property: JsonPropertyName("sqlite")] bool Sqlite,
    [property: JsonPropertyName("unified_ingest")] bool UnifiedIngest,
    [property: JsonPropertyName("rss_rules")] bool RssRules,
    [property: JsonPropertyName("qbittorrent")] bool Qbittorrent,
    [property: JsonPropertyName("tmdb")] bool Tmdb,
    [property: JsonPropertyName("organizer")] bool Organizer);

public sealed record IngestBatchRequest(
    [property: JsonPropertyName("source")] string? Source,
    [property: JsonPropertyName("data")] IReadOnlyList<IngestItemRequest?>? Data);

public sealed record IngestItemRequest(
    [property: JsonPropertyName("torrent")] string? Torrent,
    [property: JsonPropertyName("info")] IngestItemInfoRequest? Info);

public sealed record IngestItemInfoRequest(
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("source_item_id")] string? SourceItemId,
    [property: JsonPropertyName("source_work_id")] string? SourceWorkId,
    [property: JsonPropertyName("mikan_url")] string? MikanUrl,
    [property: JsonPropertyName("url")] string? Url,
    [property: JsonPropertyName("mikanid")] int? MikanId,
    [property: JsonPropertyName("bgmid")] int? BangumiId,
    [property: JsonPropertyName("anidbid")] int? AniDbId,
    [property: JsonPropertyName("imdbid")] string? ImdbId);

public sealed record IngestBatchResponse(
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("accepted_count")] int AcceptedCount,
    [property: JsonPropertyName("rejected_count")] int RejectedCount,
    [property: JsonPropertyName("items")] IReadOnlyList<IngestItemResponse> Items);

public sealed record IngestItemResponse(
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("ingest_id")] string? IngestId,
    [property: JsonPropertyName("source_profile_id")] string? SourceProfileId,
    [property: JsonPropertyName("source_profile_revision")] long? SourceProfileRevision,
    [property: JsonPropertyName("downloader_id")] string? DownloaderId,
    [property: JsonPropertyName("torrent_url_fingerprint")] string? TorrentUrlFingerprint,
    [property: JsonPropertyName("info_hash")] string? InfoHash,
    [property: JsonPropertyName("file_count")] int? FileCount,
    [property: JsonPropertyName("errors")] IReadOnlyList<string> Errors);
