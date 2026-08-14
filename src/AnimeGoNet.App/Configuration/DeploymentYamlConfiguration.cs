using System.Globalization;
using System.Text;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Sources;
using Microsoft.Extensions.Configuration;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace AnimeGoNet.App.Configuration;

internal sealed record DeploymentYamlSnapshot(
    string FilePath,
    string Version,
    bool Created,
    bool LegacyLayout,
    bool Upgraded,
    string? BackupFilePath,
    IReadOnlyDictionary<string, string?> Values);

internal static class DeploymentYamlConfiguration
{
    internal const string CurrentVersion = "1.7.1";
    private static readonly string[] SupportedVersions =
    [
        "1.1.0",
        "1.2.0",
        "1.3.0",
        "1.4.0",
        "1.4.1",
        "1.5.0",
        "1.5.1",
        "1.5.2",
        "1.6.0",
        "1.6.1",
        "1.6.2",
        "1.7.0",
        CurrentVersion,
    ];
    private const int MaximumBytes = 1024 * 1024;
    private const int MaximumDepth = 32;
    private const int MaximumNodes = 4096;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static string ResolvePath(
        IConfiguration configuration,
        AnimeGoOptions defaults)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(defaults);
        var explicitPath = ConfigurationAliasResolver.FirstNonEmpty(
            configuration,
            "ANIMEGO_CONFIG",
            "config");
        if (explicitPath is not null)
        {
            return ResolveAgainstApplication(explicitPath);
        }

        var dataPath = ConfigurationAliasResolver.FirstNonEmpty(
            configuration,
            "ANIMEGO_DATA_PATH",
            "data_path")
            ?? defaults.Paths.DataPath;
        return Path.Combine(
            ResolveAgainstApplication(dataPath),
            "animego.yaml");
    }

    public static async Task<DeploymentYamlSnapshot> LoadOrCreateAsync(
        string filePath,
        AnimeGoOptions defaults,
        bool backupLegacy = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(defaults);
        var absolutePath = ResolveAgainstApplication(filePath);
        var created = false;
        if (!File.Exists(absolutePath))
        {
            var parent = Path.GetDirectoryName(absolutePath)
                ?? throw new DeploymentYamlException("Deployment YAML path has no parent directory.");
            Directory.CreateDirectory(parent);
            try
            {
                await WriteNewAsync(
                    absolutePath,
                    RenderDefault(defaults),
                    cancellationToken).ConfigureAwait(false);
                created = true;
            }
            catch (IOException) when (File.Exists(absolutePath))
            {
                // A concurrent first-start won the CreateNew race; read its complete file.
            }
        }

        byte[] bytes;
        try
        {
            var info = new FileInfo(absolutePath);
            if (!info.Exists)
            {
                throw new DeploymentYamlException("Deployment YAML does not exist after initialization.");
            }
            if (info.Length is <= 0 or > MaximumBytes)
            {
                throw new DeploymentYamlException(
                    $"Deployment YAML must contain 1 to {MaximumBytes} bytes.");
            }

            bytes = await File.ReadAllBytesAsync(absolutePath, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (DeploymentYamlException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            throw new DeploymentYamlException(
                "Deployment YAML could not be read.",
                exception);
        }

        string text;
        try
        {
            text = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new DeploymentYamlException(
                "Deployment YAML must use valid UTF-8.",
                exception);
        }

        var values = Parse(text);
        var version = Required(values, "version");
        ValidateVersion(version);
        var legacy = values.Keys.Any(key =>
            key.StartsWith("setting:", StringComparison.Ordinal));
        string? backupFilePath = null;
        var upgraded = false;
        if (legacy)
        {
            AddLegacyAliases(values);
            var downloaderType =
                LegacyDownloaderMigrationDetector.ReadDownloaderType(text);
            if (downloaderType is null
                || string.Equals(
                    downloaderType,
                    DownloaderTypes.Qbittorrent,
                    StringComparison.OrdinalIgnoreCase))
            {
                var upgradedText = RenderLegacyUpgrade(defaults, values);
                if (backupLegacy)
                {
                    backupFilePath = await WriteBackupAsync(
                        absolutePath,
                        version,
                        bytes,
                        cancellationToken).ConfigureAwait(false);
                }

                await ReplaceAtomicallyAsync(
                    absolutePath,
                    upgradedText,
                    cancellationToken).ConfigureAwait(false);
                values = Parse(upgradedText);
                version = CurrentVersion;
                legacy = false;
                upgraded = true;
            }
        }

        return new DeploymentYamlSnapshot(
            absolutePath,
            version,
            created,
            legacy,
            upgraded,
            backupFilePath,
            values);
    }

    private static Dictionary<string, string?> Parse(string text)
    {
        var stream = new YamlStream();
        try
        {
            using var reader = new StringReader(text);
            stream.Load(reader);
        }
        catch (Exception exception) when (
            exception is YamlException or ArgumentException or InvalidOperationException)
        {
            throw new DeploymentYamlException(
                "Deployment YAML syntax is invalid.",
                exception);
        }

        if (stream.Documents.Count != 1
            || stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            throw new DeploymentYamlException(
                "Deployment YAML must contain exactly one mapping document.");
        }

        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var nodeCount = 0;
        Flatten(root, string.Empty, 0, values, ref nodeCount);
        return values;
    }

    private static void Flatten(
        YamlNode node,
        string prefix,
        int depth,
        Dictionary<string, string?> values,
        ref int nodeCount)
    {
        nodeCount++;
        if (depth > MaximumDepth || nodeCount > MaximumNodes)
        {
            throw new DeploymentYamlException(
                "Deployment YAML is too deeply nested or contains too many nodes.");
        }

        switch (node)
        {
            case YamlScalarNode scalar:
                if (prefix.Length == 0)
                {
                    throw new DeploymentYamlException(
                        "Deployment YAML root cannot be a scalar.");
                }
                if (!values.TryAdd(prefix, scalar.Value))
                {
                    throw new DeploymentYamlException(
                        $"Deployment YAML contains duplicate key '{prefix}'.");
                }
                break;
            case YamlMappingNode mapping:
                foreach (var pair in mapping.Children)
                {
                    if (pair.Key is not YamlScalarNode { Value: { Length: > 0 } key })
                    {
                        throw new DeploymentYamlException(
                            "Deployment YAML mapping keys must be non-empty scalars.");
                    }

                    var childPrefix = prefix.Length == 0
                        ? key
                        : $"{prefix}:{key}";
                    Flatten(pair.Value, childPrefix, depth + 1, values, ref nodeCount);
                }
                break;
            case YamlSequenceNode sequence:
                for (var index = 0; index < sequence.Children.Count; index++)
                {
                    Flatten(
                        sequence.Children[index],
                        $"{prefix}:{index.ToString(CultureInfo.InvariantCulture)}",
                        depth + 1,
                        values,
                        ref nodeCount);
                }
                break;
            default:
                throw new DeploymentYamlException(
                    "Deployment YAML contains an unsupported node type.");
        }
    }

    private static void AddLegacyAliases(Dictionary<string, string?> values)
    {
        Alias(values, "setting:data_path", "paths:data_path");
        Alias(values, "setting:download_path", "paths:download_path");
        Alias(values, "setting:save_path", "paths:save_path");
        Alias(values, "setting:webapi:access_key", "web:access_key");
        Alias(values, "setting:webapi:host", "web:host");
        Alias(values, "setting:webapi:port", "web:port");
        Alias(values, "advanced:database:refresh_database_cron", "schedule:refresh_database_cron");
        Alias(values, "advanced:default:tmdb_fail_skip", "metadata:season_failure:skip");
        Alias(
            values,
            "advanced:default:tmdb_fail_use_title_season",
            "metadata:season_failure:use_title_season");
        Alias(
            values,
            "advanced:default:tmdb_fail_use_first_season",
            "metadata:season_failure:use_first_season");
        Alias(
            values,
            "advanced:default:tmdb_fail_backtrace",
            "metadata:season_failure:backtrace");
        Alias(
            values,
            "advanced:default:tmdb_fail_use_bangumi",
            "metadata:tmdb_failure_use_bangumi");
        Alias(
            values,
            "advanced:default:mikan_trusted_offset_cache_enabled",
            "metadata:mikan_trusted_offset_cache_enabled");
        Alias(
            values,
            "advanced:default:mikan_trusted_offset_required_episodes",
            "metadata:mikan_trusted_offset_required_episodes");
        Alias(
            values,
            "advanced:source:themoviedb:api_key",
            "metadata:tmdb:api_key");
        Alias(values, "setting:key:themoviedb", "metadata:tmdb:api_key");
        Alias(
            values,
            "advanced:cache:themoviedb_cache_hour",
            "metadata:tmdb:cache_hours");

        Add(values, "downloaders:bt:type", DownloaderTypes.Qbittorrent);
        AliasAny(
            values,
            [
                "setting:client:url",
                "setting:client:qbittorrent:url",
            ],
            "downloaders:bt:base_url");
        AliasAny(
            values,
            [
                "setting:client:username",
                "setting:client:qbittorrent:username",
            ],
            "downloaders:bt:username");
        AliasAny(
            values,
            [
                "setting:client:password",
                "setting:client:qbittorrent:password",
            ],
            "downloaders:bt:password");
        AliasAny(
            values,
            [
                "setting:client:download_path",
                "setting:download_path",
            ],
            "downloaders:bt:download_path",
            ignoreEmpty: true);
        Add(values, "downloaders:bt:enabled", "true");

        Add(values, "sources:mikan:adapter", "mikan");
        Add(values, "sources:mikan:downloader_id", "bt");
        Alias(
            values,
            "advanced:download:rename",
            "sources:mikan:file_strategy");
        Add(values, "sources:mikan:file_strategy", "move");
        Add(values, "sources:mikan:allowed_torrent_hosts:0", "mikanani.me");
        Alias(values, "setting:category", "sources:mikan:category");
        Add(values, "sources:mikan:category", "animegonet");
        Alias(values, "setting:tag", "sources:mikan:dynamic_tag_template");
        Add(values, "sources:mikan:dynamic_tag_template", "{year}年{quarter}月新番");
        AliasAny(
            values,
            [
                "advanced:client:seeding_time_minute",
                "advanced:download:seeding_time_minute",
            ],
            "sources:mikan:seeding_time_minutes");
        Add(values, "sources:mikan:seeding_time_minutes", "0");
        Add(values, "sources:mikan:rss_filter_enabled", "true");
        Add(values, "sources:mikan:rss_priority_enabled", "true");
        Add(values, "sources:mikan:duplicate_notification_enabled", "true");
        AddLegacyMikanRssAliases(values);
        AliasAny(
            values,
            [
                "advanced:source:mikan:cookie",
                "advanced:anidata:mikan:cookie",
            ],
            "sources:mikan:mikan_identity_cookie");

        AliasAnyAbsoluteUrl(
            values,
            [
                "advanced:source:bangumi:redirect",
                "advanced:anidata:bangumi:redirect",
                "advanced:redirect:bangumi",
            ],
            "metadata:bangumi:base_url");
        AliasAnyAbsoluteUrl(
            values,
            [
                "advanced:source:themoviedb:redirect",
                "advanced:anidata:themoviedb:redirect",
                "advanced:redirect:themoviedb",
            ],
            "metadata:tmdb:base_url");
        Alias(
            values,
            "advanced:request:timeout_second",
            "metadata:tmdb:timeout_seconds");
        Alias(
            values,
            "advanced:request:timeout_second",
            "metadata:bangumi:timeout_seconds");
        Alias(
            values,
            "advanced:request:retry_num",
            "metadata:tmdb:retry_count");
        Alias(
            values,
            "advanced:request:retry_num",
            "metadata:bangumi:retry_count");
        Alias(
            values,
            "advanced:request:retry_wait_second",
            "metadata:tmdb:retry_wait_seconds");
        Alias(
            values,
            "advanced:request:retry_wait_second",
            "metadata:bangumi:retry_wait_seconds");
    }

    private static void ValidateVersion(string version)
    {
        if (!SupportedVersions.Contains(version, StringComparer.Ordinal))
        {
            throw new DeploymentYamlException(
                $"Deployment YAML version '{version}' is unsupported; expected a recognized AnimeGo version from 1.1.0 through {CurrentVersion}.");
        }
    }

    private static async Task WriteNewAsync(
        string filePath,
        string content,
        CancellationToken cancellationToken)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            BufferSize = 16 * 1024,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
        };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        await using var stream = new FileStream(filePath, options);
        await using var writer = new StreamWriter(stream, StrictUtf8);
        await writer.WriteAsync(content.AsMemory(), cancellationToken).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<string> WriteBackupAsync(
        string filePath,
        string version,
        byte[] originalBytes,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(filePath)
            ?? throw new DeploymentYamlException(
                "Deployment YAML path has no parent directory.");
        var extension = Path.GetExtension(filePath);
        if (extension.Length == 0)
        {
            extension = ".yaml";
        }
        var stem = Path.GetFileNameWithoutExtension(filePath);
        var timestamp = DateTimeOffset.UtcNow.ToString(
            "yyyyMMddHHmmss",
            CultureInfo.InvariantCulture);
        for (var sequence = 0; sequence < 1000; sequence++)
        {
            var suffix = sequence == 0
                ? string.Empty
                : $"-{sequence.ToString("D3", CultureInfo.InvariantCulture)}";
            var backupPath = Path.Combine(
                directory,
                $"{stem}-{version}-{timestamp}{suffix}{extension}");
            try
            {
                await WriteExclusiveBytesAsync(
                    backupPath,
                    originalBytes,
                    cancellationToken).ConfigureAwait(false);
                return backupPath;
            }
            catch (IOException) when (File.Exists(backupPath))
            {
                // A backup with this second/sequence already exists. Never overwrite it.
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                throw new DeploymentYamlException(
                    "Legacy deployment YAML backup could not be created.",
                    exception);
            }
        }

        throw new DeploymentYamlException(
            "Legacy deployment YAML backup name space was exhausted.");
    }

    internal static async Task ReplaceAtomicallyAsync(
        string filePath,
        string content,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(filePath)
            ?? throw new DeploymentYamlException(
                "Deployment YAML path has no parent directory.");
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(filePath)}.upgrade-{Guid.NewGuid():N}.tmp");
        try
        {
            await WriteExclusiveBytesAsync(
                temporaryPath,
                StrictUtf8.GetBytes(content),
                cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, filePath, overwrite: true);
        }
        catch (DeploymentYamlException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            throw new DeploymentYamlException(
                "Legacy deployment YAML could not be replaced atomically.",
                exception);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                // The original file is still authoritative; a uniquely named temp is harmless.
            }
        }
    }

    private static async Task WriteExclusiveBytesAsync(
        string filePath,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            BufferSize = 16 * 1024,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
        };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        await using var stream = new FileStream(filePath, options);
        await stream.WriteAsync(content, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }

    private static string RenderLegacyUpgrade(
        AnimeGoOptions defaults,
        IReadOnlyDictionary<string, string?> values)
    {
        var bt = defaults.Downloaders["bt"];
        return $$"""
            # AnimeGoNet 部署配置。此文件由旧版 {{Required(values, "version")}} 配置迁移；
            # 如启用 backup，原文件已按版本和 UTC 时间备份。Python 插件不会迁移；
            # 旧动态 tag 模板迁移到来源级后置赋值，不会作为静态 tag 提前发送。
            version: {{CurrentVersion}}

            paths:
              data_path: {{Scalar(Configured(values, "paths:data_path", defaults.Paths.DataPath))}}
              download_path: {{Scalar(Configured(values, "paths:download_path", defaults.Paths.DownloadPath))}}
              save_path: {{Scalar(Configured(values, "paths:save_path", defaults.Paths.SavePath))}}

            web:
              host: {{Scalar(Configured(values, "web:host", defaults.Web.Host))}}
              port: {{Integer(values, "web:port", defaults.Web.Port)}}
              access_key: {{Scalar(Configured(values, "web:access_key", string.Empty))}}
              background_workers_enabled: true

            outbound_proxy:
              url: ''
              hosts: []

            downloaders:
              bt:
                type: qbittorrent
                base_url: {{Scalar(Configured(values, "downloaders:bt:base_url", bt.BaseUrl.AbsoluteUri))}}
                username: {{Scalar(Configured(values, "downloaders:bt:username", string.Empty))}}
                password: {{Scalar(Configured(values, "downloaders:bt:password", string.Empty))}}
                download_path: {{Scalar(Configured(values, "downloaders:bt:download_path", bt.DownloadPath))}}
                enabled: true

            sources:
              mikan:
                display_name: {{Scalar(Configured(values, "sources:mikan:display_name", "mikan"))}}
                adapter: mikan
                downloader_id: bt
                file_strategy: {{LegacyFileStrategy(values)}}
                allowed_torrent_hosts:
                  - mikanani.me
            {{LegacyMikanAdditionalAllowedHost(values)}}
                category: {{Scalar(Configured(values, "sources:mikan:category", "animegonet"))}}
                tags: []
                dynamic_tag_template: {{Scalar(Configured(values, "sources:mikan:dynamic_tag_template", "{year}年{quarter}月新番"))}}
                seeding_time_minutes: {{Integer(values, "sources:mikan:seeding_time_minutes", 0)}}
                rss_filter_enabled: true
                rss_priority_enabled: true
                duplicate_notification_enabled: true
                rss_feed_url: {{Scalar(Configured(values, "sources:mikan:rss_feed_url", string.Empty))}}
                rss_schedule_enabled: {{Boolean(values, "sources:mikan:rss_schedule_enabled", false)}}
                rss_schedule_cron: {{Scalar(Configured(values, "sources:mikan:rss_schedule_cron", SourceRssSchedulePolicy.DefaultCron))}}
                mikan_identity_cookie: {{Scalar(Configured(values, "sources:mikan:mikan_identity_cookie", string.Empty))}}

            metadata:
              mikan:
                base_url: {{Scalar(Configured(values, "metadata:mikan:base_url", defaults.Metadata.Mikan.BaseUrl.AbsoluteUri))}}
                episode_identity_cache_hours: {{Number(values, "metadata:mikan:episode_identity_cache_hours", defaults.Metadata.Mikan.EpisodeIdentityCacheTtl.TotalHours)}}
                bangumi_identity_cache_hours: {{Number(values, "metadata:mikan:bangumi_identity_cache_hours", defaults.Metadata.Mikan.BangumiIdentityCacheTtl.TotalHours)}}
              tmdb:
                base_url: {{Scalar(Configured(values, "metadata:tmdb:base_url", defaults.Metadata.Tmdb.BaseUrl.AbsoluteUri))}}
                image_base_url: {{Scalar(Configured(values, "metadata:tmdb:image_base_url", defaults.Metadata.Tmdb.ImageBaseUrl.AbsoluteUri))}}
                api_key: {{Scalar(Configured(values, "metadata:tmdb:api_key", string.Empty))}}
                read_access_token: ''
                language: {{Scalar(defaults.Metadata.Tmdb.Language)}}
                timeout_seconds: {{Number(values, "metadata:tmdb:timeout_seconds", defaults.Metadata.Tmdb.HttpTimeout.TotalSeconds)}}
                retry_count: {{Integer(values, "metadata:tmdb:retry_count", defaults.Metadata.Tmdb.RetryCount)}}
                retry_wait_seconds: {{Number(values, "metadata:tmdb:retry_wait_seconds", defaults.Metadata.Tmdb.RetryDelay.TotalSeconds)}}
                cache_hours: {{Number(values, "metadata:tmdb:cache_hours", defaults.Metadata.Tmdb.CacheTtl.TotalHours)}}
              bangumi:
                base_url: {{Scalar(Configured(values, "metadata:bangumi:base_url", defaults.Metadata.Bangumi.BaseUrl.AbsoluteUri))}}
                timeout_seconds: {{Number(values, "metadata:bangumi:timeout_seconds", defaults.Metadata.Bangumi.HttpTimeout.TotalSeconds)}}
                retry_count: {{Integer(values, "metadata:bangumi:retry_count", defaults.Metadata.Bangumi.RetryCount)}}
                retry_wait_seconds: {{Number(values, "metadata:bangumi:retry_wait_seconds", defaults.Metadata.Bangumi.RetryDelay.TotalSeconds)}}
              season_failure:
                # P4→P3→P2→P1；AI 是独立的一次任务级流程。
                skip: {{Boolean(values, "metadata:season_failure:skip", false)}}
                backtrace: {{Boolean(values, "metadata:season_failure:backtrace", false)}}
                use_title_season: {{Boolean(values, "metadata:season_failure:use_title_season", false)}}
                use_first_season: {{Boolean(values, "metadata:season_failure:use_first_season", false)}}
              tmdb_failure_use_bangumi: {{Boolean(values, "metadata:tmdb_failure_use_bangumi", false)}}
              write_bangumi_id_when_tmdb_matched: {{Boolean(values, "metadata:write_bangumi_id_when_tmdb_matched", false)}}
              mikan_trusted_offset_cache_enabled: {{Boolean(values, "metadata:mikan_trusted_offset_cache_enabled", false)}}
              mikan_trusted_offset_required_episodes: {{Integer(values, "metadata:mikan_trusted_offset_required_episodes", 3)}}
              ai:
                provider: openai_compatible
                base_url: ''
                api_key: ''
                model: ''
                reasoning_effort: none
                # 留空使用程序内置 Prompt；多行自定义值建议通过 WebUI 私有配置保存。
                prompt_template: ''
                use_metadata_match: false
                debug_mode: false
                timeout_seconds: 600
                retry_count: 2
                use_bangumi_pubdate_first: true

            torrent_fetch:
              timeout_seconds: {{defaults.TorrentFetch.Timeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)}}
              max_response_bytes: {{defaults.TorrentFetch.MaxResponseBytes.ToString(CultureInfo.InvariantCulture)}}
              max_redirects: {{defaults.TorrentFetch.MaxRedirects.ToString(CultureInfo.InvariantCulture)}}
              staging_ttl_seconds: {{defaults.TorrentFetch.StagingTtl.TotalSeconds.ToString(CultureInfo.InvariantCulture)}}

            schedule:
              refresh_database_cron: {{Scalar(Configured(values, "schedule:refresh_database_cron", defaults.Schedule.RefreshDatabaseCron))}}

            data_update:
              enabled: false
              cron: {{Scalar(defaults.DataUpdate.Cron)}}
              manifest_url: ''
              auto_download: true
              auto_import: true
              keep_versions: {{defaults.DataUpdate.KeepVersions.ToString(CultureInfo.InvariantCulture)}}
              timeout_seconds: {{defaults.DataUpdate.HttpTimeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)}}
            """.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    }

    private static string Configured(
        IReadOnlyDictionary<string, string?> values,
        string key,
        string fallback) =>
        Optional(values, key) ?? fallback;

    private static string? Optional(
        IReadOnlyDictionary<string, string?> values,
        string key) =>
        First(Value(values, key));

    private static string Boolean(
        IReadOnlyDictionary<string, string?> values,
        string key,
        bool fallback)
    {
        var value = Optional(values, key);
        if (value is null)
        {
            return fallback ? "true" : "false";
        }
        if (!bool.TryParse(value, out var parsed))
        {
            throw new DeploymentYamlException(
                $"Legacy deployment YAML field '{key}' must be a boolean.");
        }

        return parsed ? "true" : "false";
    }

    private static string Integer(
        IReadOnlyDictionary<string, string?> values,
        string key,
        int fallback)
    {
        var value = Optional(values, key);
        if (value is null)
        {
            return fallback.ToString(CultureInfo.InvariantCulture);
        }
        if (!int.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsed))
        {
            throw new DeploymentYamlException(
                $"Legacy deployment YAML field '{key}' must be an integer.");
        }

        return parsed.ToString(CultureInfo.InvariantCulture);
    }

    private static string Number(
        IReadOnlyDictionary<string, string?> values,
        string key,
        double fallback)
    {
        var value = Optional(values, key);
        if (value is null)
        {
            return fallback.ToString(CultureInfo.InvariantCulture);
        }
        if (!double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed)
            || !double.IsFinite(parsed))
        {
            throw new DeploymentYamlException(
                $"Legacy deployment YAML field '{key}' must be a finite number.");
        }

        return parsed.ToString(CultureInfo.InvariantCulture);
    }

    private static string LegacyFileStrategy(
        IReadOnlyDictionary<string, string?> values)
    {
        var strategy = Configured(
            values,
            "sources:mikan:file_strategy",
            "move").ToLowerInvariant();
        return strategy switch
        {
            "link" or "link_delete" or "move" or "wait_move" => strategy,
            _ => throw new DeploymentYamlException(
                "Legacy deployment YAML Mikan file strategy is unsupported."),
        };
    }

    internal static string RenderDefault(AnimeGoOptions options)
    {
        var bt = options.Downloaders["bt"];
        var pt = options.Downloaders["pt"];
        return $$"""
            # AnimeGoNet 部署配置。业务状态保存在 SQLite；WebUI 不回显或改写本文件中的 secret。
            version: {{CurrentVersion}}

            # Docker 固定 /data、/download/incomplete、/download/anime；原生路径可按部署修改。
            paths:
              data_path: {{Scalar(options.Paths.DataPath)}}
              download_path: {{Scalar(options.Paths.DownloadPath)}}
              save_path: {{Scalar(options.Paths.SavePath)}}

            web:
              # 原生默认 127.0.0.1:7991；Docker 默认 0.0.0.0:7991 且必须设置 access_key。
              host: {{Scalar(options.Web.Host)}}
              port: {{options.Web.Port}}
              access_key: ''
              background_workers_enabled: true

            # 唯一出站代理。仅匹配 hosts 的目标使用代理；其余保持直连。
            outbound_proxy:
              url: ''
              # 支持精确域名和 *.example.com；统一使用小写。
              hosts: []

            downloaders:
              bt:
                type: qbittorrent
                base_url: {{Scalar(bt.BaseUrl.AbsoluteUri)}}
                username: ''
                password: ''
                download_path: {{Scalar(bt.DownloadPath)}}
                enabled: true
              pt:
                type: qbittorrent
                base_url: {{Scalar(pt.BaseUrl.AbsoluteUri)}}
                username: ''
                password: ''
                download_path: {{Scalar(pt.DownloadPath)}}
                enabled: true

            sources:
              mikan:
                display_name: mikan
                adapter: mikan
                downloader_id: bt
                file_strategy: move
                allowed_torrent_hosts:
                  - mikanani.me
                category: animegonet
                tags: []
                dynamic_tag_template: '{year}年{quarter}月新番'
                seeding_time_minutes: 0
                rss_filter_enabled: true
                rss_priority_enabled: true
                duplicate_notification_enabled: true
                rss_feed_url: ''
                rss_schedule_enabled: false
                rss_schedule_cron: '0 0/15 * * * ?'
                # 可填写 Cookie 值或完整的 .AspNetCore.Identity.Application=...；受 Access-Key 保护的配置页会直接回填。
                mikan_identity_cookie: ''

            metadata:
              mikan:
                base_url: {{Scalar(options.Metadata.Mikan.BaseUrl.AbsoluteUri)}}
                episode_identity_cache_hours: {{options.Metadata.Mikan.EpisodeIdentityCacheTtl.TotalHours.ToString(CultureInfo.InvariantCulture)}}
                bangumi_identity_cache_hours: {{options.Metadata.Mikan.BangumiIdentityCacheTtl.TotalHours.ToString(CultureInfo.InvariantCulture)}}
              tmdb:
                base_url: https://api.themoviedb.org/
                image_base_url: https://image.tmdb.org/t/p/
                api_key: ''
                read_access_token: ''
                language: zh-CN
                timeout_seconds: 30
                retry_count: 3
                retry_wait_seconds: 5
              bangumi:
                base_url: https://api.bgm.tv/
                timeout_seconds: 30
                retry_count: 3
                retry_wait_seconds: 5
              season_failure:
                # P4→P3→P2→P1；AI 是独立的一次任务级流程。
                skip: false
                backtrace: false
                use_title_season: false
                use_first_season: false
              tmdb_failure_use_bangumi: false
              write_bangumi_id_when_tmdb_matched: false
              mikan_trusted_offset_cache_enabled: false
              mikan_trusted_offset_required_episodes: 3
              ai:
                provider: openai_compatible
                base_url: ''
                api_key: ''
                model: ''
                reasoning_effort: none
                # 留空使用程序内置 Prompt；多行自定义值建议通过 WebUI 私有配置保存。
                prompt_template: ''
                use_metadata_match: false
                debug_mode: false
                timeout_seconds: 600
                retry_count: 2
                use_bangumi_pubdate_first: true

            torrent_fetch:
              timeout_seconds: 30
              max_response_bytes: 16777216
              max_redirects: 3
              staging_ttl_seconds: 900

            schedule:
              refresh_database_cron: '0 0 6 * * *'

            data_update:
              enabled: false
              cron: '0 0 4 * * ?'
              manifest_url: ''
              auto_download: true
              auto_import: true
              keep_versions: 2
              timeout_seconds: 300
            """.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    }

    private static string Scalar(string value) =>
        $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";

    private static string ResolveAgainstApplication(string path) =>
        Path.GetFullPath(
            Path.IsPathRooted(path)
                ? path
                : Path.Combine(AppContext.BaseDirectory, path));

    private static string Required(
        IReadOnlyDictionary<string, string?> values,
        string key) =>
        First(Value(values, key))
        ?? throw new DeploymentYamlException(
            $"Deployment YAML requires scalar '{key}'.");

    private static string? Value(
        IReadOnlyDictionary<string, string?> values,
        string key) =>
        values.TryGetValue(key, out var value) ? value : null;

    private static string? First(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static void Alias(
        Dictionary<string, string?> values,
        string legacyKey,
        string currentKey) =>
        AliasAny(values, [legacyKey], currentKey);

    private static void AliasAny(
        Dictionary<string, string?> values,
        IReadOnlyList<string> legacyKeys,
        string currentKey,
        bool ignoreEmpty = false)
    {
        if (values.ContainsKey(currentKey))
        {
            return;
        }

        foreach (var legacyKey in legacyKeys)
        {
            if (!values.TryGetValue(legacyKey, out var value)
                || (ignoreEmpty && string.IsNullOrWhiteSpace(value)))
            {
                continue;
            }

            values[currentKey] = value;
            return;
        }
    }

    private static void AliasAbsoluteUrl(
        Dictionary<string, string?> values,
        string legacyKey,
        string currentKey)
        => AliasAnyAbsoluteUrl(values, [legacyKey], currentKey);

    private static void AliasAnyAbsoluteUrl(
        Dictionary<string, string?> values,
        IReadOnlyList<string> legacyKeys,
        string currentKey)
    {
        if (values.ContainsKey(currentKey))
        {
            return;
        }

        foreach (var legacyKey in legacyKeys)
        {
            var value = First(Value(values, legacyKey));
            if (Uri.TryCreate(value, UriKind.Absolute, out _))
            {
                values[currentKey] = value;
                return;
            }
        }
    }

    private static void AddLegacyMikanRssAliases(
        Dictionary<string, string?> values)
    {
        if (values.ContainsKey("sources:mikan:rss_feed_url"))
        {
            return;
        }

        var candidates = values.Keys
            .Where(key => key.StartsWith("plugin:feed:", StringComparison.Ordinal))
            .Select(key => key.Split(':'))
            .Where(parts => parts.Length >= 4
                && int.TryParse(
                    parts[2],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out _))
            .Select(parts => parts[2])
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => int.Parse(value, CultureInfo.InvariantCulture))
            .Where(index => string.Equals(
                Optional(values, $"plugin:feed:{index}:file"),
                "builtin_mikan_rss.py",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (candidates.Length > 1)
        {
            throw new DeploymentYamlException(
                "Legacy deployment YAML contains multiple Mikan RSS feed plugins and cannot be migrated unambiguously.");
        }

        if (candidates.Length == 1)
        {
            var prefix = $"plugin:feed:{candidates[0]}";
            AddLegacyMikanDisplayName(
                values,
                First(
                    Value(values, $"{prefix}:vars:name"),
                    Value(values, $"{prefix}:vars:__name__")));
            SetLegacyMikanRss(
                values,
                First(
                    Value(values, $"{prefix}:vars:url"),
                    Value(values, $"{prefix}:vars:__url__")),
                First(
                    Value(values, $"{prefix}:vars:cron"),
                    Value(values, $"{prefix}:vars:__cron__"))
                    ?? "0 0/20 * * * ?",
                Boolean(values, $"{prefix}:enable", false));
            return;
        }

        AddLegacyMikanDisplayName(
            values,
            Optional(values, "setting:feed:mikan:name"));
        SetLegacyMikanRss(
            values,
            Optional(values, "setting:feed:mikan:url"),
            "0 0/20 * * * ?",
            enabled: "false");
    }

    private static void AddLegacyMikanDisplayName(
        Dictionary<string, string?> values,
        string? displayName)
    {
        if (displayName is null)
        {
            return;
        }
        if (displayName.Length > 128)
        {
            throw new DeploymentYamlException(
                "Legacy deployment YAML Mikan display name is invalid.");
        }
        values["sources:mikan:display_name"] = displayName;
    }

    private static void SetLegacyMikanRss(
        Dictionary<string, string?> values,
        string? rawUrl,
        string rawCron,
        string enabled)
    {
        string? normalizedUrl;
        try
        {
            normalizedUrl = SourceRssSchedulePolicy.NormalizeFeedUrl("mikan", rawUrl);
            var normalizedCron = SourceRssSchedulePolicy.NormalizeCron(rawCron);
            if (string.Equals(enabled, "true", StringComparison.Ordinal)
                && normalizedUrl is null)
            {
                throw new ArgumentException(
                    "An enabled Mikan RSS feed requires a URL.");
            }

            if (normalizedUrl is not null)
            {
                values["sources:mikan:rss_feed_url"] = normalizedUrl;
                var host = new Uri(normalizedUrl, UriKind.Absolute).IdnHost;
                if (!string.Equals(host, "mikanani.me", StringComparison.OrdinalIgnoreCase))
                {
                    values["sources:mikan:allowed_torrent_hosts:1"] = host;
                }
            }

            values["sources:mikan:rss_schedule_cron"] = normalizedCron;
            values["sources:mikan:rss_schedule_enabled"] = enabled;
        }
        catch (ArgumentException exception)
        {
            throw new DeploymentYamlException(
                "Legacy deployment YAML Mikan RSS configuration is invalid.",
                exception);
        }
    }

    private static string LegacyMikanAdditionalAllowedHost(
        IReadOnlyDictionary<string, string?> values)
    {
        var host = Optional(values, "sources:mikan:allowed_torrent_hosts:1");
        return host is null ? string.Empty : $"      - {Scalar(host)}";
    }

    private static void Add(
        Dictionary<string, string?> values,
        string key,
        string value) =>
        values.TryAdd(key, value);
}

internal sealed class DeploymentYamlException : InvalidOperationException
{
    public DeploymentYamlException(string message)
        : base(message)
    {
    }

    public DeploymentYamlException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
