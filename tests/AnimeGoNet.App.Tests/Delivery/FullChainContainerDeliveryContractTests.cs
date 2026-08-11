namespace AnimeGoNet.App.Tests.Delivery;

public sealed class FullChainContainerDeliveryContractTests
{
    [Fact]
    public async Task FixtureImageAndComposeDeclareIsolatedCrossArchitectureMetadataGraph()
    {
        var root = RepositoryRoot();
        var dockerfile = await ReadAsync(root, "Dockerfile.container-e2e-fixture");
        var compose = await ReadAsync(root, "docker-compose.qbittorrent-integration.yml");
        var fixture = await ReadAsync(
            root,
            "tests/AnimeGoNet.ContainerE2EFixture/Program.cs");

        Assert.Contains("amd64) rid=linux-x64", dockerfile, StringComparison.Ordinal);
        Assert.Contains("arm64) rid=linux-arm64", dockerfile, StringComparison.Ordinal);
        Assert.Contains("-p:PublishAot=true", dockerfile, StringComparison.Ordinal);
        Assert.Contains("USER 65534:65534", dockerfile, StringComparison.Ordinal);
        Assert.Contains("container-e2e-fixture:", compose, StringComparison.Ordinal);
        Assert.Contains("user: \"65534:65534\"", compose, StringComparison.Ordinal);
        Assert.Contains("read_only: true", compose, StringComparison.Ordinal);
        Assert.Contains("ipv4_address: 11.22.33.45", compose, StringComparison.Ordinal);
        Assert.Contains("- container-e2e-fixture.invalid", compose, StringComparison.Ordinal);
        Assert.Contains(
            "tmdb_base_url: http://container-e2e-fixture.invalid:8089/tmdb/",
            compose,
            StringComparison.Ordinal);
        Assert.Contains(
            "bangumi_base_url: http://container-e2e-fixture.invalid:8089/bangumi/",
            compose,
            StringComparison.Ordinal);
        Assert.Contains("tmdb_retry_count: \"0\"", compose, StringComparison.Ordinal);
        Assert.Contains("bangumi_retry_count: \"0\"", compose, StringComparison.Ordinal);

        Assert.Contains("/animegonet-container-e2e.torrent", fixture, StringComparison.Ordinal);
        Assert.Contains("/animegonet-route-smoke.torrent", fixture, StringComparison.Ordinal);
        Assert.Contains("/route-ready", fixture, StringComparison.Ordinal);
        Assert.Contains("/payload/{FileName}", fixture, StringComparison.Ordinal);
        Assert.Contains("/tmdb/3/discover/tv", fixture, StringComparison.Ordinal);
        Assert.Contains("/tmdb/3/tv/990001/season/1/episode/1", fixture, StringComparison.Ordinal);
        Assert.Contains("/bangumi/v0/subjects/990001", fixture, StringComparison.Ordinal);
        Assert.Contains("/bangumi/v0/episodes", fixture, StringComparison.Ordinal);
        Assert.Contains("/ready", fixture, StringComparison.Ordinal);
        Assert.Contains("/__state", fixture, StringComparison.Ordinal);
        Assert.DoesNotContain("TestSpace", dockerfile + compose + fixture, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SmokeGeneratesRealUnifiedDownloadMetadataOrganizationAndCleanupGate()
    {
        var root = RepositoryRoot();
        var smoke = await ReadAsync(root, "eng/smoke-qbittorrent-compose.sh");

        Assert.Contains("Dockerfile.container-e2e-fixture", smoke, StringComparison.Ordinal);
        Assert.Contains("--build-arg TARGETARCH=amd64", smoke, StringComparison.Ordinal);
        Assert.Contains("exercise_full_chain_e2e", smoke, StringComparison.Ordinal);
        Assert.Contains("reset_animegonet_state_for_full_chain", smoke, StringComparison.Ordinal);
        Assert.Contains("compose stop --timeout 10 animegonet", smoke, StringComparison.Ordinal);
        Assert.Contains("animegonet.db-wal", smoke, StringComparison.Ordinal);
        Assert.Contains("\"source\":\"container-e2e-ci\"", smoke, StringComparison.Ordinal);
        Assert.Contains("/animegonet-container-e2e.torrent", smoke, StringComparison.Ordinal);
        Assert.Contains("/api/v1/ingest", smoke, StringComparison.Ordinal);
        Assert.Contains("business_status\"] == \"organized\"", smoke, StringComparison.Ordinal);
        Assert.Contains("/api/v1/metadata/tasks/$task_id", smoke, StringComparison.Ordinal);
        Assert.Contains("series_strategy\"] == \"tmdb_title\"", smoke, StringComparison.Ordinal);
        Assert.Contains("season_strategy\"] == \"tmdb_air_date\"", smoke, StringComparison.Ordinal);
        Assert.Contains(
            "episode_strategy\"] == \"tmdb_episode_bangumi_date\"",
            smoke,
            StringComparison.Ordinal);
        Assert.Contains("/api/v1/library/seasons", smoke, StringComparison.Ordinal);
        Assert.Contains("episode_downloaded\"] == 1", smoke, StringComparison.Ordinal);
        Assert.Contains("AnimeGoNet Container E2E/S01/E001.mkv", smoke, StringComparison.Ordinal);
        Assert.Contains("tvshow.nfo", smoke, StringComparison.Ordinal);
        Assert.Contains("anime.a_json", smoke, StringComparison.Ordinal);
        Assert.Contains("anime.s_json", smoke, StringComparison.Ordinal);
        Assert.Contains("E001.e_json", smoke, StringComparison.Ordinal);
        Assert.Contains("payload_requests", smoke, StringComparison.Ordinal);
        Assert.Contains("tmdb_credential_failures", smoke, StringComparison.Ordinal);
        Assert.Contains("/api/v2/torrents/info?hashes=$expected_hash", smoke, StringComparison.Ordinal);
        Assert.Contains("/api/v2/torrents/deleteTags", smoke, StringComparison.Ordinal);
        Assert.Contains("/api/v2/torrents/removeCategories", smoke, StringComparison.Ordinal);
        Assert.Contains("docker image rm --force \"$container_e2e_fixture_image\"", smoke, StringComparison.Ordinal);
        Assert.DoesNotContain("TestSpace", smoke, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WorkflowWiresFullChainWebUiAssertionsWithoutClaimingLocalDockerExecution()
    {
        var root = RepositoryRoot();
        var package = await ReadAsync(root, "package.json");
        var spec = await ReadAsync(root, "tests/web-e2e/full-chain-container.spec.mjs");
        var workflow = await ReadAsync(root, ".github/workflows/animegonet-docker.yml");

        Assert.Contains("web:e2e:full-chain", package, StringComparison.Ordinal);
        Assert.Contains(
            "mcr.microsoft.com/playwright:v1.62.0-noble",
            await ReadAsync(root, "eng/smoke-qbittorrent-compose.sh"),
            StringComparison.Ordinal);
        Assert.Contains("ANIMEGONET_FULL_CHAIN_TASK_ID", spec, StringComparison.Ordinal);
        Assert.Contains("business_status: \"organized\"", spec, StringComparison.Ordinal);
        Assert.Contains("series_strategy: \"tmdb_title\"", spec, StringComparison.Ordinal);
        Assert.Contains("season_strategy: \"tmdb_air_date\"", spec, StringComparison.Ordinal);
        Assert.Contains(
            "episode_strategy: \"tmdb_episode_bangumi_date\"",
            spec,
            StringComparison.Ordinal);
        Assert.Contains("#downloads", spec, StringComparison.Ordinal);
        Assert.Contains("#metadata-tasks", spec, StringComparison.Ordinal);
        Assert.Contains("#library-list", spec, StringComparison.Ordinal);
        Assert.Contains("browserErrors", spec, StringComparison.Ordinal);
        Assert.Contains("ANIMEGONET_FULL_CHAIN_WEBUI: \"1\"", workflow, StringComparison.Ordinal);
        Assert.Contains("./eng/smoke-qbittorrent-compose.sh animegonet:ci", workflow, StringComparison.Ordinal);
    }

    private static Task<string> ReadAsync(string root, string relativePath) =>
        File.ReadAllTextAsync(Path.Combine(
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
