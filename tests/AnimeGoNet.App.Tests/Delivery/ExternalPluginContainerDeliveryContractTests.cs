namespace AnimeGoNet.App.Tests.Delivery;

public sealed class ExternalPluginContainerDeliveryContractTests
{
    [Fact]
    public void NativeFixtureIsCompiledFromTheSdkAndAuditsItsContainerBoundary()
    {
        var root = RepositoryRoot();
        var solution = Read(root, "AnimeGoNet.slnx");
        var project = Read(
            root,
            "tests/AnimeGoNet.ContainerPluginFixture/AnimeGoNet.ContainerPluginFixture.csproj");
        var program = Read(root, "tests/AnimeGoNet.ContainerPluginFixture/Program.cs");
        var handler = Read(
            root,
            "tests/AnimeGoNet.ContainerPluginFixture/ContainerSourcePlugin.cs");
        var manifest = Read(
            root,
            "tests/AnimeGoNet.ContainerPluginFixture/plugin.json");

        Assert.Contains("AnimeGoNet.ContainerPluginFixture.csproj", solution, StringComparison.Ordinal);
        Assert.Contains("<PublishAot>true</PublishAot>", project, StringComparison.Ordinal);
        Assert.Contains("AnimeGo.Plugin.Sdk.csproj", project, StringComparison.Ordinal);
        Assert.Contains("AnimeGoPluginHost.RunSourceAsync", program, StringComparison.Ordinal);
        Assert.Contains("com.animegonet.container-source", program, StringComparison.Ordinal);
        Assert.Contains("ANIMEGO_PLUGIN_DATA_PATH", handler, StringComparison.Ordinal);
        Assert.Contains(".container-write-probe", handler, StringComparison.Ordinal);
        Assert.Contains("package_directory_writable", handler, StringComparison.Ordinal);
        Assert.Contains("UnixIdentity.GetEffectiveUserId()", handler, StringComparison.Ordinal);
        Assert.Contains("package_read_only=true", handler, StringComparison.Ordinal);
        Assert.Contains("\"type\": \"source\"", manifest, StringComparison.Ordinal);
        Assert.Contains("\"rid\": \"__RID__\"", manifest, StringComparison.Ordinal);

        Assert.DoesNotContain("Python", project + program + handler, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TestSpace", project + program + handler, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DockerExportAndComposeKeepThePackageReadOnlyAndPluginDataWritable()
    {
        var root = RepositoryRoot();
        var dockerfile = Read(root, "Dockerfile.external-plugin-fixture");
        var export = Read(root, "eng/export-external-plugin-fixture.sh");
        var compose = Read(root, "docker-compose.qbittorrent-integration.yml");
        var workflow = Read(root, ".github/workflows/animegonet-docker.yml");

        Assert.Contains("amd64) rid=linux-x64", dockerfile, StringComparison.Ordinal);
        Assert.Contains("arm64) rid=linux-arm64", dockerfile, StringComparison.Ordinal);
        Assert.Contains("-p:PublishAot=true", dockerfile, StringComparison.Ordinal);
        Assert.Contains("FROM scratch", dockerfile, StringComparison.Ordinal);
        Assert.Contains("find /out -type f -exec chmod 0444", dockerfile, StringComparison.Ordinal);
        Assert.Contains("chmod 0555 /out/AnimeGoNet.ContainerPluginFixture", dockerfile, StringComparison.Ordinal);

        Assert.Contains("if [[ -e \"$output_root\" ]]", export, StringComparison.Ordinal);
        Assert.Contains("docker create \"$image\"", export, StringComparison.Ordinal);
        Assert.Contains("docker cp \"$container_id:/plugin/.\"", export, StringComparison.Ordinal);
        Assert.Contains("find \"$package_root\" -type l", export, StringComparison.Ordinal);
        Assert.Contains("docker image rm --force \"$image\"", export, StringComparison.Ordinal);
        Assert.Contains("manifest[\"rid\"] == expected_rid", export, StringComparison.Ordinal);

        Assert.Contains(
            "/animegonet/data/plugins/com.animegonet.container-source:" +
            "/data/plugins/com.animegonet.container-source:ro",
            compose,
            StringComparison.Ordinal);
        Assert.Contains("- ${ANIMEGONET_INTEGRATION_ROOT}/animegonet/data:/data", compose, StringComparison.Ordinal);
        Assert.Contains("external plugin isolation", workflow, StringComparison.Ordinal);

        Assert.DoesNotContain("passkey", dockerfile + export + compose, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TestSpace", dockerfile + export + compose, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ContainerSmokeExecutesThenDisablesTheExternalSourceWithoutNetworkAccess()
    {
        var root = RepositoryRoot();
        var smoke = Read(root, "eng/smoke-qbittorrent-compose.sh");

        Assert.Contains("export-external-plugin-fixture.sh", smoke, StringComparison.Ordinal);
        Assert.Contains("exercise_external_plugin", smoke, StringComparison.Ordinal);
        Assert.Contains("/api/v1/plugins/$plugin_id/configuration", smoke, StringComparison.Ordinal);
        Assert.Contains("\"adapter\":\"com.animegonet.container-source\"", smoke, StringComparison.Ordinal);
        Assert.Contains("\"source\":\"container-source-ci\"", smoke, StringComparison.Ordinal);
        Assert.Contains("HostNotAllowed", smoke, StringComparison.Ordinal);
        Assert.Contains("uid=$test_uid", smoke, StringComparison.Ordinal);
        Assert.Contains("package_read_only=true", smoke, StringComparison.Ordinal);
        Assert.Contains("runtime[\"state\"] == \"ready\"", smoke, StringComparison.Ordinal);
        Assert.Contains("\"expected_revision\":1,\"enabled\":false", smoke, StringComparison.Ordinal);
        Assert.Contains("runtime[\"state\"] == \"stopped\"", smoke, StringComparison.Ordinal);
        Assert.Contains("test ! -e \"$marker\"", smoke, StringComparison.Ordinal);

        Assert.DoesNotContain("TestSpace", smoke, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("personal-passkey", smoke, StringComparison.OrdinalIgnoreCase);
    }

    private static string Read(string root, string relativePath) =>
        File.ReadAllText(Path.Combine(
            root,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));
}
