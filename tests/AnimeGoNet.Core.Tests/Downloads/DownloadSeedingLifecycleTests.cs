using AnimeGoNet.Core.Downloads;

namespace AnimeGoNet.Core.Tests.Downloads;

public sealed class DownloadSeedingLifecycleTests
{
    [Fact]
    public void ZeroTargetNeverBlocksOrganization()
    {
        var result = DownloadSeedingLifecycle.Project(
            0,
            DownloadTaskState.Seeding,
            90,
            DownloadSeedingState.Waiting,
            120);

        Assert.Equal(DownloadSeedingState.NotRequired, result.State);
        Assert.Equal(120, result.ElapsedSeconds);
    }

    [Fact]
    public void PositiveTargetTracksWaitingSeedingAndElapsedCompletion()
    {
        var waiting = DownloadSeedingLifecycle.Project(
            30,
            DownloadTaskState.Downloading,
            0);
        var seeding = DownloadSeedingLifecycle.Project(
            30,
            DownloadTaskState.Seeding,
            600,
            waiting.State,
            waiting.ElapsedSeconds);
        var completed = DownloadSeedingLifecycle.Project(
            30,
            DownloadTaskState.Seeding,
            1_800,
            seeding.State,
            seeding.ElapsedSeconds);

        Assert.Equal(DownloadSeedingState.Waiting, waiting.State);
        Assert.Equal(DownloadSeedingState.Seeding, seeding.State);
        Assert.Equal(DownloadSeedingState.Completed, completed.State);
        Assert.Equal(1_800, completed.ElapsedSeconds);
    }

    [Fact]
    public void CompletedStateAndElapsedTimeNeverRegress()
    {
        var result = DownloadSeedingLifecycle.Project(
            30,
            DownloadTaskState.Downloading,
            60,
            DownloadSeedingState.Completed,
            1_800);

        Assert.Equal(DownloadSeedingState.Completed, result.State);
        Assert.Equal(1_800, result.ElapsedSeconds);
    }

    [Fact]
    public void DownloaderPauseDoesNotCompleteInfiniteSeedingTarget()
    {
        var stillSeeding = DownloadSeedingLifecycle.Project(
            -1,
            DownloadTaskState.Seeding,
            50_000);
        var paused = DownloadSeedingLifecycle.Project(
            -1,
            DownloadTaskState.Complete,
            50_001,
            stillSeeding.State,
            stillSeeding.ElapsedSeconds);

        Assert.Equal(DownloadSeedingState.Seeding, stillSeeding.State);
        Assert.Equal(DownloadSeedingState.Waiting, paused.State);
        Assert.Equal(50_001, paused.ElapsedSeconds);
    }

    [Fact]
    public void DownloaderPauseDoesNotCompleteFiniteTargetBeforeElapsedBoundary()
    {
        var paused = DownloadSeedingLifecycle.Project(
            30,
            DownloadTaskState.Complete,
            1_799,
            DownloadSeedingState.Seeding,
            1_798);
        var completed = DownloadSeedingLifecycle.Project(
            30,
            DownloadTaskState.Complete,
            1_800,
            paused.State,
            paused.ElapsedSeconds);

        Assert.Equal(DownloadSeedingState.Waiting, paused.State);
        Assert.Equal(DownloadSeedingState.Completed, completed.State);
    }

    [Theory]
    [InlineData(-2, 0, 0)]
    [InlineData(5_256_001, 0, 0)]
    [InlineData(1, -1, 0)]
    [InlineData(1, 0, -1)]
    public void RejectsInvalidPersistedOrReportedValues(
        int targetMinutes,
        long reportedElapsedSeconds,
        long previousElapsedSeconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DownloadSeedingLifecycle.Project(
                targetMinutes,
                DownloadTaskState.Seeding,
                reportedElapsedSeconds,
                previousElapsedSeconds: previousElapsedSeconds));
    }
}
