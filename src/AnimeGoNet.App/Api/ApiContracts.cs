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
