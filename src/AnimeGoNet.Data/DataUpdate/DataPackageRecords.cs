using AnimeGoNet.Core.DataUpdate;
using AnimeGoNet.Core.Diagnostics;

namespace AnimeGoNet.Data.DataUpdate;

public sealed record DataPackageImportRequest(
    DataManifest Manifest,
    string ManifestSha256,
    string AssetDirectory,
    Version ClientVersion,
    int KeepVersions,
    DateTimeOffset UtcNow);

public sealed record DataPackageImportResult(
    string RunId,
    string DataVersion,
    bool AlreadyActive,
    long SubjectCount,
    long EpisodeCount,
    string? PreviousVersion,
    IReadOnlyList<string> PrunedVersions);

public sealed record DataPackageRollbackResult(
    string RunId,
    string ActiveVersion,
    string PreviousVersion);

public sealed record DataPackageVersionInfo(
    string DataVersion,
    string State,
    long SubjectCount,
    long EpisodeCount,
    DateTimeOffset InstalledAtUtc,
    DateTimeOffset? ActivatedAtUtc);

public sealed record DataPackageRunInfo(
    string RunId,
    string Operation,
    string? DataVersion,
    string Status,
    string? FailureCode,
    long SubjectCount,
    long EpisodeCount,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public sealed record DataPackageStatus(
    string? ActiveVersion,
    string? PreviousVersion,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<DataPackageVersionInfo> Versions,
    DataPackageRunInfo? LastRun);

public sealed class DataPackageException(string code, string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException)
{
    public string Code { get; } = StableErrorCode.Require(code, nameof(code));
}
