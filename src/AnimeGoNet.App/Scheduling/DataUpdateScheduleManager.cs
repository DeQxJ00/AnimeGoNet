using AnimeGoNet.App.Configuration;
using AnimeGoNet.Core.Configuration;

namespace AnimeGoNet.App.Scheduling;

public sealed class DataUpdateScheduleManager(
    PluginScheduleCoordinator coordinator,
    DataUpdateRuntimeState runtimeOptions,
    RuntimeConfigurationState runtime) : IDisposable
{
    public const string ScheduleName = "animegonet-data-update";
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task ApplyAsync(
        DataUpdateOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ApplyLockedAsync(options, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ApplyCurrentAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ApplyLockedAsync(runtimeOptions.Value, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ApplyLockedAsync(
        DataUpdateOptions options,
        CancellationToken cancellationToken)
    {
        var previous = runtimeOptions.Value;
        coordinator.Remove(ScheduleName);
        try
        {
            if (runtime.BackgroundWorkersEnabled && options.Enabled)
            {
                await coordinator.AddAsync(
                    Registration(options),
                    cancellationToken).ConfigureAwait(false);
            }
            runtimeOptions.Update(options);
        }
        catch
        {
            if (runtime.BackgroundWorkersEnabled && previous.Enabled)
            {
                await coordinator.AddAsync(
                    Registration(previous),
                    cancellationToken).ConfigureAwait(false);
            }
            throw;
        }
    }

    private static PluginScheduleRegistration Registration(DataUpdateOptions options) =>
        new(
            ScheduleName,
            "animegonet-data-update",
            options.Cron,
            StartRun: false);

    public void Dispose() => _gate.Dispose();
}
