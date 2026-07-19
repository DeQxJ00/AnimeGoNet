namespace AnimeGoNet.Core.Downloads;

public static class QbittorrentStateMapper
{
    public static DownloadTaskState Map(string? state, double progress) => state switch
    {
        "downloading" or "forcedDL" => DownloadTaskState.Downloading,
        "moving" => DownloadTaskState.Moving,
        "uploading" or "stalledUP" or "forcedUP" => DownloadTaskState.Seeding,
        "stoppedDL" or "pausedDL" => DownloadTaskState.Paused,
        "stoppedUP" or "pausedUP" or "checkingUP" => DownloadTaskState.Complete,
        "error" or "missingFiles" => DownloadTaskState.Error,
        "allocating" or "metaDL" or "stalledDL" or "checkingDL" or "checkingResumeData"
            or "queuedDL" or "queuedUP" => progress >= 1 ? DownloadTaskState.Complete : DownloadTaskState.Waiting,
        _ => DownloadTaskState.Unknown,
    };
}
