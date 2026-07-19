using System.Text.Json.Serialization;

namespace AnimeGoNet.App.Downloads;

public sealed record QbittorrentTorrentInfo(
    [property: JsonPropertyName("hash")] string Hash,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("progress")] double Progress,
    [property: JsonPropertyName("downloaded")] long Downloaded,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("dlspeed")] long DownloadSpeed,
    [property: JsonPropertyName("eta")] long Eta);
