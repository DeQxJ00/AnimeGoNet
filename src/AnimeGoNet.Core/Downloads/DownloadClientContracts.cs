namespace AnimeGoNet.Core.Downloads;

public interface IDownloadClient
{
    Task ConnectAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DownloadTaskSnapshot>> ListAsync(CancellationToken cancellationToken = default);

    Task AddTorrentAsync(AddTorrentCommand command, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DownloadFileSnapshot>> ListFilesAsync(
        string hash,
        CancellationToken cancellationToken = default);

    Task SetFilePriorityAsync(
        string hash,
        IReadOnlyList<int> fileIndexes,
        int priority,
        CancellationToken cancellationToken = default);

    Task AddTagsAsync(
        IReadOnlyList<string> hashes,
        IReadOnlyList<string> tags,
        CancellationToken cancellationToken = default);

    Task PauseAsync(IReadOnlyList<string> hashes, CancellationToken cancellationToken = default);

    Task ResumeAsync(IReadOnlyList<string> hashes, CancellationToken cancellationToken = default);

    Task DeleteAsync(IReadOnlyList<string> hashes, bool deleteFiles, CancellationToken cancellationToken = default);
}

public interface IDownloadClientRegistry
{
    IReadOnlyCollection<string> InstanceIds { get; }

    IDownloadClient GetRequired(string instanceId);
}

public interface IDownloadClientDiagnostics
{
    Task<string> GetVersionAsync(CancellationToken cancellationToken = default);

    Task<string> GetDefaultSavePathAsync(CancellationToken cancellationToken = default);
}

public sealed record AddTorrentCommand(
    Stream Torrent,
    string FileName,
    string SavePath,
    string? Rename,
    string? Category,
    IReadOnlyList<string> Tags,
    bool StartPaused = true,
    int SeedingTimeMinutes = 0);

public sealed record DownloadTaskSnapshot(
    string Hash,
    string Name,
    DownloadTaskState State,
    double Progress,
    long DownloadedBytes,
    long TotalBytes,
    long DownloadSpeedBytesPerSecond,
    long? EtaSeconds,
    int Seeds = 0,
    int Peers = 0,
    long SeedingTimeSeconds = 0);

public sealed record DownloadFileSnapshot(
    int Index,
    string RelativePath,
    long SizeBytes,
    double Progress,
    int Priority)
{
    public bool Wanted => Priority > 0;
}

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
