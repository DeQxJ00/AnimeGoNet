using AnimeGoNet.App.Configuration;
using AnimeGoNet.Core.Configuration;

namespace AnimeGoNet.App.Tests.Configuration;

public sealed class DownloaderDeploymentLocksTests
{
    [Fact]
    public void SourcesAreCanonicalPerInstanceAndNeverContainValues()
    {
        var locks = DownloaderDeploymentLocks.FromSources(
        [
            "downloaders__BT__base_url",
            "DOWNLOADERS:bt:PASSWORD",
            "ANIMEGO_CLIENT_USERNAME",
            "DOWNLOADERS__archive__enabled",
            "DOWNLOADERS__invalid id__password",
            "DOWNLOADERS__bt__unknown",
        ],
        [
            "--downloaders:bt:base_url=https://secret.invalid",
            "--downloaders:pt:download_path=/secret/path",
            "--ANIMEGO_CLIENT_PASSWORD=private-command-line-value",
            "--unrelated=value",
        ]);

        Assert.True(locks.IsLocked("BT", "BASE_URL"));
        Assert.True(locks.IsLocked("bt", "username"));
        Assert.True(locks.IsLocked("archive", "enabled"));
        Assert.True(locks.IsLocked("pt", "download_path"));
        Assert.False(locks.IsLocked("pt", "password"));
        Assert.False(locks.IsLocked("bt", "unknown"));

        var baseUrl = Assert.Single(
            locks.Items,
            item => item.DownloaderId == "bt" && item.Field == "base_url");
        Assert.Equal("environment_and_command_line", baseUrl.Source);
        Assert.Equal(["downloaders__BT__base_url"], baseUrl.EnvironmentVariables);
        Assert.Equal(["--downloaders:bt:base_url"], baseUrl.CommandLineArguments);
        Assert.DoesNotContain(
            locks.Items.SelectMany(item =>
                item.EnvironmentVariables.Concat(item.CommandLineArguments)),
            key => key.Contains("secret", StringComparison.OrdinalIgnoreCase)
                || key.Contains("/secret/path", StringComparison.Ordinal));

        var legacyPassword = Assert.Single(
            locks.Items,
            item => item.DownloaderId == "bt" && item.Field == "password");
        Assert.Equal("environment_and_command_line", legacyPassword.Source);
        Assert.Contains("DOWNLOADERS:bt:PASSWORD", legacyPassword.EnvironmentVariables);
        Assert.Contains("--ANIMEGO_CLIENT_PASSWORD", legacyPassword.CommandLineArguments);
    }

    [Fact]
    public void ReapplyRestoresOnlyLockedFieldsAndKeepsInstancesIndependent()
    {
        var defaults = AnimeGoDefaults.CreateNative(Path.GetTempPath());
        var deploymentBt = defaults.Downloaders["bt"] with
        {
            BaseUrl = new Uri("http://environment.invalid:8080"),
            Username = "environment-user",
            Password = "environment-password",
            DownloadPath = Path.Combine(Path.GetTempPath(), "environment-download"),
            Enabled = true,
        };
        var deploymentPt = defaults.Downloaders["pt"] with
        {
            BaseUrl = new Uri("http://pt-environment.invalid:8080"),
            Password = "pt-environment-password",
        };
        var deployment = defaults with
        {
            Downloaders = new Dictionary<string, QbittorrentInstanceOptions>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["bt"] = deploymentBt,
                ["pt"] = deploymentPt,
            },
        };
        var candidate = deployment with
        {
            Downloaders = new Dictionary<string, QbittorrentInstanceOptions>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["bt"] = deploymentBt with
                {
                    BaseUrl = new Uri("http://private.invalid:9090"),
                    Username = "private-user",
                    Password = "private-password",
                    DownloadPath = Path.Combine(Path.GetTempPath(), "private-download"),
                    Enabled = false,
                },
                ["pt"] = deploymentPt with
                {
                    BaseUrl = new Uri("http://pt-private.invalid:9090"),
                    Password = "pt-private-password",
                },
            },
        };
        var locks = DownloaderDeploymentLocks.FromSources(
        [
            "DOWNLOADERS__bt__base_url",
            "DOWNLOADERS__bt__password",
        ],
        [
            "--downloaders:bt:enabled=false",
            "--downloaders:pt:download_path=/downloads/pt",
        ]);

        var result = locks.Reapply(deployment, candidate);

        Assert.Equal(deploymentBt.BaseUrl, result.Downloaders["bt"].BaseUrl);
        Assert.Equal("environment-password", result.Downloaders["bt"].Password);
        Assert.True(result.Downloaders["bt"].Enabled);
        Assert.Equal("private-user", result.Downloaders["bt"].Username);
        Assert.Equal(
            Path.Combine(Path.GetTempPath(), "private-download"),
            result.Downloaders["bt"].DownloadPath);
        Assert.Equal(
            new Uri("http://pt-private.invalid:9090"),
            result.Downloaders["pt"].BaseUrl);
        Assert.Equal("pt-private-password", result.Downloaders["pt"].Password);
    }
}
