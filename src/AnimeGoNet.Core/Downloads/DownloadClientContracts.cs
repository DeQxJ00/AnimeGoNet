namespace AnimeGoNet.Core.Downloads;

public interface IDownloadClient
{
    Task ConnectAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DownloadTaskSnapshot>> ListAsync(CancellationToken cancellationToken = default);

    Task AddTorrentAsync(AddTorrentCommand command, CancellationToken cancellationToken = default);

    Task PauseAsync(IReadOnlyList<string> hashes, CancellationToken cancellationToken = default);

    Task ResumeAsync(IReadOnlyList<string> hashes, CancellationToken cancellationToken = default);

    Task DeleteAsync(IReadOnlyList<string> hashes, bool deleteFiles, CancellationToken cancellationToken = default);
}

public interface IDownloadClientRegistry
{
    IReadOnlyCollection<string> InstanceIds { get; }

    IDownloadClient GetRequired(string instanceId);
}

public sealed record AddTorrentCommand(
    Stream Torrent,
    string FileName,
    string SavePath,
    string? Rename,
    string? Category,
    IReadOnlyList<string> Tags,
    bool StartPaused = true);

public sealed record DownloadTaskSnapshot(
    string Hash,
    string Name,
    DownloadTaskState State,
    double Progress,
    long DownloadedBytes,
    long TotalBytes,
    long DownloadSpeedBytesPerSecond,
    long? EtaSeconds);

public enum DownloadTaskState
{
    Unknown,
    Waiting,
    Downloading,
    Moving,
    Seeding,
    Paused,
    Complete,
    Error,
}
