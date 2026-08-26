using YamlDotNet.RepresentationModel;

namespace AnimeGoNet.App.Tests.Delivery;

public sealed class ExternalQbittorrentDeploymentContractTests
{
    [Fact]
    public async Task ComposeRunsOnlyAnimeGoNetAndLocksBothDownloadersToSharedPaths()
    {
        var root = RepositoryRoot();
        var path = Path.Combine(root, "docker-compose.external-qbittorrent.yml");
        var compose = await File.ReadAllTextAsync(path);

        var yaml = new YamlStream();
        yaml.Load(new StringReader(compose));
        var document = Assert.IsType<YamlMappingNode>(Assert.Single(yaml.Documents).RootNode);
        var services = Mapping(document, "services");
        var service = Mapping(services, "animegonet");
        Assert.Single(services.Children);

        var environment = Mapping(service, "environment");
        AssertScalar(environment, "data_path", "/data");
        AssertScalar(environment, "download_path", "/download/incomplete");
        AssertScalar(environment, "save_path", "/download/anime");
        AssertScalar(environment, "movie_save_path", "/download/movies");
        AssertScalar(environment, "downloaders__bt__download_path", "/download/incomplete/bt");
        AssertScalar(environment, "downloaders__pt__download_path", "/download/incomplete/pt");
        AssertRequiredVariable(
            environment,
            "inner_plugin_mikan__access_key",
            "ANIMEGONET_ACCESS_KEY");
        AssertRequiredVariable(
            environment,
            "inner_plugin_u2__access_key",
            "ANIMEGONET_U2_ACCESS_KEY");
        AssertScalar(
            environment,
            "webui_access_key",
            "${ANIMEGONET_WEBUI_ACCESS_KEY:-}");
        AssertRequiredVariable(environment, "downloaders__bt__base_url", "QBITTORRENT_BT_URL");
        AssertRequiredVariable(environment, "downloaders__bt__username", "QBITTORRENT_BT_USERNAME");
        AssertRequiredVariable(environment, "downloaders__bt__password", "QBITTORRENT_BT_PASSWORD");
        AssertRequiredVariable(environment, "downloaders__pt__base_url", "QBITTORRENT_PT_URL");
        AssertRequiredVariable(environment, "downloaders__pt__username", "QBITTORRENT_PT_USERNAME");
        AssertRequiredVariable(environment, "downloaders__pt__password", "QBITTORRENT_PT_PASSWORD");

        var volumes = Sequence(service, "volumes")
            .Children
            .Select(node => Assert.IsType<YamlMappingNode>(node))
            .ToArray();
        Assert.Contains(volumes, volume =>
            Scalar(volume, "source").Contains("ANIMEGONET_DATA_ROOT", StringComparison.Ordinal)
            && Scalar(volume, "target") == "/data");
        Assert.Contains(volumes, volume =>
            Scalar(volume, "source").Contains("ANIMEGONET_SHARED_DOWNLOAD_ROOT", StringComparison.Ordinal)
            && Scalar(volume, "target") == "/download");

        Assert.Equal("true", Scalar(service, "read_only"));
        Assert.Contains("no-new-privileges:true", compose, StringComparison.Ordinal);
        Assert.DoesNotContain("depends_on", compose, StringComparison.Ordinal);
        Assert.DoesNotContain("TestSpace", compose, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("passkey", compose, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DocumentationRequiresIdenticalContainerPathsAndSafeExplicitVerification()
    {
        var root = RepositoryRoot();
        var documentation = await File.ReadAllTextAsync(
            Path.Combine(root, "docs", "EXTERNAL_QBITTORRENT.md"));
        var ignore = await File.ReadAllTextAsync(Path.Combine(root, ".gitignore"));

        Assert.Contains("/download/incomplete/bt", documentation, StringComparison.Ordinal);
        Assert.Contains("/download/incomplete/pt", documentation, StringComparison.Ordinal);
        Assert.Contains("容器内路径必须完全相同", documentation, StringComparison.Ordinal);
        Assert.Contains("同一份存储", documentation, StringComparison.Ordinal);
        Assert.Contains("config --quiet", documentation, StringComparison.Ordinal);
        Assert.Contains("/api/v1/downloaders/bt/test", documentation, StringComparison.Ordinal);
        Assert.Contains("/api/v1/downloaders/bt/path-probe", documentation, StringComparison.Ordinal);
        Assert.Contains("/api/v1/downloaders/pt/test", documentation, StringComparison.Ordinal);
        Assert.Contains("/api/v1/downloaders/pt/path-probe", documentation, StringComparison.Ordinal);
        Assert.Contains("可识别的 AnimeGoNet category/tag", documentation, StringComparison.Ordinal);
        Assert.Contains("精确清理", documentation, StringComparison.Ordinal);
        Assert.Contains(".env", ignore, StringComparison.Ordinal);
        Assert.Contains("*.private.json", ignore, StringComparison.Ordinal);
    }

    private static YamlMappingNode Mapping(YamlMappingNode parent, string key) =>
        Assert.IsType<YamlMappingNode>(parent.Children[new YamlScalarNode(key)]);

    private static YamlSequenceNode Sequence(YamlMappingNode parent, string key) =>
        Assert.IsType<YamlSequenceNode>(parent.Children[new YamlScalarNode(key)]);

    private static string Scalar(YamlMappingNode parent, string key) =>
        Assert.IsType<YamlScalarNode>(parent.Children[new YamlScalarNode(key)]).Value!;

    private static void AssertScalar(YamlMappingNode parent, string key, string expected) =>
        Assert.Equal(expected, Scalar(parent, key));

    private static void AssertRequiredVariable(
        YamlMappingNode parent,
        string key,
        string variable)
    {
        var value = Scalar(parent, key);
        Assert.StartsWith("${" + variable + ":?", value, StringComparison.Ordinal);
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));
}
