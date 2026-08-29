using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

namespace AnimeGoNet.App.Tests.Delivery;

public sealed partial class OperationsDocumentationContractTests
{
    private static readonly string[] DocumentPaths =
    [
        "README.md",
        "docs/USER_MIGRATION.md",
        "docs/PLUGIN_OPERATIONS.md",
        "docs/OPERATIONS.md",
    ];

    [Fact]
    public void ReadmeLinksEveryUserFacingOperationsGuide()
    {
        var root = RepositoryRoot();
        var readme = File.ReadAllText(Path.Combine(root, "README.md"));

        Assert.Contains("(docs/USER_MIGRATION.md)", readme, StringComparison.Ordinal);
        Assert.Contains("(docs/PLUGIN_OPERATIONS.md)", readme, StringComparison.Ordinal);
        Assert.Contains("(docs/OPERATIONS.md)", readme, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadmeProvidesAStandaloneAnimeGoNetComposeAndDocumentsRuntimeParameters()
    {
        var root = RepositoryRoot();
        var readme = File.ReadAllText(Path.Combine(root, "README.md"));
        var dockerSection = readme.IndexOf("## Docker", StringComparison.Ordinal);
        var composeStart = readme.IndexOf("```yaml", dockerSection, StringComparison.Ordinal);
        var composeEnd = readme.IndexOf("```", composeStart + "```yaml".Length, StringComparison.Ordinal);

        Assert.True(dockerSection >= 0 && composeStart > dockerSection && composeEnd > composeStart);
        var compose = readme[(composeStart + "```yaml".Length)..composeEnd].Trim();
        var parsed = new YamlStream();
        parsed.Load(new StringReader(compose));

        var rootMapping = Assert.IsType<YamlMappingNode>(Assert.Single(parsed.Documents).RootNode);
        var services = Assert.IsType<YamlMappingNode>(rootMapping.Children[new YamlScalarNode("services")]);
        var service = Assert.Single(services.Children);
        Assert.Equal("animegonet", Assert.IsType<YamlScalarNode>(service.Key).Value);
        Assert.DoesNotContain("qbittorrent:", compose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ghcr.io/deqxj00/animegonet:latest", compose, StringComparison.Ordinal);
        Assert.Contains(":/data", compose, StringComparison.Ordinal);
        Assert.Contains(":/download", compose, StringComparison.Ordinal);
        Assert.DoesNotContain("environment:", compose, StringComparison.Ordinal);
        Assert.DoesNotContain("security_opt:", compose, StringComparison.Ordinal);

        foreach (var parameter in new[]
                 {
                     "ANIMEGONET_IMAGE",
                     "ANIMEGONET_BIND_ADDRESS",
                     "ANIMEGONET_PORT",
                     "ANIMEGONET_DATA_ROOT",
                     "ANIMEGONET_DOWNLOAD_ROOT",
                     "downloaders__bt__base_url",
                     "outbound_proxy_hosts",
                 })
        {
            Assert.Contains($"`{parameter}`", readme, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ReadmeEmbedsNonEmptyRuntimeWebUiScreenshots()
    {
        var root = RepositoryRoot();
        var readme = File.ReadAllText(Path.Combine(root, "README.md"));
        var images = new[]
        {
            "webui-overview.jpg",
            "webui-library.jpg",
            "webui-tasks.jpg",
            "webui-matching-logs.jpg",
        };

        foreach (var image in images)
        {
            var relativePath = $"docs/images/{image}";
            var bytes = File.ReadAllBytes(Path.Combine(root, "docs", "images", image));

            Assert.Contains($"]({relativePath})", readme, StringComparison.Ordinal);
            Assert.True(bytes.Length > 50_000, $"Runtime screenshot is unexpectedly small: {relativePath}");
            Assert.Equal(new byte[] { 255, 216, 255 }, bytes[..3]);
        }
    }

    [Fact]
    public void MigrationGuideLocksBackupIsolationValidationAndRollbackBoundaries()
    {
        var text = Read("docs/USER_MIGRATION.md");

        Assert.Contains("--backup=true", text, StringComparison.Ordinal);
        Assert.Contains("--web=false", text, StringComparison.Ordinal);
        Assert.Contains("Transmission", text, StringComparison.Ordinal);
        Assert.Contains("AnimeGoNet.LegacyCacheImporter", text, StringComparison.Ordinal);
        Assert.Contains("GET /ping", text, StringComparison.Ordinal);
        Assert.Contains("GET /api/v1/status", text, StringComparison.Ordinal);
        Assert.Contains("恢复升级前数据库", text, StringComparison.Ordinal);
        Assert.Contains("Python/JavaScript 插件不会迁移或执行", text, StringComparison.Ordinal);
    }

    [Fact]
    public void PluginGuideLocksRidValidationIsolationAndRollbackBoundaries()
    {
        var text = Read("docs/PLUGIN_OPERATIONS.md");

        Assert.Contains("plugin.json", text, StringComparison.Ordinal);
        Assert.Contains("data_path/plugin-data/<plugin-id>", text, StringComparison.Ordinal);
        Assert.Contains("data_path/config/external-plugins.private.json", text, StringComparison.Ordinal);
        Assert.Contains("validate E:\\PluginStaging", text, StringComparison.Ordinal);
        Assert.Contains("run E:\\PluginStaging", text, StringComparison.Ordinal);
        Assert.Contains("pack E:\\PluginStaging", text, StringComparison.Ordinal);
        Assert.Contains("默认禁用", text, StringComparison.Ordinal);
        Assert.Contains("显式 reset", text, StringComparison.Ordinal);
        foreach (var rid in new[] { "win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-arm64" })
        {
            Assert.Contains(rid, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void OperationsGuideLocksHealthBackupDeleteAndDockerVerificationBoundary()
    {
        var text = Read("docs/OPERATIONS.md");

        Assert.Contains("GET /ping", text, StringComparison.Ordinal);
        Assert.Contains("GET /api/v1/status", text, StringComparison.Ordinal);
        Assert.Contains("GET /openapi/v1.json", text, StringComparison.Ordinal);
        Assert.Contains("PRAGMA quick_check;", text, StringComparison.Ordinal);
        Assert.Contains("schema_migrations", text, StringComparison.Ordinal);
        Assert.Contains("deleteFiles=false", text, StringComparison.Ordinal);
        Assert.Contains("未验证", text, StringComparison.Ordinal);
        Assert.Contains("Ubuntu 24.04", text, StringComparison.Ordinal);
        Assert.Contains("linux-arm64 镜像及原生 runner 仍", text, StringComparison.Ordinal);
        Assert.Contains("2026-08-11-ubuntu-ct-docker-validation.md", text, StringComparison.Ordinal);
    }

    [Fact]
    public void UserFacingDocumentLinksResolveAndDoNotReferenceLocalTestSecrets()
    {
        var root = RepositoryRoot();
        foreach (var relativeDocument in DocumentPaths)
        {
            var documentPath = Path.Combine(root, relativeDocument.Replace('/', Path.DirectorySeparatorChar));
            var text = File.ReadAllText(documentPath);
            Assert.DoesNotContain("TestSpace", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("192.168.", text, StringComparison.Ordinal);
            Assert.DoesNotContain("WorkSpaceAI", text, StringComparison.OrdinalIgnoreCase);

            foreach (Match match in MarkdownLinkRegex().Matches(text))
            {
                var target = Uri.UnescapeDataString(match.Groups[1].Value);
                if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                    || target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var targetPath = Path.GetFullPath(Path.Combine(
                    Path.GetDirectoryName(documentPath)!,
                    target.Replace('/', Path.DirectorySeparatorChar)));
                Assert.True(File.Exists(targetPath), $"Broken Markdown link in {relativeDocument}: {target}");
            }
        }
    }

    private static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));

    [GeneratedRegex(@"\[[^\]]+\]\(([^)#]+)(?:#[^)]*)?\)", RegexOptions.CultureInvariant)]
    private static partial Regex MarkdownLinkRegex();
}
