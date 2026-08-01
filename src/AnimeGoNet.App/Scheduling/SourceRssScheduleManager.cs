using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AnimeGoNet.App.Configuration;
using AnimeGoNet.Data.Sources;

namespace AnimeGoNet.App.Scheduling;

public sealed class SourceRssScheduleManager(
    PluginScheduleCoordinator coordinator,
    SourceProfileStore profiles,
    RuntimeConfigurationState runtime) : IDisposable
{
    public const string PluginId = "mikan-rss-ingest-schedule";
    private const string SchedulePrefix = "source-rss-";
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task ApplyAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var schedule in coordinator.List().Where(value =>
                         value.Name.StartsWith(SchedulePrefix, StringComparison.Ordinal)))
            {
                coordinator.Remove(schedule.Name);
            }

            await profiles.RecoverInterruptedScheduledRunsAsync(
                DateTimeOffset.UtcNow,
                cancellationToken).ConfigureAwait(false);
            if (!runtime.BackgroundWorkersEnabled) return;

            foreach (var profile in await profiles.ListScheduledAsync(cancellationToken).ConfigureAwait(false))
            {
                await coordinator.AddAsync(Registration(profile), cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ApplyAsync(
        SourceProfileAdminRecord profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            coordinator.Remove(ScheduleName(profile.Id));
            var latest = await profiles.GetAsync(
                profile.Id,
                cancellationToken).ConfigureAwait(false);
            if (latest is null) return;
            if (runtime.BackgroundWorkersEnabled
                && latest.Enabled
                && latest.RssScheduleEnabled)
            {
                await coordinator.AddAsync(Registration(latest), cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveAsync(
        string sourceProfileId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            coordinator.Remove(ScheduleName(sourceProfileId));
        }
        finally
        {
            _gate.Release();
        }
    }

    public PluginScheduleSnapshot? Get(string sourceProfileId) =>
        coordinator.Get(ScheduleName(sourceProfileId));

    internal static string ScheduleName(string sourceProfileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceProfileId);
        var normalized = sourceProfileId.Trim().ToLowerInvariant();
        var direct = SchedulePrefix + normalized;
        if (direct.Length <= 64) return direct;

        var hash = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..16];
        return $"{SchedulePrefix}{normalized[..36]}-{hash}";
    }

    private static PluginScheduleRegistration Registration(SourceProfileAdminRecord profile) =>
        new(
            ScheduleName(profile.Id),
            PluginId,
            profile.RssScheduleCron,
            StartRun: false,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["source_profile_id"] = profile.Id,
                ["source_profile_revision"] = profile.Revision.ToString(CultureInfo.InvariantCulture),
            });

    public void Dispose() => _gate.Dispose();
}
