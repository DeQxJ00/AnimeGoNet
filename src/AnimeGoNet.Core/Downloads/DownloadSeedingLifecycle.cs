using AnimeGoNet.Core.Configuration;

namespace AnimeGoNet.Core.Downloads;

public enum DownloadSeedingState
{
    NotRequired,
    Waiting,
    Seeding,
    Completed,
}

public sealed record DownloadSeedingProjection(
    DownloadSeedingState State,
    long ElapsedSeconds);

public static class DownloadSeedingLifecycle
{
    public static DownloadSeedingProjection Project(
        int targetMinutes,
        DownloadTaskState downloadState,
        long reportedElapsedSeconds,
        DownloadSeedingState previousState = DownloadSeedingState.NotRequired,
        long previousElapsedSeconds = 0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(targetMinutes, -1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            targetMinutes,
            SourceDownloadPolicy.MaximumSeedingTimeMinutes);
        ArgumentOutOfRangeException.ThrowIfNegative(reportedElapsedSeconds);
        ArgumentOutOfRangeException.ThrowIfNegative(previousElapsedSeconds);

        var elapsed = Math.Max(previousElapsedSeconds, reportedElapsedSeconds);
        if (targetMinutes == 0)
        {
            return new DownloadSeedingProjection(DownloadSeedingState.NotRequired, elapsed);
        }

        if (previousState == DownloadSeedingState.Completed
            || downloadState == DownloadTaskState.Complete
            || (targetMinutes > 0 && elapsed >= checked((long)targetMinutes * 60)))
        {
            return new DownloadSeedingProjection(DownloadSeedingState.Completed, elapsed);
        }

        return new DownloadSeedingProjection(
            downloadState == DownloadTaskState.Seeding
                ? DownloadSeedingState.Seeding
                : DownloadSeedingState.Waiting,
            elapsed);
    }
}
