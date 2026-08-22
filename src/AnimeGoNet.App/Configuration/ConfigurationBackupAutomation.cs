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
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ConfigurationBackupAutomationStore(DirectoryLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        _path = Path.Combine(layout.ConfigurationPath, "configuration-backup-automation.json");
    }

    public void Dispose() => _gate.Dispose();

    public async Task<ConfigurationBackupAutomationPolicy> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ConfigurationBackupAutomationPolicy> SaveAsync(
        ConfigurationBackupAutomationPolicy policy,
        CancellationToken cancellationToken = default)
    {
        ConfigurationBackupAutomationPolicy.Validate(policy);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = Path.Combine(
                Path.GetDirectoryName(_path)!,
                $".configuration-backup-automation.{Guid.NewGuid():N}.tmp");
            try
            {
                await using (var stream = new FileStream(
                    temporary,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await JsonSerializer.SerializeAsync(
                        stream,
                        policy,
                        ConfigurationBackupAutomationJsonContext.Default.ConfigurationBackupAutomationPolicy,
                        cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(
                        temporary,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }

                File.Move(temporary, _path, overwrite: true);
            }
            finally
            {
                File.Delete(temporary);
            }

            return policy;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ConfigurationBackupAutomationPolicy> LoadCoreAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_path)) return ConfigurationBackupAutomationPolicy.Default;
        await using var stream = new FileStream(
            _path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var policy = await JsonSerializer.DeserializeAsync(
            stream,
            ConfigurationBackupAutomationJsonContext.Default.ConfigurationBackupAutomationPolicy,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Automatic configuration backup policy is empty.");
        ConfigurationBackupAutomationPolicy.Validate(policy);
        return policy;
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
