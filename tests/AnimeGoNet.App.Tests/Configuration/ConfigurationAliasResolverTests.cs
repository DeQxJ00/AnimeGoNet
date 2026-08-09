using AnimeGoNet.App.Configuration;
using AnimeGoNet.Core.Configuration;
using Microsoft.Extensions.Configuration;

namespace AnimeGoNet.App.Tests.Configuration;

public sealed class ConfigurationAliasResolverTests
{
    [Fact]
    public void LegacyEnvironmentAliasesMapEveryUpstreamField()
    {
        var root = Path.Combine(Path.GetTempPath(), "animegonet-upstream-env-parity");
        var clientDownload = Path.Combine(root, "client-download");
        var download = Path.Combine(root, "download");
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["downloaders:bt:type"] = "qbittorrent",
            ["downloaders:bt:base_url"] = "http://127.0.0.1:8080/",
            ["downloaders:bt:download_path"] = Path.Combine(root, "yaml-download"),
            ["sources:mikan:adapter"] = "mikan",
            ["sources:mikan:downloader_id"] = "bt",
            ["sources:mikan:file_strategy"] = "move",
            ["sources:mikan:allowed_torrent_hosts:0"] = "mikanani.me",
        });
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ANIMEGO_CLIENT_URL"] = "http://127.0.0.1:18080/",
            ["ANIMEGO_CLIENT_DOWNLOAD_PATH"] = clientDownload,
            ["ANIMEGO_DOWNLOAD_PATH"] = download,
            ["ANIMEGO_WEB_PORT"] = "10086",
        });

        var options = AnimeGoApplication.LoadOptions(configuration, inContainer: false);

        Assert.Equal(new Uri("http://127.0.0.1:18080/"), options.Downloaders["bt"].BaseUrl);
        Assert.Equal(Path.GetFullPath(clientDownload), options.Downloaders["bt"].DownloadPath);
        Assert.Equal(Path.GetFullPath(download), options.Paths.DownloadPath);
        Assert.Equal(10086, options.Web.Port);
    }

    [Fact]
    public void HighestPriorityProviderWinsAcrossLegacyAndCanonicalAliases()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["paths:data_path"] = "yaml-path",
        });
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ANIMEGO_DATA_PATH"] = "environment-path",
        });
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["data_path"] = "command-line-path",
        });

        Assert.Equal(
            "command-line-path",
            ConfigurationAliasResolver.FirstNonEmpty(
                configuration,
                "ANIMEGO_DATA_PATH",
                "data_path",
                "paths:data_path"));
    }

    [Fact]
    public void ExplicitEmptyValueInHigherProviderClearsPresentOptionalAlias()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["outbound_proxy:url"] = "http://yaml.invalid:7890/",
        });
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ANIMEGO_OUTBOUND_PROXY_URL"] = "http://environment.invalid:7890/",
        });
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["outbound_proxy_url"] = string.Empty,
        });

        Assert.Equal(
            string.Empty,
            ConfigurationAliasResolver.FirstPresent(
                configuration,
                "outbound_proxy_url",
                "ANIMEGO_OUTBOUND_PROXY_URL",
                "outbound_proxy:url"));
    }

    [Fact]
    public void LoadOptionsAndConfigPathUseProviderPriorityAcrossPathAliases()
    {
        var root = Path.Combine(Path.GetTempPath(), "animegonet-alias-priority");
        var yamlData = Path.Combine(root, "yaml-data");
        var environmentData = Path.Combine(root, "environment-data");
        var commandData = Path.Combine(root, "command-data");
        var commandDownload = Path.Combine(root, "command-download");
        var commandSave = Path.Combine(root, "command-save");
        var environmentConfig = Path.Combine(root, "environment.yaml");
        var commandConfig = Path.Combine(root, "command.yaml");
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["paths:data_path"] = yamlData,
            ["paths:download_path"] = Path.Combine(root, "yaml-download"),
            ["paths:save_path"] = Path.Combine(root, "yaml-save"),
        });
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ANIMEGO_DATA_PATH"] = environmentData,
            ["ANIMEGO_DOWNLOAD_PATH"] = Path.Combine(root, "environment-download"),
            ["ANIMEGO_SAVE_PATH"] = Path.Combine(root, "environment-save"),
            ["ANIMEGO_CONFIG"] = environmentConfig,
        });
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["data_path"] = commandData,
            ["download_path"] = commandDownload,
            ["save_path"] = commandSave,
            ["config"] = commandConfig,
        });

        var options = AnimeGoApplication.LoadOptions(configuration, inContainer: false);
        var configPath = DeploymentYamlConfiguration.ResolvePath(
            configuration,
            AnimeGoDefaults.CreateNative(root));

        Assert.Equal(Path.GetFullPath(commandData), options.Paths.DataPath);
        Assert.Equal(Path.GetFullPath(commandDownload), options.Paths.DownloadPath);
        Assert.Equal(Path.GetFullPath(commandSave), options.Paths.SavePath);
        Assert.Equal(Path.GetFullPath(commandConfig), configPath);
    }

    [Fact]
    public void CanonicalDownloaderAndSourceKeysBeatLowerProviderLegacyAliases()
    {
        var root = Path.Combine(Path.GetTempPath(), "animegonet-routing-priority");
        var downloadRoot = Path.Combine(root, "download");
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["paths:data_path"] = Path.Combine(root, "data"),
            ["paths:download_path"] = downloadRoot,
            ["paths:save_path"] = Path.Combine(root, "save"),
            ["downloaders:bt:base_url"] = "http://yaml.invalid:8080/",
            ["downloaders:bt:username"] = "yaml-user",
            ["downloaders:bt:password"] = "yaml-password",
            ["downloaders:bt:download_path"] = Path.Combine(downloadRoot, "yaml"),
            ["sources:mikan:downloader_id"] = "bt",
            ["sources:mikan:category"] = "yaml-category",
        });
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ANIMEGO_CLIENT_URL"] = "http://environment.invalid:8080/",
            ["ANIMEGO_CLIENT_USERNAME"] = "environment-user",
            ["ANIMEGO_CLIENT_PASSWORD"] = "environment-password",
            ["ANIMEGO_CLIENT_DOWNLOAD_PATH"] = Path.Combine(downloadRoot, "environment"),
            ["ANIMEGO_CATEGORY"] = "environment-category",
        });
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["downloaders:bt:base_url"] = "http://command.invalid:8080/",
            ["downloaders:bt:username"] = "command-user",
            ["downloaders:bt:password"] = "command-password",
            ["downloaders:bt:download_path"] = Path.Combine(downloadRoot, "command"),
            ["sources:mikan:category"] = "command-category",
        });

        var options = AnimeGoApplication.LoadOptions(configuration, inContainer: false);
        var downloader = Assert.Single(options.Downloaders).Value;
        var source = Assert.Single(options.InitialSourceProfiles);

        Assert.Equal(new Uri("http://command.invalid:8080/"), downloader.BaseUrl);
        Assert.Equal("command-user", downloader.Username);
        Assert.Equal("command-password", downloader.Password);
        Assert.Equal(Path.Combine(downloadRoot, "command"), downloader.DownloadPath);
        Assert.Equal("command-category", source.Category);
    }

    [Fact]
    public void HighestProviderAlsoDefinesUnifiedAiLegacyAliasGroup()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["metadata:ai:use_metadata_match"] = "true",
        });
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ai_use_season_match"] = "false",
            ["ai_use_episode_match"] = "false",
        });

        var options = AnimeGoApplication.LoadOptions(configuration, inContainer: false);

        Assert.False(options.Metadata.Ai.UseMetadataMatch);
    }
}
