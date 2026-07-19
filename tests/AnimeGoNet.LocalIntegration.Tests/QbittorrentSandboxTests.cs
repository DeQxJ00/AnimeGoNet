using System.Diagnostics;
using System.Text.Json;
using AnimeGoNet.App.Downloads;
using AnimeGoNet.Core.Configuration;

namespace AnimeGoNet.LocalIntegration.Tests;

public sealed class QbittorrentSandboxTests
{
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task IsolatedQbittorrentVersionLoginListAndPaths()
    {
        Assert.Equal("1", Required("ANIMEGONET_QBIT_INTEGRATION"));
        var sandbox = Path.GetFullPath(Required("ANIMEGONET_QBIT_SANDBOX"));
        var executable = Path.GetFullPath(Required("ANIMEGONET_QBIT_EXE"));
        var profile = Path.GetFullPath(Required("ANIMEGONET_QBIT_PROFILE"));
        var downloadPath = Path.GetFullPath(Required("ANIMEGONET_QBIT_DOWNLOAD_PATH"));
        var savePath = Path.GetFullPath(Required("ANIMEGONET_QBIT_SAVE_PATH"));
        var dataPath = Path.GetFullPath(Required("ANIMEGONET_QBIT_DATA_PATH"));
        var baseUrl = new Uri(Required("ANIMEGONET_QBIT_BASE_URL"));

        Assert.StartsWith(
            sandbox,
            executable,
            StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(Path.GetDirectoryName(executable)!, profile, StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(downloadPath));
        Assert.True(Directory.Exists(savePath));
        Assert.True(Directory.Exists(dataPath));

        using var httpClient = new HttpClient(new HttpClientHandler { UseCookies = true })
        {
            BaseAddress = baseUrl,
            Timeout = TimeSpan.FromSeconds(10),
        };
        var client = new QbittorrentClient(
            httpClient,
            new QbittorrentInstanceOptions
            {
                BaseUrl = baseUrl,
                Username = Required("ANIMEGONET_QBIT_USERNAME"),
                Password = Required("ANIMEGONET_QBIT_PASSWORD"),
                DownloadPath = downloadPath,
            });

        await client.ConnectAsync();
        var existingTasks = await client.ListAsync();
        Assert.Empty(existingTasks);

        var apiVersion = (await httpClient.GetStringAsync("api/v2/app/version")).Trim().TrimStart('v');
        var fileVersion = FileVersionInfo.GetVersionInfo(executable).ProductVersion!.Trim().TrimStart('v');
        Assert.Equal(fileVersion, apiVersion);

        var configuredSavePath = (await httpClient.GetStringAsync("api/v2/app/defaultSavePath")).Trim();
        Assert.Equal(
            Path.TrimEndingDirectorySeparator(downloadPath),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(configuredSavePath)),
            ignoreCase: true);

        using var preferences = JsonDocument.Parse(
            await httpClient.GetStringAsync("api/v2/app/preferences"));
        var root = preferences.RootElement;
        Assert.False(root.GetProperty("bypass_local_auth").GetBoolean());
        Assert.True(root.GetProperty("temp_path_enabled").GetBoolean());
        Assert.Equal(
            Path.TrimEndingDirectorySeparator(downloadPath),
            Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(root.GetProperty("temp_path").GetString()!)),
            ignoreCase: true);
    }

    private static string Required(string name) =>
        Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"Run eng/qbittorrent-local-integration.ps1; missing {name}.");
}
