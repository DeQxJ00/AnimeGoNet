using AnimeGoNet.App.Configuration;
using AnimeGoNet.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeGoNet.App.Tests.Configuration;

public sealed class DeploymentYamlConfigurationTests
{
    [Fact]
    public async Task FirstStartCreatesAnnotatedUtf8YamlWithSafeDefaults()
    {
        var root = CreateRoot();
        try
        {
            var defaults = AnimeGoDefaults.CreateNative(root);
            var path = Path.Combine(root, "data", "animego.yaml");

            var snapshot = await DeploymentYamlConfiguration.LoadOrCreateAsync(
                path,
                defaults);

            Assert.True(snapshot.Created);
            Assert.False(snapshot.LegacyLayout);
            Assert.Equal(DeploymentYamlConfiguration.CurrentVersion, snapshot.Version);
            Assert.Equal(Path.GetFullPath(path), snapshot.FilePath);
            Assert.Equal(defaults.Paths.DataPath, snapshot.Values["paths:data_path"]);
            Assert.Equal("127.0.0.1", snapshot.Values["web:host"]);
            Assert.Equal("7991", snapshot.Values["web:port"]);
            Assert.Equal("move", snapshot.Values["sources:mikan:file_strategy"]);
            Assert.Equal(
                string.Empty,
                snapshot.Values["sources:mikan:mikan_identity_cookie"]);
            Assert.Equal("false", snapshot.Values["metadata:ai:use_metadata_match"]);
            Assert.Equal("600", snapshot.Values["metadata:ai:timeout_seconds"]);

            var text = await File.ReadAllTextAsync(path);
            Assert.StartsWith("# AnimeGoNet 部署配置", text, StringComparison.Ordinal);
            Assert.Contains("# P4→P3→P2→P1", text, StringComparison.Ordinal);
            Assert.Contains("password: ''", text, StringComparison.Ordinal);
            Assert.DoesNotContain("\uFEFF", text, StringComparison.Ordinal);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task DockerFirstStartWritesThePublishedSharedDirectoryContract()
    {
        var root = CreateRoot();
        try
        {
            var path = Path.Combine(root, "animego.yaml");

            var snapshot = await DeploymentYamlConfiguration.LoadOrCreateAsync(
                path,
                AnimeGoDefaults.CreateDocker());

            Assert.Equal("/data", snapshot.Values["paths:data_path"]);
            Assert.Equal(
                "/download/incomplete",
                snapshot.Values["paths:download_path"]);
            Assert.Equal("/download/anime", snapshot.Values["paths:save_path"]);
            Assert.Equal("0.0.0.0", snapshot.Values["web:host"]);
            Assert.Equal("7991", snapshot.Values["web:port"]);
            Assert.Equal(
                "/download/incomplete/bt",
                snapshot.Values["downloaders:bt:download_path"]);
            Assert.Equal(
                "/download/incomplete/pt",
                snapshot.Values["downloaders:pt:download_path"]);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task NewYamlBindsNamedDownloaderSourceMetadataAndCliOverrides()
    {
        var root = CreateRoot();
        var yamlPath = Path.Combine(root, "deployment.yaml");
        var yamlData = Path.Combine(root, "yaml-data");
        var cliData = Path.Combine(root, "cli-data");
        var download = Path.Combine(root, "download");
        var save = Path.Combine(root, "save");
        try
        {
            await File.WriteAllTextAsync(
                yamlPath,
                $$"""
                version: 1.7.1
                paths:
                  data_path: '{{Yaml(yamlData)}}'
                  download_path: '{{Yaml(download)}}'
                  save_path: '{{Yaml(save)}}'
                web:
                  access_key: ''
                  background_workers_enabled: false
                downloaders:
                  sandbox:
                    type: qbittorrent
                    base_url: http://127.0.0.1:18080/
                    username: local-user
                    password: local-password
                    download_path: '{{Yaml(download)}}'
                    enabled: true
                sources:
                  mikan:
                    adapter: mikan
                    downloader_id: sandbox
                    file_strategy: move
                    allowed_torrent_hosts:
                      - mikan.example.invalid
                    category: animegonet-yaml
                    tags:
                      - yaml-test
                    seeding_time_minutes: 0
                    rss_filter_enabled: true
                    rss_priority_enabled: false
                    mikan_identity_cookie: '.AspNetCore.Identity.Application=yaml-private-cookie'
                metadata:
                  tmdb:
                    base_url: https://tmdb.example.invalid/api/
                    proxy_url: http://127.0.0.1:17890/
                    api_key: yaml-key
                    read_access_token: ''
                    language: ja-JP
                    timeout_seconds: 41
                    retry_count: 4
                    retry_wait_seconds: 6.5
                    cache_hours: 72
                  bangumi:
                    base_url: https://bangumi.example.invalid/v0/
                    proxy_url: ''
                    timeout_seconds: 42
                    retry_count: 5
                    retry_wait_seconds: 7.5
                  season_failure:
                    skip: true
                    backtrace: true
                    use_title_season: true
                    use_first_season: true
                  tmdb_failure_use_bangumi: true
                  mikan_trusted_offset_cache_enabled: true
                  ai:
                    provider: openai_compatible
                    base_url: https://ai.example.invalid/v1/
                    api_key: ai-key
                    model: test-model
                    use_metadata_match: true
                    timeout_seconds: 601
                    retry_count: 4
                    use_bangumi_pubdate_first: false
                torrent_fetch:
                  timeout_seconds: 31
                  max_response_bytes: 123456
                  max_redirects: 2
                  staging_ttl_seconds: 901
                schedule:
                  refresh_database_cron: '1 2 3 * * *'
                data_update:
                  enabled: true
                  cron: '2 3 4 * * ?'
                  manifest_url: https://data.example.invalid/latest.json
                  auto_download: false
                  auto_import: false
                  keep_versions: 3
                  timeout_seconds: 302
                """);

            await using var app = await AnimeGoApplication.BuildAsync(
                [
                    "--config", yamlPath,
                    "--data_path", cliData,
                    "--tmdb_language", "en-US",
                ],
                runningInContainer: false,
                startBackgroundWorkers: false);
            var options = app.Services.GetRequiredService<AnimeGoOptions>();

            Assert.Equal(Path.GetFullPath(cliData), options.Paths.DataPath);
            Assert.Equal(Path.GetFullPath(download), options.Paths.DownloadPath);
            Assert.Equal(Path.GetFullPath(save), options.Paths.SavePath);
            var downloader = Assert.Single(options.Downloaders);
            Assert.Equal("sandbox", downloader.Key);
            Assert.Equal(new Uri("http://127.0.0.1:18080/"), downloader.Value.BaseUrl);
            Assert.Equal("local-user", downloader.Value.Username);
            Assert.Equal("local-password", downloader.Value.Password);
            Assert.Equal(Path.GetFullPath(download), downloader.Value.DownloadPath);
            var source = Assert.Single(options.InitialSourceProfiles);
            Assert.Equal("sandbox", source.DownloaderId);
            Assert.Equal(FileStrategy.Move, source.FileStrategy);
            Assert.Equal(["mikan.example.invalid"], source.AllowedTorrentHosts);
            Assert.Equal(["yaml-test"], source.Tags);
            Assert.False(source.RssPriorityEnabled);
            Assert.Equal("yaml-private-cookie", source.MikanIdentityCookie);
            Assert.DoesNotContain(
                "yaml-private-cookie",
                source.ToString(),
                StringComparison.Ordinal);

            Assert.Equal(new Uri("https://tmdb.example.invalid/api/"), options.Metadata.Tmdb.BaseUrl);
            Assert.Equal(new Uri("http://127.0.0.1:17890/"), options.Metadata.Tmdb.ProxyUrl);
            Assert.Equal("yaml-key", options.Metadata.Tmdb.ApiKey);
            Assert.Equal("en-US", options.Metadata.Tmdb.Language);
            Assert.Equal(TimeSpan.FromSeconds(41), options.Metadata.Tmdb.HttpTimeout);
            Assert.Equal(4, options.Metadata.Tmdb.RetryCount);
            Assert.Equal(TimeSpan.FromSeconds(6.5), options.Metadata.Tmdb.RetryDelay);
            Assert.Equal(TimeSpan.FromHours(72), options.Metadata.Tmdb.CacheTtl);
            Assert.Equal(
                new Uri("https://bangumi.example.invalid/v0/"),
                options.Metadata.Bangumi.BaseUrl);
            Assert.Equal(5, options.Metadata.Bangumi.RetryCount);
            Assert.Equal(TimeSpan.FromSeconds(7.5), options.Metadata.Bangumi.RetryDelay);
            Assert.True(options.Metadata.SeasonFailure.Skip);
            Assert.True(options.Metadata.SeasonFailure.Backtrace);
            Assert.True(options.Metadata.SeasonFailure.UseTitleSeason);
            Assert.True(options.Metadata.SeasonFailure.UseFirstSeason);
            Assert.True(options.Metadata.TmdbFailureUseBangumi);
            Assert.True(options.Metadata.MikanTrustedOffsetCacheEnabled);
            Assert.True(options.Metadata.Ai.UseMetadataMatch);
            Assert.Equal(TimeSpan.FromSeconds(601), options.Metadata.Ai.HttpTimeout);
            Assert.Equal(4, options.Metadata.Ai.RetryCount);
            Assert.False(options.Metadata.Ai.UseBangumiPubDateFirst);
            Assert.Equal(TimeSpan.FromSeconds(31), options.TorrentFetch.Timeout);
            Assert.Equal(123456, options.TorrentFetch.MaxResponseBytes);
            Assert.Equal(2, options.TorrentFetch.MaxRedirects);
            Assert.Equal(TimeSpan.FromSeconds(901), options.TorrentFetch.StagingTtl);
            Assert.Equal("1 2 3 * * *", options.Schedule.RefreshDatabaseCron);
            Assert.True(options.DataUpdate.Enabled);
            Assert.False(options.DataUpdate.AutoDownload);
            Assert.False(options.DataUpdate.AutoImport);
            Assert.Equal(3, options.DataUpdate.KeepVersions);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Theory]
    [InlineData("1.1.0")]
    [InlineData("1.2.0")]
    [InlineData("1.3.0")]
    [InlineData("1.4.0")]
    [InlineData("1.4.1")]
    [InlineData("1.5.0")]
    [InlineData("1.5.1")]
    [InlineData("1.5.2")]
    [InlineData("1.6.0")]
    [InlineData("1.6.1")]
    [InlineData("1.6.2")]
    [InlineData("1.7.0")]
    [InlineData("1.7.1")]
    public async Task LegacyVersionsMapToCanonicalQbittorrentAndMikanKeys(
        string version)
    {
        var root = CreateRoot();
        try
        {
            var path = Path.Combine(root, "legacy.yaml");
            await File.WriteAllTextAsync(
                path,
                $$"""
                version: {{version}}
                setting:
                  client:
                    client: QBittorrent
                    url: http://127.0.0.1:18080/
                    username: legacy-user
                    password: legacy-password
                    download_path: ''
                  data_path: legacy-data
                  download_path: legacy-download
                  save_path: legacy-save
                  category: LegacyAnime
                  webapi:
                    access_key: legacy-access
                advanced:
                  download:
                    rename: link_delete
                  default:
                    tmdb_fail_skip: true
                    tmdb_fail_use_title_season: true
                    tmdb_fail_use_first_season: false
                  client:
                    seeding_time_minute: 30
                  database:
                    refresh_database_cron: '0 0 7 * * *'
                """);

            var snapshot = await DeploymentYamlConfiguration.LoadOrCreateAsync(
                path,
                AnimeGoDefaults.CreateNative(root),
                backupLegacy: false);

            Assert.False(snapshot.Created);
            Assert.False(snapshot.LegacyLayout);
            Assert.True(snapshot.Upgraded);
            Assert.Null(snapshot.BackupFilePath);
            Assert.Equal(DeploymentYamlConfiguration.CurrentVersion, snapshot.Version);
            Assert.Equal("legacy-data", snapshot.Values["paths:data_path"]);
            Assert.Equal("http://127.0.0.1:18080/", snapshot.Values["downloaders:bt:base_url"]);
            Assert.Equal("legacy-download", snapshot.Values["downloaders:bt:download_path"]);
            Assert.Equal("link_delete", snapshot.Values["sources:mikan:file_strategy"]);
            Assert.Equal("30", snapshot.Values["sources:mikan:seeding_time_minutes"]);
            Assert.Equal("true", snapshot.Values["metadata:season_failure:skip"]);
            Assert.Equal("0 0 7 * * *", snapshot.Values["schedule:refresh_database_cron"]);
            var upgraded = await File.ReadAllTextAsync(path);
            Assert.Contains("version: 1.7.1", upgraded, StringComparison.Ordinal);
            Assert.DoesNotContain("\nsetting:", upgraded, StringComparison.Ordinal);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task LegacyUpgradeBacksUpExactOriginalAndIsIdempotent()
    {
        var root = CreateRoot();
        try
        {
            var path = Path.Combine(root, "animego.yaml");
            var original =
                """
                version: 1.6.1
                setting:
                  client:
                    qbittorrent:
                      url: http://127.0.0.1:18080/
                      username: legacy-user
                      password: legacy-secret
                      download_path: ''
                  data_path: legacy-data
                  download_path: legacy-download
                  save_path: legacy-save
                  category: LegacyAnime
                  tag: legacy-tag
                  webapi:
                    access_key: legacy-access
                  proxy:
                    enable: true
                    url: http://127.0.0.1:17890/
                  key:
                    themoviedb: legacy-tmdb-key
                advanced:
                  anidata:
                    mikan:
                      cookie: '.AspNetCore.Identity.Application=legacy-private-cookie'
                    bangumi:
                      redirect: https://bangumi.example.invalid/
                    themoviedb:
                      redirect: https://tmdb.example.invalid/
                  request:
                    timeout_second: 47
                    retry_num: 4
                    retry_wait_second: 6
                  download:
                    rename: wait_move
                    seeding_time_minute: 15
                  default:
                    tmdb_fail_skip: true
                    tmdb_fail_use_title_season: false
                    tmdb_fail_use_first_season: true
                  database:
                    refresh_database_cron: '0 0 8 * * *'
                """;
            await File.WriteAllTextAsync(path, original);

            var snapshot = await DeploymentYamlConfiguration.LoadOrCreateAsync(
                path,
                AnimeGoDefaults.CreateNative(root));

            Assert.True(snapshot.Upgraded);
            Assert.False(snapshot.LegacyLayout);
            Assert.NotNull(snapshot.BackupFilePath);
            Assert.Matches(
                @"animego-1\.6\.1-\d{14}(?:-\d{3})?\.yaml$",
                snapshot.BackupFilePath);
            Assert.Equal(
                System.Text.Encoding.UTF8.GetBytes(original),
                await File.ReadAllBytesAsync(snapshot.BackupFilePath!));
            Assert.Equal(
                original.Replace("\r\n", "\n", StringComparison.Ordinal),
                (await File.ReadAllTextAsync(snapshot.BackupFilePath!))
                    .Replace("\r\n", "\n", StringComparison.Ordinal));
            Assert.DoesNotContain(
                snapshot.Values.Keys,
                key => key.StartsWith(
                    "sources:mikan:tags:",
                    StringComparison.Ordinal));
            Assert.Equal(
                "legacy-tag",
                snapshot.Values["sources:mikan:dynamic_tag_template"]);
            Assert.Equal("wait_move", snapshot.Values["sources:mikan:file_strategy"]);
            Assert.Equal("15", snapshot.Values["sources:mikan:seeding_time_minutes"]);
            Assert.Equal(
                "https://tmdb.example.invalid/",
                snapshot.Values["metadata:tmdb:base_url"]);
            Assert.Equal(
                "https://bangumi.example.invalid/",
                snapshot.Values["metadata:bangumi:base_url"]);
            Assert.Equal(
                "http://127.0.0.1:17890/",
                snapshot.Values["metadata:tmdb:proxy_url"]);
            Assert.Equal("legacy-tmdb-key", snapshot.Values["metadata:tmdb:api_key"]);
            Assert.Equal("47", snapshot.Values["metadata:tmdb:timeout_seconds"]);
            Assert.Equal("4", snapshot.Values["metadata:tmdb:retry_count"]);
            Assert.Equal("6", snapshot.Values["metadata:tmdb:retry_wait_seconds"]);
            Assert.Equal("4", snapshot.Values["metadata:bangumi:retry_count"]);
            Assert.Equal("6", snapshot.Values["metadata:bangumi:retry_wait_seconds"]);
            Assert.Equal(
                ".AspNetCore.Identity.Application=legacy-private-cookie",
                snapshot.Values["sources:mikan:mikan_identity_cookie"]);
            var upgradedText = await File.ReadAllTextAsync(path);
            Assert.Contains(
                "mikan_identity_cookie: '.AspNetCore.Identity.Application=legacy-private-cookie'",
                upgradedText,
                StringComparison.Ordinal);
            Assert.Contains(
                "dynamic_tag_template: 'legacy-tag'",
                upgradedText,
                StringComparison.Ordinal);

            var second = await DeploymentYamlConfiguration.LoadOrCreateAsync(
                path,
                AnimeGoDefaults.CreateNative(root));

            Assert.False(second.Upgraded);
            Assert.False(second.LegacyLayout);
            Assert.Null(second.BackupFilePath);
            Assert.Single(
                Directory.GetFiles(
                    root,
                    "animego-1.6.1-*.yaml",
                    SearchOption.TopDirectoryOnly));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task UnsupportedLegacyDownloaderIsNotRewrittenOrBackedUp()
    {
        var root = CreateRoot();
        try
        {
            var path = Path.Combine(root, "animego.yaml");
            var original =
                """
                version: 1.7.1
                setting:
                  client:
                    client: Transmission
                    url: http://127.0.0.1:9091/
                    username: transmission-user
                    password: transmission-secret
                  data_path: legacy-data
                  download_path: legacy-download
                  save_path: legacy-save
                """;
            await File.WriteAllTextAsync(path, original);

            var snapshot = await DeploymentYamlConfiguration.LoadOrCreateAsync(
                path,
                AnimeGoDefaults.CreateNative(root));

            Assert.True(snapshot.LegacyLayout);
            Assert.False(snapshot.Upgraded);
            Assert.Null(snapshot.BackupFilePath);
            Assert.Equal(original, await File.ReadAllTextAsync(path));
            Assert.Empty(
                Directory.GetFiles(
                    root,
                    "animego-1.7.1-*.yaml",
                    SearchOption.TopDirectoryOnly));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task InvalidLegacyValueLeavesOriginalUntouchedWithoutBackup()
    {
        var root = CreateRoot();
        try
        {
            var path = Path.Combine(root, "animego.yaml");
            var original =
                """
                version: 1.7.1
                setting:
                  data_path: legacy-data
                  download_path: legacy-download
                  save_path: legacy-save
                advanced:
                  default:
                    tmdb_fail_skip: definitely-not-a-boolean
                """;
            await File.WriteAllTextAsync(path, original);

            var exception = await Assert.ThrowsAsync<DeploymentYamlException>(
                () => DeploymentYamlConfiguration.LoadOrCreateAsync(
                    path,
                    AnimeGoDefaults.CreateNative(root)));

            Assert.Contains(
                "must be a boolean",
                exception.Message,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "definitely-not-a-boolean",
                exception.ToString(),
                StringComparison.Ordinal);
            Assert.Equal(original, await File.ReadAllTextAsync(path));
            Assert.Empty(
                Directory.GetFiles(
                    root,
                    "animego-1.7.1-*.yaml",
                    SearchOption.TopDirectoryOnly));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task BackupFalseCommandLineUpgradeProducesBuildableApplication()
    {
        var root = CreateRoot();
        try
        {
            var path = Path.Combine(root, "animego.yaml");
            var data = Path.Combine(root, "data");
            var download = Path.Combine(root, "download");
            var save = Path.Combine(root, "save");
            await File.WriteAllTextAsync(
                path,
                $$"""
                version: 1.7.1
                setting:
                  client:
                    client: QBittorrent
                    url: http://127.0.0.1:18080/
                    username: legacy-user
                    password: legacy-password
                    download_path: ''
                  data_path: '{{Yaml(data)}}'
                  download_path: '{{Yaml(download)}}'
                  save_path: '{{Yaml(save)}}'
                  category: LegacyAnime
                advanced:
                  download:
                    rename: move
                  client:
                    seeding_time_minute: 0
                """);

            await using var app = await AnimeGoApplication.BuildAsync(
                [
                    "--config", path,
                    "--backup=false",
                ],
                runningInContainer: false,
                startBackgroundWorkers: false);
            var options = app.Services.GetRequiredService<AnimeGoOptions>();

            Assert.Equal(Path.GetFullPath(data), options.Paths.DataPath);
            Assert.Equal(Path.GetFullPath(download), options.Paths.DownloadPath);
            Assert.Equal(Path.GetFullPath(save), options.Paths.SavePath);
            var downloader = Assert.Single(options.Downloaders);
            Assert.Equal("bt", downloader.Key);
            Assert.Equal(Path.GetFullPath(download), downloader.Value.DownloadPath);
            Assert.Equal("bt", Assert.Single(options.InitialSourceProfiles).DownloaderId);
            Assert.Empty(
                Directory.GetFiles(
                    root,
                    "animego-1.7.1-*.yaml",
                    SearchOption.TopDirectoryOnly));
            Assert.DoesNotContain(
                "\nsetting:",
                await File.ReadAllTextAsync(path),
                StringComparison.Ordinal);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task InvalidOrUnsupportedYamlFailsWithoutEchoingSecrets()
    {
        var root = CreateRoot();
        try
        {
            var path = Path.Combine(root, "invalid.yaml");
            await File.WriteAllTextAsync(
                path,
                """
                version: 9.9.9
                secret: do-not-echo-this
                """);

            var exception = await Assert.ThrowsAsync<DeploymentYamlException>(
                () => DeploymentYamlConfiguration.LoadOrCreateAsync(
                    path,
                    AnimeGoDefaults.CreateNative(root)));

            Assert.Contains("unsupported", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("do-not-echo-this", exception.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task DuplicateYamlKeyFailsWithAStableRedactedDiagnostic()
    {
        var root = CreateRoot();
        try
        {
            var path = Path.Combine(root, "duplicate.yaml");
            await File.WriteAllTextAsync(
                path,
                """
                version: 1.7.1
                metadata:
                  tmdb:
                    api_key: do-not-echo-this
                    api_key: second-secret
                """);

            var exception = await Assert.ThrowsAsync<DeploymentYamlException>(
                () => DeploymentYamlConfiguration.LoadOrCreateAsync(
                    path,
                    AnimeGoDefaults.CreateNative(root)));

            Assert.Contains(
                "syntax",
                exception.Message,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                "do-not-echo-this",
                exception.ToString(),
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "second-secret",
                exception.ToString(),
                StringComparison.Ordinal);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task CommandLineDownloaderSecretCannotBeOverriddenByPrivateWebConfiguration()
    {
        var root = CreateRoot();
        var data = Path.Combine(root, "data");
        var yamlPath = Path.Combine(root, "deployment.yaml");
        var downloadRoot = Path.Combine(root, "download");
        var yamlDownload = Path.Combine(downloadRoot, "yaml");
        var privateDownload = Path.Combine(downloadRoot, "private");
        try
        {
            await File.WriteAllTextAsync(
                yamlPath,
                $$"""
                version: 1.7.1
                paths:
                  data_path: '{{Yaml(data)}}'
                  download_path: '{{Yaml(downloadRoot)}}'
                  save_path: '{{Yaml(Path.Combine(root, "save"))}}'
                downloaders:
                  bt:
                    type: qbittorrent
                    base_url: http://127.0.0.1:18080/
                    username: yaml-user
                    password: yaml-password
                    download_path: '{{Yaml(yamlDownload)}}'
                    enabled: true
                sources:
                  mikan:
                    adapter: mikan
                    downloader_id: bt
                    file_strategy: move
                    allowed_torrent_hosts:
                      - mikan.example.invalid
                    category: animegonet
                    seeding_time_minutes: 0
                    rss_filter_enabled: true
                    rss_priority_enabled: true
                """);
            using (var store = new DownloaderOverrideStore(
                       Path.Combine(data, "config")))
            {
                await store.UpsertAsync(
                    "bt",
                    new DownloaderOverrideEntry(
                        "http://127.0.0.1:19090/",
                        "private-user",
                        "private-password",
                        privateDownload,
                        true,
                        0,
                        DateTimeOffset.UtcNow),
                    expectedRevision: 0);
            }

            await using var app = await AnimeGoApplication.BuildAsync(
                [
                    "--config", yamlPath,
                    "--downloaders:bt:password=cli-password",
                ],
                runningInContainer: false,
                startBackgroundWorkers: false);
            var downloader = app.Services
                .GetRequiredService<AnimeGoOptions>()
                .Downloaders["bt"];

            Assert.Equal(new Uri("http://127.0.0.1:19090/"), downloader.BaseUrl);
            Assert.Equal("private-user", downloader.Username);
            Assert.Equal("cli-password", downloader.Password);
            Assert.Equal(Path.GetFullPath(privateDownload), downloader.DownloadPath);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "animegonet-yaml-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string Yaml(string value) =>
        value.Replace("'", "''", StringComparison.Ordinal);
}
