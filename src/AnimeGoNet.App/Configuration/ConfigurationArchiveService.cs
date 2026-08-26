using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AnimeGoNet.App.Plugins;
using AnimeGoNet.App.Metadata;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Metadata;
using AnimeGoNet.Core.Media;
using AnimeGoNet.Core.Rules;
using AnimeGoNet.Data.Mikan;
using AnimeGoNet.Data.Rules;
using AnimeGoNet.Data.Sources;

namespace AnimeGoNet.App.Configuration;

public sealed record ConfigurationArchiveDocument(
    int FormatVersion,
    string Product,
    DateTimeOffset ExportedAtUtc,
    bool ContainsSecrets,
    ApplicationOverrideEntry? Application,
    IReadOnlyDictionary<string, ConfigurationArchiveDownloader> Downloaders,
    IReadOnlyDictionary<string, ConfigurationArchivePlugin> ExternalPlugins,
    IReadOnlyList<ConfigurationArchiveSource> Sources,
    IReadOnlyList<ConfigurationArchiveRssRules> RssRules,
    IReadOnlyList<ConfigurationArchiveLegacyFilter> LegacyMikanFilters,
    IReadOnlyList<ConfigurationArchiveMikanWorkRule> MikanWorkRules,
    ConfigurationBackupAutomationPolicy? BackupAutomation = null);

public sealed record ConfigurationArchiveDownloader(
    string BaseUrl,
    string? Username,
    string? Password,
    string DownloadPath,
    bool Enabled);

public sealed record ConfigurationArchivePlugin(
    bool Enabled,
    JsonElement Args,
    JsonElement Vars);

public sealed record ConfigurationArchiveSource(
    string Id,
    string DisplayName,
    string Adapter,
    string DownloaderId,
    string FileStrategy,
    IReadOnlyList<string> AllowedTorrentHosts,
    string Category,
    IReadOnlyList<string> Tags,
    int SeedingTimeMinutes,
    bool RssFilterEnabled,
    bool RssPriorityEnabled,
    bool Enabled,
    string? MikanIdentityCookie,
    string? DynamicTagTemplate,
    string? RssFeedUrl,
    bool RssScheduleEnabled,
    string RssScheduleCron,
    bool DuplicateNotificationEnabled,
    string MediaType = MediaTypes.Tv,
    bool PreferAniDbTmdbMapping = false,
    string AniDbTmdbMappingUrlTemplate = AiMatchingOptions.FixedAniDbMappingUrlTemplate);

public sealed record ConfigurationArchiveRssRules(
    string SourceProfileId,
    MikanRssRuleSet Rules);

public sealed record ConfigurationArchiveLegacyFilter(
    string SourceProfileId,
    LegacyMikanFilterConfig Config);

public sealed record ConfigurationArchiveMikanWorkRule(
    int MikanId,
    int? BangumiSubjectId,
    int? TmdbSeriesId,
    int? TmdbSeasonNumber,
    int? EpisodeOffset,
    bool Enabled);

public sealed record ConfigurationArchiveCounts(
    int Application,
    int Downloaders,
    int ExternalPlugins,
    int Sources,
    int RssRuleSets,
    int LegacyMikanFilters,
    int MikanWorkRules);

public sealed record ConfigurationArchivePreview(
    string Sha256,
    DateTimeOffset ExportedAtUtc,
    ConfigurationArchiveCounts Counts,
    IReadOnlyList<string> Warnings);

public sealed record ConfigurationArchiveBackup(
    string Id,
    string Kind,
    DateTimeOffset CreatedAtUtc,
    long SizeBytes,
    string Sha256);

public sealed record ConfigurationArchiveApplyResult(
    string BackupId,
    string Sha256,
    ConfigurationArchiveCounts Counts,
    bool RestartRequired);

public sealed class ConfigurationArchiveException(string code, string message)
    : InvalidOperationException(message)
{
    public string Code { get; } = code;
}

public sealed partial class ConfigurationArchiveService(
    DirectoryLayout layout,
    AnimeGoOptions options,
    DeploymentConfigurationLocks applicationLocks,
    DownloaderDeploymentLocks downloaderLocks,
    SourceProfileDeploymentLocks sourceLocks,
    ApplicationOverrideStore applicationOverrides,
    DownloaderOverrideStore downloaderOverrides,
    ExternalPluginConfigurationStore externalPlugins,
    SourceProfileStore sources,
    MikanRssRuleStore rssRules,
    LegacyMikanFilterStore legacyFilters,
    MikanWorkMetadataRuleStore mikanWorkRules,
    ConfigurationBackupAutomationStore backupAutomation) : IDisposable
{
    public const int MaximumArchiveBytes = 4 * 1024 * 1024;
    private const int CurrentFormatVersion = 1;
    private const string ProductName = "AnimeGoNet";
    private readonly string _backupDirectory = Path.Combine(layout.BackupsPath, "configuration-archives");
    private readonly SemaphoreSlim _gate = new(1, 1);

    public void Dispose() => _gate.Dispose();

    public async Task<byte[]> ExportAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ExportCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ConfigurationArchivePreview> PreviewAsync(
        Stream body,
        long? contentLength,
        CancellationToken cancellationToken = default)
    {
        var bytes = await ReadArchiveAsync(body, contentLength, cancellationToken).ConfigureAwait(false);
        var document = ParseAndValidate(bytes);
        return ToPreview(document, bytes);
    }

    public async Task<ConfigurationArchiveApplyResult> ImportAsync(
        Stream body,
        long? contentLength,
        string expectedSha256,
        CancellationToken cancellationToken = default)
    {
        var bytes = await ReadArchiveAsync(body, contentLength, cancellationToken).ConfigureAwait(false);
        var document = ParseAndValidate(bytes);
        var sha256 = Hash(bytes);
        if (!string.Equals(sha256, expectedSha256?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new ConfigurationArchiveException(
                "configuration_archive_changed",
                "The archive does not match the previously previewed SHA-256 digest.");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var backup = await CreateBackupCoreAsync("pre-import", cancellationToken).ConfigureAwait(false);
            await ApplyCoreAsync(document, cancellationToken).ConfigureAwait(false);
            return new ConfigurationArchiveApplyResult(
                backup.Id, sha256, Count(document), RestartRequired: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ConfigurationArchiveBackup> CreateBackupAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await CreateBackupCoreAsync("manual", cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<ConfigurationArchiveBackup>> ListBackupsAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return (await ListBackupsCoreAsync(cancellationToken).ConfigureAwait(false))
                .Take(100)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ConfigurationArchiveBackup?> CreateAutomaticBackupIfDueAsync(
        DateTimeOffset nowUtc,
        TimeZoneInfo timeZone,
        int retentionCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        ConfigurationBackupAutomationPolicy.Validate(new(true, retentionCount));
        nowUtc = nowUtc.ToUniversalTime();
        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(nowUtc, timeZone).DateTime);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var backups = (await ListBackupsCoreAsync(cancellationToken).ConfigureAwait(false))
                .Where(item => item.Kind == "automatic")
                .ToList();
            ConfigurationArchiveBackup? created = null;
            if (!backups.Any(item => DateOnly.FromDateTime(
                    TimeZoneInfo.ConvertTime(item.CreatedAtUtc, timeZone).DateTime) == localDate))
            {
                created = await CreateBackupCoreAsync(
                    "automatic",
                    cancellationToken,
                    nowUtc).ConfigureAwait(false);
                backups.Add(created);
            }

            foreach (var expired in backups
                         .OrderByDescending(item => item.CreatedAtUtc)
                         .Skip(retentionCount))
            {
                File.Delete(BackupPath(expired.Id));
            }
            return created;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<byte[]> ReadBackupAsync(
        string backupId,
        CancellationToken cancellationToken = default)
    {
        var path = BackupPath(backupId);
        if (!File.Exists(path)) throw new KeyNotFoundException("Configuration backup was not found.");
        return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ConfigurationArchiveApplyResult> RestoreAsync(
        string backupId,
        CancellationToken cancellationToken = default)
    {
        var path = BackupPath(backupId);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(path)) throw new KeyNotFoundException("Configuration backup was not found.");
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            var document = ParseAndValidate(bytes);
            var safety = await CreateBackupCoreAsync("pre-restore", cancellationToken).ConfigureAwait(false);
            await ApplyCoreAsync(document, cancellationToken).ConfigureAwait(false);
            return new ConfigurationArchiveApplyResult(
                safety.Id, Hash(bytes), Count(document), RestartRequired: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task DeleteBackupAsync(string backupId)
    {
        var path = BackupPath(backupId);
        if (!File.Exists(path)) throw new KeyNotFoundException("Configuration backup was not found.");
        File.Delete(path);
        return Task.CompletedTask;
    }

    private async Task<byte[]> ExportCoreAsync(CancellationToken cancellationToken)
    {
        var application = await applicationOverrides.LoadAsync(cancellationToken).ConfigureAwait(false);
        var downloaders = await downloaderOverrides.LoadAsync(cancellationToken).ConfigureAwait(false);
        var profiles = await sources.ListAsync(cancellationToken).ConfigureAwait(false);
        var rss = new List<ConfigurationArchiveRssRules>();
        var legacy = new List<ConfigurationArchiveLegacyFilter>();
        foreach (var profile in profiles.Where(item => item.Adapter == "mikan"))
        {
            var rule = await rssRules.GetAsync(profile.Id, cancellationToken).ConfigureAwait(false);
            if (rule is not null) rss.Add(new(profile.Id, rule.Rules));
            var filter = await legacyFilters.GetAsync(profile.Id, cancellationToken).ConfigureAwait(false);
            if (filter is not null) legacy.Add(new(profile.Id, filter.Config));
        }
        var workRules = await mikanWorkRules.ListAsync(cancellationToken).ConfigureAwait(false);
        var automation = await backupAutomation.LoadAsync(cancellationToken).ConfigureAwait(false);
        var document = new ConfigurationArchiveDocument(
            CurrentFormatVersion,
            ProductName,
            DateTimeOffset.UtcNow,
            ContainsSecrets: true,
            application.Settings,
            downloaders.Downloaders.ToDictionary(
                item => item.Key,
                item => new ConfigurationArchiveDownloader(
                    item.Value.BaseUrl, item.Value.Username, item.Value.Password,
                    item.Value.DownloadPath, item.Value.Enabled),
                StringComparer.OrdinalIgnoreCase),
            externalPlugins.Current.Plugins.ToDictionary(
                item => item.Key,
                item => new ConfigurationArchivePlugin(
                    item.Value.Enabled, item.Value.Args.Clone(), item.Value.Vars.Clone()),
                StringComparer.Ordinal),
            profiles.Select(ToArchiveSource).ToArray(),
            rss,
            legacy,
            workRules.Select(item => new ConfigurationArchiveMikanWorkRule(
                item.MikanId, item.BangumiSubjectId, item.TmdbSeriesId,
                item.TmdbSeasonNumber, item.EpisodeOffset, item.Enabled)).ToArray(),
            automation);
        return JsonSerializer.SerializeToUtf8Bytes(
            document, ConfigurationArchiveJsonContext.Default.ConfigurationArchiveDocument);
    }

    private async Task<ConfigurationArchiveBackup> CreateBackupCoreAsync(
        string kind,
        CancellationToken cancellationToken,
        DateTimeOffset? createdAtUtc = null)
    {
        var bytes = await ExportCoreAsync(cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(_backupDirectory);
        var now = (createdAtUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var id = $"{kind}-{now:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}";
        var path = Path.Combine(_backupDirectory, id + ".json");
        await WritePrivateFileAsync(path, bytes, cancellationToken).ConfigureAwait(false);
        return new ConfigurationArchiveBackup(
            id, kind, now, bytes.LongLength, Hash(bytes));
    }

    private async Task ApplyCoreAsync(
        ConfigurationArchiveDocument document,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var appCurrent = await applicationOverrides.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (document.Application is null)
            await applicationOverrides.DeleteAsync(appCurrent.Revision, cancellationToken).ConfigureAwait(false);
        else
        {
            var importedApplication = applicationLocks.PreserveLockedOverrides(
                appCurrent.Settings,
                document.Application with { UpdatedAtUtc = now });
            await applicationOverrides.SaveAsync(
                importedApplication,
                appCurrent.Revision,
                cancellationToken).ConfigureAwait(false);
        }

        var downloaderCurrent = await downloaderOverrides.LoadAsync(cancellationToken).ConfigureAwait(false);
        foreach (var (id, entry) in document.Downloaders)
        {
            downloaderCurrent.Downloaders.TryGetValue(id, out var currentOverride);
            options.Downloaders.TryGetValue(id, out var currentRuntime);
            var stored = new ConfigurationArchiveDownloader(
                downloaderLocks.IsLocked(id, "base_url")
                    ? currentOverride?.BaseUrl ?? currentRuntime?.BaseUrl.AbsoluteUri ?? entry.BaseUrl
                    : entry.BaseUrl,
                downloaderLocks.IsLocked(id, "username") ? currentOverride?.Username : entry.Username,
                downloaderLocks.IsLocked(id, "password") ? currentOverride?.Password : entry.Password,
                downloaderLocks.IsLocked(id, "download_path")
                    ? currentOverride?.DownloadPath ?? currentRuntime?.DownloadPath ?? entry.DownloadPath
                    : entry.DownloadPath,
                downloaderLocks.IsLocked(id, "enabled")
                    ? currentOverride?.Enabled ?? currentRuntime?.Enabled ?? entry.Enabled
                    : entry.Enabled);
            downloaderCurrent = await downloaderOverrides.UpsertAsync(
                id,
                new DownloaderOverrideEntry(
                    stored.BaseUrl, stored.Username, stored.Password, stored.DownloadPath,
                    stored.Enabled, 0, now),
                downloaderCurrent.Revision,
                cancellationToken).ConfigureAwait(false);
        }

        var pluginCurrent = await externalPlugins.LoadAsync(cancellationToken).ConfigureAwait(false);
        foreach (var (id, entry) in document.ExternalPlugins)
        {
            pluginCurrent = await externalPlugins.UpsertAsync(
                id, entry.Enabled, entry.Args, entry.Vars,
                pluginCurrent.Revision, now, cancellationToken).ConfigureAwait(false);
        }

        foreach (var entry in document.Sources)
        {
            var existing = await sources.GetAsync(entry.Id, cancellationToken).ConfigureAwait(false);
            var definition = ToDefinition(entry) with
            {
                Category = sourceLocks.IsLocked(entry.Id, "category")
                    ? existing?.Category ?? entry.Category
                    : entry.Category,
                DynamicTagTemplate = sourceLocks.IsLocked(entry.Id, "dynamic_tag_template")
                    ? existing?.DynamicTagTemplate
                    : entry.DynamicTagTemplate,
                MikanIdentityCookie = sourceLocks.IsLocked(entry.Id, "mikan_identity_cookie")
                    ? existing?.MikanIdentityCookie
                    : entry.MikanIdentityCookie,
            };
            if (existing is null)
                await sources.CreateAsync(entry.Id, definition, now, cancellationToken).ConfigureAwait(false);
            else
                await sources.UpdateAsync(
                    entry.Id, definition, existing.Revision, now, cancellationToken).ConfigureAwait(false);
        }

        foreach (var entry in document.RssRules)
        {
            var current = await rssRules.GetAsync(entry.SourceProfileId, cancellationToken).ConfigureAwait(false);
            await rssRules.SaveAsync(
                entry.SourceProfileId, entry.Rules, current?.Revision ?? 0, now, cancellationToken)
                .ConfigureAwait(false);
        }
        foreach (var entry in document.LegacyMikanFilters)
        {
            await legacyFilters.EnsureDefaultAsync(entry.SourceProfileId, now, cancellationToken).ConfigureAwait(false);
            var current = await legacyFilters.GetAsync(entry.SourceProfileId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Legacy Mikan filter was not initialized.");
            await legacyFilters.SaveAsync(
                entry.SourceProfileId, entry.Config, current.Revision,
                "web", now, cancellationToken).ConfigureAwait(false);
        }
        foreach (var entry in document.MikanWorkRules)
        {
            var current = await mikanWorkRules.GetAsync(entry.MikanId, cancellationToken).ConfigureAwait(false);
            await mikanWorkRules.SaveAsync(
                new MikanWorkMetadataRuleUpdate(
                    entry.MikanId, entry.BangumiSubjectId, entry.TmdbSeriesId,
                    entry.TmdbSeasonNumber, entry.EpisodeOffset, entry.Enabled),
                current?.Revision ?? 0, now, cancellationToken).ConfigureAwait(false);
        }
        if (document.BackupAutomation is not null)
        {
            await backupAutomation.SaveAsync(
                document.BackupAutomation,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private ConfigurationArchiveDocument ParseAndValidate(byte[] bytes)
    {
        ConfigurationArchiveDocument document;
        try
        {
            document = JsonSerializer.Deserialize(
                bytes, ConfigurationArchiveJsonContext.Default.ConfigurationArchiveDocument)
                ?? throw new ConfigurationArchiveException(
                    "configuration_archive_empty", "The configuration archive is empty.");
        }
        catch (JsonException exception)
        {
            throw new ConfigurationArchiveException(
                "configuration_archive_json_invalid",
                $"The configuration archive is not valid JSON: {exception.Message}");
        }
        if (document.FormatVersion != CurrentFormatVersion
            || !string.Equals(document.Product, ProductName, StringComparison.Ordinal)
            || !document.ContainsSecrets)
        {
            throw new ConfigurationArchiveException(
                "configuration_archive_format_unsupported",
                "The configuration archive format or product identifier is unsupported.");
        }
        if (document.Downloaders is null || document.ExternalPlugins is null
            || document.Sources is null || document.RssRules is null
            || document.LegacyMikanFilters is null || document.MikanWorkRules is null)
        {
            throw new ConfigurationArchiveException(
                "configuration_archive_shape_invalid", "The configuration archive is incomplete.");
        }
        if (document.BackupAutomation is not null)
        {
            try
            {
                ConfigurationBackupAutomationPolicy.Validate(document.BackupAutomation);
            }
            catch (ArgumentOutOfRangeException exception)
            {
                throw new ConfigurationArchiveException(
                    "configuration_archive_backup_automation_invalid",
                    exception.Message);
            }
        }
        if (document.Application is not null)
        {
            var candidate = applicationLocks.Reapply(options, ApplicationOverrideStore.Apply(
                options,
                new ApplicationOverrideSnapshot(1, 0, document.Application)));
            var failures = AnimeGoOptionsValidator.Validate(candidate);
            if (failures.Count > 0)
                throw new ConfigurationArchiveException("configuration_archive_application_invalid", failures[0]);
            if (candidate.Metadata.Ai.PromptTemplate is not null)
            {
                try
                {
                    AiMetadataPromptRenderer.ValidateTemplate(candidate.Metadata.Ai.PromptTemplate);
                }
                catch (AiMetadataMatcherException exception)
                {
                    throw new ConfigurationArchiveException(
                        "configuration_archive_application_invalid",
                        $"The AI prompt template is invalid ({exception.SafeCode}).");
                }
            }
        }
        foreach (var (id, downloader) in document.Downloaders)
        {
            if (!SourceIdPattern().IsMatch(id)
                || !Uri.TryCreate(downloader.BaseUrl, UriKind.Absolute, out var baseUrl)
                || baseUrl.Scheme is not ("http" or "https")
                || !string.IsNullOrEmpty(baseUrl.UserInfo)
                || !PathBoundary.IsAbsolute(downloader.DownloadPath)
                || downloader.Username?.Length > 1024
                || downloader.Password?.Length > 1024)
            {
                throw new ConfigurationArchiveException(
                    "configuration_archive_downloader_invalid",
                    $"Downloader '{id}' is invalid or its download path is not absolute.");
            }
        }
        foreach (var (id, plugin) in document.ExternalPlugins)
        {
            if (!SourceIdPattern().IsMatch(id)
                || plugin.Args.ValueKind != JsonValueKind.Object
                || plugin.Vars.ValueKind != JsonValueKind.Object)
            {
                throw new ConfigurationArchiveException(
                    "configuration_archive_plugin_invalid",
                    $"External plugin configuration '{id}' is invalid.");
            }
        }
        var sourceIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in document.Sources)
        {
            if (!SourceIdPattern().IsMatch(source.Id)
                || string.IsNullOrWhiteSpace(source.Adapter)
                || !SourceIdPattern().IsMatch(source.DownloaderId)
                || !sourceIds.Add(source.Id))
                throw new ConfigurationArchiveException(
                    "configuration_archive_source_invalid", "A source profile identifier is invalid or duplicated.");
        }
        var rssSourceIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in document.RssRules)
        {
            if (!sourceIds.Contains(rule.SourceProfileId)
                || !rssSourceIds.Add(rule.SourceProfileId))
                throw new ConfigurationArchiveException(
                    "configuration_archive_reference_invalid", "An RSS rule set references a source absent from the archive.");
            _ = MikanRssRuleSetNormalizer.Normalize(rule.Rules);
        }
        var legacySourceIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var filter in document.LegacyMikanFilters)
        {
            if (!sourceIds.Contains(filter.SourceProfileId)
                || !legacySourceIds.Add(filter.SourceProfileId)
                || filter.Config is null
                || filter.Config.Filiter0 is null
                || filter.Config.Filiter1 is null
                || filter.Config.Filiter2 is null
                || filter.Config.Filiter3 is null
                || filter.Config.Filiter4 is null)
                throw new ConfigurationArchiveException(
                    "configuration_archive_reference_invalid", "A legacy Mikan filter references a source absent from the archive.");
        }
        var mikanIds = new HashSet<int>();
        foreach (var rule in document.MikanWorkRules)
        {
            if (rule.MikanId < 1
                || !mikanIds.Add(rule.MikanId)
                || rule.BangumiSubjectId is <= 0
                || rule.TmdbSeriesId is <= 0
                || rule.TmdbSeasonNumber is <= 0
                || (rule.BangumiSubjectId is null
                    && rule.TmdbSeriesId is null
                    && rule.EpisodeOffset is null)
                || (rule.TmdbSeasonNumber is not null && rule.TmdbSeriesId is null)
                || (rule.EpisodeOffset is not null
                    && (rule.TmdbSeriesId is null || rule.TmdbSeasonNumber is null)))
            {
                throw new ConfigurationArchiveException(
                    "configuration_archive_mikan_work_rule_invalid",
                    "A Mikan work metadata rule is invalid or duplicated.");
            }
        }
        return document;
    }

    private ConfigurationArchivePreview ToPreview(
        ConfigurationArchiveDocument document,
        byte[] bytes)
    {
        var warnings = new List<string>
        {
            "归档包含密码、Cookie、API Key 和插件私有变量，请按敏感文件保管。",
            "导入采用同 ID 覆盖、包外项目保留的安全合并方式。",
            "应用、下载器、插件主机和 RSS 调度相关变更需重启后完整生效。",
        };
        if (applicationLocks.Items.Count + downloaderLocks.Items.Count + sourceLocks.Items.Count > 0)
        {
            warnings.Add("目标实例存在部署锁；被环境变量或命令行控制的字段将保留目标机现值。");
        }
        return new ConfigurationArchivePreview(
            Hash(bytes), document.ExportedAtUtc, Count(document), warnings);
    }

    private static ConfigurationArchiveCounts Count(ConfigurationArchiveDocument document) =>
        new(
            document.Application is null ? 0 : 1,
            document.Downloaders.Count,
            document.ExternalPlugins.Count,
            document.Sources.Count,
            document.RssRules.Count,
            document.LegacyMikanFilters.Count,
            document.MikanWorkRules.Count);

    private static ConfigurationArchiveSource ToArchiveSource(SourceProfileAdminRecord item) =>
        new(
            item.Id, item.DisplayName, item.Adapter, item.DownloaderId, item.FileStrategy,
            item.AllowedTorrentHosts, item.Category, item.Tags, item.SeedingTimeMinutes,
            item.RssFilterEnabled, item.RssPriorityEnabled, item.Enabled,
            item.MikanIdentityCookie, item.DynamicTagTemplate, item.RssFeedUrl,
            item.RssScheduleEnabled, item.RssScheduleCron,
            item.DuplicateNotificationEnabled, item.MediaType,
            item.PreferAniDbTmdbMapping, item.AniDbTmdbMappingUrlTemplate);

    private static SourceProfileDefinition ToDefinition(ConfigurationArchiveSource item) =>
        new(
            item.DisplayName, item.Adapter, item.DownloaderId, item.FileStrategy,
            item.AllowedTorrentHosts, item.Category, item.Tags, item.SeedingTimeMinutes,
            item.RssFilterEnabled, item.RssPriorityEnabled, item.Enabled,
            item.MikanIdentityCookie, item.DynamicTagTemplate, item.RssFeedUrl,
            item.RssScheduleEnabled, item.RssScheduleCron,
            item.DuplicateNotificationEnabled, item.MediaType,
            item.PreferAniDbTmdbMapping, item.AniDbTmdbMappingUrlTemplate);

    private string BackupPath(string backupId)
    {
        if (string.IsNullOrWhiteSpace(backupId) || !BackupIdPattern().IsMatch(backupId))
            throw new ConfigurationArchiveException(
                "configuration_backup_id_invalid", "The configuration backup identifier is invalid.");
        return Path.Combine(_backupDirectory, backupId + ".json");
    }

    private static async Task<byte[]> ReadArchiveAsync(
        Stream body,
        long? contentLength,
        CancellationToken cancellationToken)
    {
        if (contentLength is <= 0 or > MaximumArchiveBytes)
            throw new ConfigurationArchiveException(
                "configuration_archive_size_invalid", "The configuration archive size is invalid.");
        await using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        while (true)
        {
            var read = await body.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            if (buffer.Length + read > MaximumArchiveBytes)
                throw new ConfigurationArchiveException(
                    "configuration_archive_size_invalid", "The configuration archive exceeds 4 MiB.");
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
        if (buffer.Length == 0)
            throw new ConfigurationArchiveException(
                "configuration_archive_size_invalid", "The configuration archive is empty.");
        return buffer.ToArray();
    }

    private static async Task WritePrivateFileAsync(
        string path,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken).ConfigureAwait(false);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(temporary, path, overwrite: false);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static string Hash(byte[] bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));

    private async Task<IReadOnlyList<ConfigurationArchiveBackup>> ListBackupsCoreAsync(
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_backupDirectory);
        var result = new List<ConfigurationArchiveBackup>();
        foreach (var path in Directory.EnumerateFiles(_backupDirectory, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileNameWithoutExtension(path);
            if (!BackupIdPattern().IsMatch(name)) continue;
            var info = new FileInfo(path);
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            result.Add(new ConfigurationArchiveBackup(
                name,
                BackupKind(name),
                BackupCreatedAtUtc(name, info.CreationTimeUtc),
                info.Length,
                Hash(bytes)));
        }
        return result.OrderByDescending(item => item.CreatedAtUtc).ToArray();
    }

    private static string BackupKind(string backupId) =>
        backupId.StartsWith("manual-", StringComparison.Ordinal) ? "manual"
            : backupId.StartsWith("automatic-", StringComparison.Ordinal) ? "automatic"
            : backupId.StartsWith("pre-restore-", StringComparison.Ordinal) ? "pre-restore"
            : "pre-import";

    private static DateTimeOffset BackupCreatedAtUtc(string backupId, DateTime fallbackUtc)
    {
        var kind = BackupKind(backupId);
        var timestamp = backupId.AsSpan(kind.Length + 1, 19);
        return DateTimeOffset.TryParseExact(
            timestamp,
            "yyyyMMdd'T'HHmmssfff'Z'",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var value)
            ? value
            : new DateTimeOffset(DateTime.SpecifyKind(fallbackUtc, DateTimeKind.Utc));
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex SourceIdPattern();

    [GeneratedRegex("^(manual|automatic|pre-import|pre-restore)-[0-9]{8}T[0-9]{9}Z-[a-f0-9]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex BackupIdPattern();
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    WriteIndented = true,
    GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(ConfigurationArchiveDocument))]
[JsonSerializable(typeof(ConfigurationArchivePreview))]
[JsonSerializable(typeof(ConfigurationArchiveApplyResult))]
[JsonSerializable(typeof(ConfigurationArchiveBackup))]
[JsonSerializable(typeof(IReadOnlyList<ConfigurationArchiveBackup>))]
internal sealed partial class ConfigurationArchiveJsonContext : JsonSerializerContext;
