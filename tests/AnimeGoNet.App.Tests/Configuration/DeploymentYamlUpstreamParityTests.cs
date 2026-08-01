using System.Security.Cryptography;
using AnimeGoNet.App.Configuration;
using AnimeGoNet.Core.Configuration;

namespace AnimeGoNet.App.Tests.Configuration;

public sealed class DeploymentYamlUpstreamParityTests
{
    private const string UpstreamCommit =
        "c7475dfc55a374cd0dd08821bf17125dab1e3145";

    [Theory]
    [InlineData("animego_110.yaml", "25b35f85ca2ad21874c63323b7997024c1b27b250e3940c6a43c38b152893206")]
    [InlineData("animego_120.yaml", "a03ec464c77d1b7fd40c43c569189a282478d811f751115de68e9fcc0dc7ce84")]
    [InlineData("animego_130.yaml", "1fe482061d89357e26e66c33faded8960e6e85fdd1e9146d94f69fcff0860839")]
    [InlineData("animego_140.yaml", "116e8622716129946af595874da6b324d29c1ffb1cce985ad1505b9a0f1c50f0")]
    [InlineData("animego_141.yaml", "0c7f30dc30da1ba8809fb73488f369ab76f768dd5969ad9c32eb4882b60eb547")]
    [InlineData("animego_150.yaml", "69e4f9f446fec19b50d3113ccc06a5927649550baf26569ce410f3eb27c26723")]
    [InlineData("animego_151.yaml", "868a21b9b7fef36b834e56a760171975fe2fc26da5a1aee6f889665d275f6c5c")]
    [InlineData("animego_152.yaml", "b95d8ae33ee869e5610b320cfe6f48bdf5a649e5ef23ce428b952911ea171240")]
    [InlineData("animego_160.yaml", "65920e50c11a34dfbfb22dae9cfa23279c93cac30e5c8646f8d2a99dea0b9ce0")]
    [InlineData("animego_161.yaml", "8031a0cec4858c439bc7bb95db7a3f24079997d086956d98097e777c5beffc33")]
    [InlineData("animego_162.yaml", "bdd40029f9ea547237e40c787ac7812b597d623f53ff6cd4a7bc21933fb8f62d")]
    [InlineData("animego_170.yaml", "a4b98464153ffd2680d54b2fac962c7224fff3206b40044277eb2cc4c6ce5104")]
    public async Task PinnedHistoricalYamlMigratesToCanonicalOwnedFields(
        string fileName,
        string expectedSha256)
    {
        var upstream = Environment.GetEnvironmentVariable("ANIMEGO_UPSTREAM_REPO");
        if (string.IsNullOrWhiteSpace(upstream))
        {
            return;
        }

        var fixturePath = Path.Combine(
            Path.GetFullPath(upstream),
            "test",
            "testdata",
            "config",
            fileName);
        Assert.True(
            File.Exists(fixturePath),
            $"Pinned AnimeGo fixture {fileName} for {UpstreamCommit} is missing.");

        var original = await File.ReadAllBytesAsync(fixturePath);
        Assert.Equal(expectedSha256, Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant());

        var root = CreateRoot();
        try
        {
            var activePath = Path.Combine(root, "animego.yaml");
            await File.WriteAllBytesAsync(activePath, original);

            var snapshot = await DeploymentYamlConfiguration.LoadOrCreateAsync(
                activePath,
                AnimeGoDefaults.CreateNative(root),
                backupLegacy: false);

            Assert.True(snapshot.Upgraded);
            Assert.False(snapshot.LegacyLayout);
            Assert.Null(snapshot.BackupFilePath);
            Assert.Equal(DeploymentYamlConfiguration.CurrentVersion, snapshot.Version);
            Assert.Equal("data", snapshot.Values["paths:data_path"]);
            Assert.Equal("download/incomplete", snapshot.Values["paths:download_path"]);
            Assert.Equal("download/anime", snapshot.Values["paths:save_path"]);
            Assert.Equal("localhost", snapshot.Values["web:host"]);
            Assert.Equal("7991", snapshot.Values["web:port"]);
            Assert.Equal("qbittorrent", snapshot.Values["downloaders:bt:type"]);
            Assert.Equal("http://127.0.0.1:8080", snapshot.Values["downloaders:bt:base_url"]);
            Assert.Equal("download/incomplete", snapshot.Values["downloaders:bt:download_path"]);
            Assert.Equal("link_delete", snapshot.Values["sources:mikan:file_strategy"]);
            Assert.Equal("AnimeGo", snapshot.Values["sources:mikan:category"]);
            Assert.Equal("{year}年{quarter}月新番", snapshot.Values["sources:mikan:dynamic_tag_template"]);
            Assert.Equal("1", snapshot.Values["sources:mikan:seeding_time_minutes"]);
            Assert.Equal("animego123", snapshot.Values["web:access_key"]);
            Assert.Equal("5", snapshot.Values["metadata:tmdb:timeout_seconds"]);
            Assert.Equal("3", snapshot.Values["metadata:tmdb:retry_count"]);
            Assert.Equal("5", snapshot.Values["metadata:tmdb:retry_wait_seconds"]);
            Assert.Equal("5", snapshot.Values["metadata:bangumi:timeout_seconds"]);
            Assert.Equal("3", snapshot.Values["metadata:bangumi:retry_count"]);
            Assert.Equal("5", snapshot.Values["metadata:bangumi:retry_wait_seconds"]);
            Assert.Equal("0 0 6 * * *", snapshot.Values["schedule:refresh_database_cron"]);

            var canonical = await File.ReadAllTextAsync(activePath);
            Assert.Contains("version: 1.7.1", canonical, StringComparison.Ordinal);
            Assert.DoesNotContain("\nsetting:", canonical, StringComparison.Ordinal);
            Assert.DoesNotContain("\nplugin:", canonical, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("1.1.1")]
    [InlineData("1.3.1")]
    [InlineData("1.6.3")]
    [InlineData("1.7")]
    public async Task VersionInsideNumericRangeButAbsentUpstreamIsRejected(
        string version)
    {
        var root = CreateRoot();
        try
        {
            var path = Path.Combine(root, "animego.yaml");
            var original = $"version: {version}\nsetting:\n  data_path: should-stay\n";
            await File.WriteAllTextAsync(path, original);

            var exception = await Assert.ThrowsAsync<DeploymentYamlException>(
                () => DeploymentYamlConfiguration.LoadOrCreateAsync(
                    path,
                    AnimeGoDefaults.CreateNative(root)));

            Assert.Contains("recognized AnimeGo version", exception.Message, StringComparison.Ordinal);
            Assert.Equal(original, await File.ReadAllTextAsync(path));
            Assert.Empty(Directory.GetFiles(root, "animego-*.yaml"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "animegonet-yaml-upstream-parity",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
