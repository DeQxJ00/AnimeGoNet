using System.Text;

namespace AnimeGoNet.App.Tests.Delivery;

public sealed class DockerQbittorrentIntegrationContractTests
{
    [Fact]
    public async Task BaseContainerSmokeForcesRuntimeHardeningAndCleanSigterm()
    {
        var root = RepositoryRoot();
        var dockerfile = await File.ReadAllTextAsync(Path.Combine(root, "Dockerfile.animegonet"));
        var compose = await File.ReadAllTextAsync(Path.Combine(root, "docker-compose.animegonet.yml"));
        var smoke = await File.ReadAllTextAsync(Path.Combine(root, "eng", "smoke-container.sh"));
        var workflow = await File.ReadAllTextAsync(Path.Combine(
            root,
            ".github",
            "workflows",
            "animegonet-docker.yml"));

        Assert.Contains("USER 10001:10001", dockerfile, StringComparison.Ordinal);
        Assert.Contains("user: \"${PUID:-1000}:${PGID:-1000}\"", compose, StringComparison.Ordinal);
        Assert.Contains("read_only: true", compose, StringComparison.Ordinal);
        Assert.Contains("- /tmp", compose, StringComparison.Ordinal);
        Assert.Contains("no-new-privileges:true", compose, StringComparison.Ordinal);

        Assert.Contains("--user \"$test_uid:$test_gid\"", smoke, StringComparison.Ordinal);
        Assert.Contains("--read-only", smoke, StringComparison.Ordinal);
        Assert.Contains("--tmpfs /tmp:rw,nosuid,nodev,noexec,size=64m", smoke, StringComparison.Ordinal);
        Assert.Contains("--security-opt no-new-privileges:true", smoke, StringComparison.Ordinal);
        Assert.Contains("{{.HostConfig.ReadonlyRootfs}}", smoke, StringComparison.Ordinal);
        Assert.Contains("{{json .HostConfig.Tmpfs}}", smoke, StringComparison.Ordinal);
        Assert.Contains("{{json .HostConfig.SecurityOpt}}", smoke, StringComparison.Ordinal);
        Assert.Contains("test \"$(id -u)\" -ne 0", smoke, StringComparison.Ordinal);
        Assert.Contains("touch /data/.animegonet-smoke-write", smoke, StringComparison.Ordinal);
        Assert.Contains("touch /download/.animegonet-smoke-write", smoke, StringComparison.Ordinal);
        Assert.Contains("touch /tmp/.animegonet-smoke-write", smoke, StringComparison.Ordinal);
        Assert.Contains("{{.State.Health.Status}}", smoke, StringComparison.Ordinal);
        Assert.Contains("docker stop --signal SIGTERM --time 7", smoke, StringComparison.Ordinal);
        Assert.Contains("{{.State.ExitCode}}", smoke, StringComparison.Ordinal);
        Assert.Contains("./eng/smoke-container.sh animegonet:ci", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("TestSpace", dockerfile + compose + smoke, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task IsolatedComposeAndSmokeCoverDualInstanceLifecycleWithoutPrivateInputs()
    {
        var root = RepositoryRoot();
        var compose = await File.ReadAllTextAsync(
            Path.Combine(root, "docker-compose.qbittorrent-integration.yml"));
        var smoke = await File.ReadAllTextAsync(
            Path.Combine(root, "eng", "smoke-qbittorrent-compose.sh"));
        var workflow = await File.ReadAllTextAsync(
            Path.Combine(root, ".github", "workflows", "animegonet-docker.yml"));

        Assert.Contains("qbittorrent-bt:", compose, StringComparison.Ordinal);
        Assert.Contains("qbittorrent-pt:", compose, StringComparison.Ordinal);
        Assert.Contains("127.0.0.1::8080", compose, StringComparison.Ordinal);
        Assert.Contains("127.0.0.1::7991", compose, StringComparison.Ordinal);
        Assert.Contains("${ANIMEGONET_INTEGRATION_ROOT:?", compose, StringComparison.Ordinal);
        Assert.Contains("/download:/download", compose, StringComparison.Ordinal);
        Assert.Contains("download_path: /download/incomplete", compose, StringComparison.Ordinal);
        Assert.Contains("save_path: /download/anime", compose, StringComparison.Ordinal);
        Assert.Contains("data_path: /data", compose, StringComparison.Ordinal);
        Assert.Contains("read_only: true", compose, StringComparison.Ordinal);
        Assert.Contains("no-new-privileges:true", compose, StringComparison.Ordinal);

        Assert.Contains("mktemp -d", smoke, StringComparison.Ordinal);
        Assert.Contains("compose down --volumes --remove-orphans", smoke, StringComparison.Ordinal);
        Assert.Contains("emporary password is provided for this session:", smoke, StringComparison.Ordinal);
        Assert.Contains("/api/v2/auth/login", smoke, StringComparison.Ordinal);
        Assert.Contains("/api/v2/app/setPreferences", smoke, StringComparison.Ordinal);
        Assert.Contains("compose restart", smoke, StringComparison.Ordinal);
        Assert.Contains("/api/v2/torrents/add", smoke, StringComparison.Ordinal);
        Assert.Contains("/api/v2/torrents/info", smoke, StringComparison.Ordinal);
        Assert.Contains("/api/v2/torrents/files", smoke, StringComparison.Ordinal);
        Assert.Contains("/api/v2/torrents/filePrio", smoke, StringComparison.Ordinal);
        Assert.Contains("/api/v2/torrents/start", smoke, StringComparison.Ordinal);
        Assert.Contains("/api/v2/torrents/stop", smoke, StringComparison.Ordinal);
        Assert.Contains("/api/v2/torrents/delete", smoke, StringComparison.Ordinal);
        Assert.Contains("deleteFiles=true", smoke, StringComparison.Ordinal);
        Assert.Contains("bcff48bafa9434c0062a4c2a45ed885f26701721", smoke, StringComparison.Ordinal);
        Assert.Contains("/api/v1/downloaders/$instance/test", smoke, StringComparison.Ordinal);
        Assert.Contains("/api/v1/downloaders/$instance/path-probe", smoke, StringComparison.Ordinal);
        Assert.Contains("\"downloader_id\":\"pt\"", smoke, StringComparison.Ordinal);
        Assert.Contains("\"downloader_id\"] == \"bt\"", smoke, StringComparison.Ordinal);
        Assert.Contains("background_workers_enabled: \"false\"", compose, StringComparison.Ordinal);
        Assert.Contains("./eng/smoke-qbittorrent-compose.sh animegonet:ci", workflow, StringComparison.Ordinal);

        Assert.DoesNotContain("passkey", compose, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("passkey", smoke, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TestSpace", compose + smoke, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FixtureIsAStableLoopbackOnlySingleFileTorrent()
    {
        var root = RepositoryRoot();
        var encoded = await File.ReadAllTextAsync(
            Path.Combine(root, "tests", "fixtures", "animegonet-ci.torrent.b64"));
        var bytes = Convert.FromBase64String(encoded.Trim());
        var torrent = Encoding.ASCII.GetString(bytes);

        Assert.Equal(
            "d8:announce27:http://127.0.0.1:9/announce4:info" +
            "d6:lengthi5e4:name17:animegonet-ci.bin12:piece lengthi16384e" +
            "6:pieces20:aaaaaaaaaaaaaaaaaaaaee",
            torrent);
        Assert.DoesNotContain("https://", torrent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("passkey", torrent, StringComparison.OrdinalIgnoreCase);
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
