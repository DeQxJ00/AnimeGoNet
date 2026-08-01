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
    [property: JsonPropertyName("eta")] long Eta,
    [property: JsonPropertyName("num_seeds")] int Seeds,
    [property: JsonPropertyName("num_leechs")] int Peers,
    [property: JsonPropertyName("seeding_time")] long SeedingTimeSeconds);

public sealed record QbittorrentTorrentFile(
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("progress")] double Progress,
    [property: JsonPropertyName("priority")] int Priority);
