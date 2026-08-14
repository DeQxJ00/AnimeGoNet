using System.Text;
using System.Security.Cryptography;
using AnimeGoNet.App.Torrents;

namespace AnimeGoNet.App.Tests.Delivery;

public sealed class DockerQbittorrentIntegrationContractTests
{
    [Fact]
    public async Task BaseContainerSmokeForcesRuntimeHardeningAndCleanSigterm()
    {
        var root = RepositoryRoot();
        var dockerfile = await File.ReadAllTextAsync(Path.Combine(root, "Dockerfile.animegonet"));
        var dockerignore = await File.ReadAllTextAsync(Path.Combine(root, ".dockerignore"));
        var compose = await File.ReadAllTextAsync(Path.Combine(root, "docker-compose.animegonet.yml"));
        var smoke = await File.ReadAllTextAsync(Path.Combine(root, "eng", "smoke-container.sh"));
        var workflow = await File.ReadAllTextAsync(Path.Combine(
            root,
            ".github",
            "workflows",
            "animegonet-docker.yml"));

        Assert.Contains("USER 10001:10001", dockerfile, StringComparison.Ordinal);
        Assert.Contains(
            "COPY src/AnimeGo.Plugin.Abstractions/AnimeGo.Plugin.Abstractions.csproj src/AnimeGo.Plugin.Abstractions/",
            dockerfile,
            StringComparison.Ordinal);
        Assert.Contains(
            "COPY docs/TMDB_AI_MATCH_PROMPT.md docs/TMDB_AI_MATCH_PROMPT.md",
            dockerfile,
            StringComparison.Ordinal);
        Assert.Contains(
            "COPY docs/TMDB_AI_MATCH_PROMPT_TESTER.md docs/TMDB_AI_MATCH_PROMPT_TESTER.md",
            dockerfile,
            StringComparison.Ordinal);
        Assert.Contains("!tests/AnimeGoNet.ContainerE2EFixture/**", dockerignore, StringComparison.Ordinal);
        Assert.Contains("user: \"${PUID:-1000}:${PGID:-1000}\"", compose, StringComparison.Ordinal);
        Assert.Contains("read_only: true", compose, StringComparison.Ordinal);
        Assert.Contains("- /tmp", compose, StringComparison.Ordinal);
        Assert.Contains("no-new-privileges:true", compose, StringComparison.Ordinal);
        Assert.Contains(
            "webui_access_key: ${ANIMEGONET_WEBUI_ACCESS_KEY:-}",
            compose,
            StringComparison.Ordinal);
        Assert.Contains(
            "inner_plugin_mikan__access_key: ${ANIMEGONET_ACCESS_KEY:?",
            compose,
            StringComparison.Ordinal);

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
        Assert.Contains("torrent-fixture:", compose, StringComparison.Ordinal);
        Assert.Contains("image: busybox:1.37", compose, StringComparison.Ordinal);
        Assert.Contains("user: \"65534:65534\"", compose, StringComparison.Ordinal);
        Assert.Contains("ipv4_address: 11.22.33.44", compose, StringComparison.Ordinal);
        Assert.Contains("- torrent-fixture.invalid", compose, StringComparison.Ordinal);
        Assert.Contains("- subnet: 11.22.33.0/24", compose, StringComparison.Ordinal);
        Assert.True(TorrentNetworkPolicy.IsPublicAddress(System.Net.IPAddress.Parse("11.22.33.44")));
        Assert.False(TorrentNetworkPolicy.IsPublicAddress(System.Net.IPAddress.Parse("172.20.0.44")));
        Assert.Contains("127.0.0.1::8080", compose, StringComparison.Ordinal);
        Assert.Contains("127.0.0.1::7991", compose, StringComparison.Ordinal);
        Assert.Contains("${ANIMEGONET_INTEGRATION_ROOT:?", compose, StringComparison.Ordinal);
        Assert.Contains("/download:/download", compose, StringComparison.Ordinal);
        Assert.Contains("download_path: /download/incomplete", compose, StringComparison.Ordinal);
        Assert.Contains("save_path: /download/anime", compose, StringComparison.Ordinal);
        Assert.Contains("data_path: /data", compose, StringComparison.Ordinal);
        Assert.Contains("read_only: true", compose, StringComparison.Ordinal);
        Assert.Contains("no-new-privileges:true", compose, StringComparison.Ordinal);
        Assert.Contains(
            "webui_access_key: ${ANIMEGONET_WEBUI_ACCESS_KEY:-}",
            compose,
            StringComparison.Ordinal);
        Assert.Contains(
            "inner_plugin_mikan__access_key: ${ANIMEGONET_ACCESS_KEY:?",
            compose,
            StringComparison.Ordinal);

        Assert.Contains("mktemp -d", smoke, StringComparison.Ordinal);
        Assert.Contains("compose down --volumes --remove-orphans", smoke, StringComparison.Ordinal);
        Assert.Contains("emporary password is provided for this session:", smoke, StringComparison.Ordinal);
        Assert.Contains("/api/v2/auth/login", smoke, StringComparison.Ordinal);
        Assert.Contains("--header \"Host: 127.0.0.1:8080\"", smoke, StringComparison.Ordinal);
        Assert.Contains("--header \"Referer: http://127.0.0.1:8080/\"", smoke, StringComparison.Ordinal);
        Assert.Contains("--header \"Origin: http://127.0.0.1:8080\"", smoke, StringComparison.Ordinal);
        Assert.Contains("/api/v2/app/setPreferences", smoke, StringComparison.Ordinal);
        Assert.Contains("login \"$base_url\" \"$temporary_password\" \"$cookie_jar\"", smoke, StringComparison.Ordinal);
        Assert.Contains("connection_cookie_jar()", smoke, StringComparison.Ordinal);
        Assert.Contains(
            "cookie_jar=\"$(connection_cookie_jar \"$connection\")\"",
            smoke,
            StringComparison.Ordinal);
        Assert.Contains(
            "$(connection_cookie_jar \"$bt_connection\")",
            smoke,
            StringComparison.Ordinal);
        Assert.Contains("bt_password=\"${bt_connection##*|}\"", smoke, StringComparison.Ordinal);
        Assert.Contains("\"password\": \"$bt_password\"", smoke, StringComparison.Ordinal);
        Assert.Contains("/api/v2/torrents/add", smoke, StringComparison.Ordinal);
        Assert.Contains("/api/v2/torrents/info", smoke, StringComparison.Ordinal);
        Assert.Contains("/api/v2/torrents/files", smoke, StringComparison.Ordinal);
        Assert.Contains("/api/v2/torrents/filePrio", smoke, StringComparison.Ordinal);
        Assert.Contains("/api/v2/torrents/start", smoke, StringComparison.Ordinal);
        Assert.Contains("/api/v2/torrents/stop", smoke, StringComparison.Ordinal);
        Assert.Contains("/api/v2/torrents/delete", smoke, StringComparison.Ordinal);
        Assert.Contains("deleteFiles=true", smoke, StringComparison.Ordinal);
        Assert.Contains("bcff48bafa9434c0062a4c2a45ed885f26701721", smoke, StringComparison.Ordinal);
        Assert.Contains("9356dbb012e7d8a6999badefacfc74dd1d00593e", smoke, StringComparison.Ordinal);
        Assert.Contains("/api/v1/downloaders/$instance/test", smoke, StringComparison.Ordinal);
        Assert.Contains("/api/v1/downloaders/$instance/path-probe", smoke, StringComparison.Ordinal);
        Assert.Contains("/api/v1/ingest", smoke, StringComparison.Ordinal);
        Assert.Contains("\"source\":\"mikan-ci\"", smoke, StringComparison.Ordinal);
        Assert.Contains("\"source\":\"u2-ci\"", smoke, StringComparison.Ordinal);
        Assert.Contains("wait_for_routed_task", smoke, StringComparison.Ordinal);
        Assert.Contains(
            "ready = len(items) == 1 and items[0][\"state\"].lower().startswith((\"stopped\", \"paused\"))",
            smoke,
            StringComparison.Ordinal);
        Assert.Contains("other_connection", smoke, StringComparison.Ordinal);
        Assert.Contains("cleanup_routed_task", smoke, StringComparison.Ordinal);
        Assert.Contains("\"downloader_id\":\"pt\"", smoke, StringComparison.Ordinal);
        Assert.Contains("\"downloader_id\"] == \"bt\"", smoke, StringComparison.Ordinal);
        Assert.Contains("background_workers_enabled: \"true\"", compose, StringComparison.Ordinal);
        Assert.Contains(
            "mikan_base_url: http://container-e2e-fixture.invalid:8089/",
            compose,
            StringComparison.Ordinal);
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
        Assert.Equal("bcff48bafa9434c0062a4c2a45ed885f26701721", InfoHash(bytes));
        Assert.DoesNotContain("https://", torrent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("passkey", torrent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PtFixtureHasASeparateStableInfoHashAndNoPrivateTracker()
    {
        var root = RepositoryRoot();
        var encoded = await File.ReadAllTextAsync(
            Path.Combine(root, "tests", "fixtures", "animegonet-ci-pt.torrent.b64"));
        var bytes = Convert.FromBase64String(encoded.Trim());
        var torrent = Encoding.ASCII.GetString(bytes);

        Assert.Equal(
            "d8:announce27:http://127.0.0.1:9/announce4:info" +
            "d6:lengthi7e4:name20:animegonet-ci-pt.bin12:piece lengthi16384e" +
            "6:pieces20:bbbbbbbbbbbbbbbbbbbbee",
            torrent);
        Assert.Equal("9356dbb012e7d8a6999badefacfc74dd1d00593e", InfoHash(bytes));
        Assert.DoesNotContain("https://", torrent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("passkey", torrent, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(
            await File.ReadAllTextAsync(
                Path.Combine(root, "tests", "fixtures", "animegonet-ci.torrent.b64")),
            encoded);
    }

    private static string InfoHash(byte[] torrent)
    {
        var marker = Encoding.ASCII.GetBytes("4:info");
        var markerIndex = torrent.AsSpan().IndexOf(marker);
        Assert.True(markerIndex >= 0);
        var infoStart = markerIndex + marker.Length;
        var infoDictionary = torrent.AsSpan(infoStart, torrent.Length - infoStart - 1);
#pragma warning disable CA5350 // BitTorrent v1 mandates SHA-1 over the original bencoded info bytes.
        return Convert.ToHexStringLower(SHA1.HashData(infoDictionary));
#pragma warning restore CA5350
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
