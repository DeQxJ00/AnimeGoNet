using AnimeGoNet.Core.Diagnostics;

namespace AnimeGoNet.App.DataUpdate;

public interface IDataUpdateService
{
    Task<DataUpdateExecutionResult> ExecuteAsync(
        string triggerKind,
        string requestedAction,
        CancellationToken cancellationToken = default);

    Task<DataUpdateExecutionResult> ImportDownloadedAsync(
        string dataVersion,
        string triggerKind = AnimeGoNet.Data.DataUpdate.DataUpdateTriggerKinds.Manual,
        CancellationToken cancellationToken = default);

    Task<DataUpdateExecutionResult> ImportOfflineArchiveAsync(
        Stream archive,
        long? contentLength,
        CancellationToken cancellationToken = default);
}

public sealed record DataUpdateExecutionResult(
    string RunId,
    string Status,
    string? DataVersion,
    string? ActiveVersion,
    bool Downloaded,
    bool Imported);

public sealed class DataUpdateServiceException(
    string code,
    string message,
    Exception? innerException = null)
    : InvalidOperationException(message, innerException)
{
    public string Code { get; } = StableErrorCode.Require(code, nameof(code));
}
