using AnimeGo.Plugin.Abstractions;
using AnimeGoNet.App.Configuration;
using AnimeGoNet.App.DataUpdate;
using AnimeGoNet.App.Plugins;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Data.DataUpdate;

namespace AnimeGoNet.App.Tests.DataUpdate;

public sealed class DataUpdateSchedulePluginTests
{
    [Theory]
    [InlineData(false, false, DataUpdateActions.Check)]
    [InlineData(false, true, DataUpdateActions.Check)]
    [InlineData(true, false, DataUpdateActions.Download)]
    [InlineData(true, true, DataUpdateActions.DownloadImport)]
    public async Task MapsAutoPolicyToExplicitAction(
        bool autoDownload,
        bool autoImport,
        string expectedAction)
    {
        var service = new RecordingDataUpdateService();
        var defaults = AnimeGoDefaults.CreateNative(Path.GetTempPath());
        var options = defaults with
        {
            DataUpdate = defaults.DataUpdate with
            {
                AutoDownload = autoDownload,
                AutoImport = autoImport,
            },
        };
        var plugin = new DataUpdateSchedulePlugin(
            service,
            new DataUpdateRuntimeState(options.DataUpdate));

        var result = await plugin.ExecuteAsync(Context(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(DataUpdateTriggerKinds.Scheduled, service.TriggerKind);
        Assert.Equal(expectedAction, service.RequestedAction);
    }

    [Fact]
    public async Task ReturnsStableServiceFailureToCoordinator()
    {
        var service = new RecordingDataUpdateService
        {
            Failure = new DataUpdateServiceException(
                "data_manifest_http_failed",
                "Manifest request failed."),
        };
        var plugin = new DataUpdateSchedulePlugin(
            service,
            new DataUpdateRuntimeState(
                AnimeGoDefaults.CreateNative(Path.GetTempPath()).DataUpdate));

        var result = await plugin.ExecuteAsync(Context(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("data_manifest_http_failed", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task ReadsHotReloadedAutoPolicyOnEveryScheduledExecution()
    {
        var service = new RecordingDataUpdateService();
        var initial = AnimeGoDefaults.CreateNative(Path.GetTempPath()).DataUpdate with
        {
            AutoDownload = false,
            AutoImport = false,
        };
        var runtime = new DataUpdateRuntimeState(initial);
        var plugin = new DataUpdateSchedulePlugin(service, runtime);

        await plugin.ExecuteAsync(Context(), CancellationToken.None);
        Assert.Equal(DataUpdateActions.Check, service.RequestedAction);

        runtime.Update(initial with { AutoDownload = true, AutoImport = true });
        await plugin.ExecuteAsync(Context(), CancellationToken.None);

        Assert.Equal(DataUpdateActions.DownloadImport, service.RequestedAction);
    }

    private static ScheduledContext Context() =>
        new(
            "scheduled-test",
            new DateTimeOffset(2026, 7, 29, 4, 0, 0, TimeSpan.Zero),
            new Dictionary<string, string>(StringComparer.Ordinal));

    private sealed class RecordingDataUpdateService : IDataUpdateService
    {
        public string? TriggerKind { get; private set; }

        public string? RequestedAction { get; private set; }

        public Exception? Failure { get; init; }

        public Task<DataUpdateExecutionResult> ExecuteAsync(
            string triggerKind,
            string requestedAction,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TriggerKind = triggerKind;
            RequestedAction = requestedAction;
            if (Failure is not null)
            {
                return Task.FromException<DataUpdateExecutionResult>(Failure);
            }
            return Task.FromResult(new DataUpdateExecutionResult(
                "run",
                DataUpdateTransferStatuses.UpToDate,
                "2026.07.29.1",
                "2026.07.29.1",
                false,
                false));
        }

        public Task<DataUpdateExecutionResult> ImportDownloadedAsync(
            string dataVersion,
            string triggerKind = DataUpdateTriggerKinds.Manual,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DataUpdateExecutionResult> ImportOfflineArchiveAsync(
            Stream archive,
            long? contentLength,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
