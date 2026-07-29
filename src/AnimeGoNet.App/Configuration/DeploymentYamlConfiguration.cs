using System.Globalization;
using System.Text;
using AnimeGoNet.Core.Configuration;
using Microsoft.Extensions.Configuration;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace AnimeGoNet.App.Configuration;

internal sealed record DeploymentYamlSnapshot(
    string FilePath,
    string Version,
    bool Created,
    bool LegacyLayout,
    IReadOnlyDictionary<string, string?> Values);

internal static class DeploymentYamlConfiguration
{
    internal const string CurrentVersion = "1.7.1";
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
        var explicitPath = First(
            configuration["ANIMEGO_CONFIG"],
            configuration["config"]);
        if (explicitPath is not null)
        {
            return ResolveAgainstApplication(explicitPath);
        }

        var dataPath = First(
            configuration["ANIMEGO_DATA_PATH"],
            configuration["data_path"])
            ?? defaults.Paths.DataPath;
        return Path.Combine(
            ResolveAgainstApplication(dataPath),
            "animego.yaml");
    }

    public static async Task<DeploymentYamlSnapshot> LoadOrCreateAsync(
        string filePath,
        AnimeGoOptions defaults,
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
        if (legacy)
        {
            AddLegacyAliases(values);
        }

        return new DeploymentYamlSnapshot(
            absolutePath,
            version,
            created,
            legacy,
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
            "advanced:source:themoviedb:api_key",
            "metadata:tmdb:api_key");
        Alias(values, "setting:key:themoviedb", "metadata:tmdb:api_key");

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
        Alias(
            values,
            "advanced:client:seeding_time_minute",
            "sources:mikan:seeding_time_minutes");
        Add(values, "sources:mikan:seeding_time_minutes", "0");
        Add(values, "sources:mikan:rss_filter_enabled", "true");
        Add(values, "sources:mikan:rss_priority_enabled", "true");

        AliasAbsoluteUrl(
            values,
            "advanced:source:bangumi:redirect",
            "metadata:bangumi:base_url");
        AliasAbsoluteUrl(
            values,
            "advanced:source:themoviedb:redirect",
            "metadata:tmdb:base_url");
        if (string.Equals(
                Value(values, "setting:proxy:enable"),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            AliasAbsoluteUrl(
                values,
                "setting:proxy:url",
                "metadata:tmdb:proxy_url");
            AliasAbsoluteUrl(
                values,
                "setting:proxy:url",
                "metadata:bangumi:proxy_url");
        }
    }

    private static void ValidateVersion(string version)
    {
        if (!System.Version.TryParse(version, out var parsed)
            || parsed < new System.Version(1, 1, 0)
            || parsed > new System.Version(1, 7, 1))
        {
            throw new DeploymentYamlException(
                $"Deployment YAML version '{version}' is unsupported; expected 1.1.0 through {CurrentVersion}.");
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

    private static string RenderDefault(AnimeGoOptions options)
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
              # 原生默认回环可留空；Docker 必须设置 access_key。
              access_key: ''
              background_workers_enabled: true

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
                adapter: mikan
                downloader_id: bt
                file_strategy: move
                allowed_torrent_hosts:
                  - mikanani.me
                category: animegonet
                tags: []
                seeding_time_minutes: 0
                rss_filter_enabled: true
                rss_priority_enabled: true

            metadata:
              tmdb:
                base_url: https://api.themoviedb.org/
                proxy_url: ''
                api_key: ''
                read_access_token: ''
                language: zh-CN
                timeout_seconds: 30
              bangumi:
                base_url: https://api.bgm.tv/
                proxy_url: ''
                timeout_seconds: 30
              season_failure:
                # P4→P3→P2→P1；AI 是独立的一次任务级流程。
                skip: false
                backtrace: false
                use_title_season: false
                use_first_season: false
              tmdb_failure_use_bangumi: false
              mikan_trusted_offset_cache_enabled: false
              ai:
                provider: openai_compatible
                base_url: ''
                api_key: ''
                model: ''
                use_metadata_match: false
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
    {
        var value = First(Value(values, legacyKey));
        if (!values.ContainsKey(currentKey)
            && Uri.TryCreate(value, UriKind.Absolute, out _))
        {
            values[currentKey] = value;
        }
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
