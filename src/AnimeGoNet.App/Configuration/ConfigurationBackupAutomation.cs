using System.Text.Json;
using System.Text.Json.Serialization;
using AnimeGoNet.Core.Configuration;

namespace AnimeGoNet.App.Configuration;

public sealed record ConfigurationBackupAutomationPolicy(
    bool Enabled,
    int RetentionCount)
{
    public const int DefaultRetentionCount = 10;
    public const int MaximumRetentionCount = 100;

    public static ConfigurationBackupAutomationPolicy Default { get; } =
        new(Enabled: false, DefaultRetentionCount);

    public static void Validate(ConfigurationBackupAutomationPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (policy.RetentionCount is < 1 or > MaximumRetentionCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(policy),
                $"Automatic configuration backup retention must be between 1 and {MaximumRetentionCount}.");
        }
    }
}

public sealed class ConfigurationBackupAutomationStore : IDisposable
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate;

    public ConfigurationBackupAutomationStore(DirectoryLayout layout, string? yamlFilePath = null)
    {
        ArgumentNullException.ThrowIfNull(layout);
        _path = Path.GetFullPath(yamlFilePath ?? Path.Combine(layout.DataPath, "animego.yaml"));
        _gate = DeploymentFileGate.ForPath(_path);
    }

    public void Dispose() { }

    public async Task<ConfigurationBackupAutomationPolicy> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var root = await ConfigurationYamlDocument.ReadAsync(_path, cancellationToken).ConfigureAwait(false);
            var enabledText = (ConfigurationYamlDocument.Get(root, "configuration_backup:enabled") as YamlDotNet.RepresentationModel.YamlScalarNode)?.Value;
            var retentionText = (ConfigurationYamlDocument.Get(root, "configuration_backup:retention_count") as YamlDotNet.RepresentationModel.YamlScalarNode)?.Value;
            var enabled = enabledText is null ? false : bool.Parse(enabledText);
            var retention = retentionText is null ? ConfigurationBackupAutomationPolicy.DefaultRetentionCount
                : int.Parse(retentionText, System.Globalization.CultureInfo.InvariantCulture);
            var policy = new ConfigurationBackupAutomationPolicy(enabled, retention);
            ConfigurationBackupAutomationPolicy.Validate(policy);
            return policy;
        }
        finally { _gate.Release(); }
    }

    public async Task<ConfigurationBackupAutomationPolicy> SaveAsync(
        ConfigurationBackupAutomationPolicy policy, CancellationToken cancellationToken = default)
    {
        ConfigurationBackupAutomationPolicy.Validate(policy);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var root = await ConfigurationYamlDocument.ReadAsync(_path, cancellationToken).ConfigureAwait(false);
            ConfigurationYamlDocument.Set(root, "configuration_backup:enabled",
                ConfigurationYamlDocument.Scalar(policy.Enabled ? "true" : "false"));
            ConfigurationYamlDocument.Set(root, "configuration_backup:retention_count",
                ConfigurationYamlDocument.Scalar(policy.RetentionCount.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            await ConfigurationYamlDocument.SaveAsync(_path, root, "backup-policy", cancellationToken).ConfigureAwait(false);
            return policy;
        }
        finally { _gate.Release(); }
    }
}

public sealed partial class ConfigurationBackupAutomationRunner(
    ConfigurationBackupAutomationStore store,
    ConfigurationArchiveService archives,
    ILogger<ConfigurationBackupAutomationRunner> logger)
{
    public async Task<ConfigurationArchiveBackup?> RunOnceAsync(
        CancellationToken cancellationToken = default)
    {
        var policy = await store.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (!policy.Enabled) return null;

        var created = await archives.CreateAutomaticBackupIfDueAsync(
            DateTimeOffset.UtcNow,
            TimeZoneInfo.Local,
            policy.RetentionCount,
            cancellationToken).ConfigureAwait(false);
        if (created is not null)
        {
            LogCreated(logger, created.Id, policy.RetentionCount);
        }
        return created;
    }

    [LoggerMessage(
        EventId = 7410,
        Level = LogLevel.Information,
        Message = "Created daily configuration backup {BackupId}; retaining {RetentionCount} automatic backups.")]
    private static partial void LogCreated(
        ILogger logger,
        string backupId,
        int retentionCount);
}

public sealed partial class ConfigurationBackupAutomationWorker(
    ConfigurationBackupAutomationRunner runner,
    ILogger<ConfigurationBackupAutomationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await runner.RunOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                LogFailure(logger, exception);
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken).ConfigureAwait(false);
        }
    }

    [LoggerMessage(
        EventId = 7411,
        Level = LogLevel.Error,
        Message = "Daily configuration backup check failed.")]
    private static partial void LogFailure(ILogger logger, Exception exception);
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    WriteIndented = true,
    GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(ConfigurationBackupAutomationPolicy))]
internal sealed partial class ConfigurationBackupAutomationJsonContext : JsonSerializerContext;
