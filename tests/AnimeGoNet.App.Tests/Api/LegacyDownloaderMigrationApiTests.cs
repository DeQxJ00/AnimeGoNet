using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AnimeGoNet.App.Configuration;
using AnimeGoNet.App.Torrents;
using AnimeGoNet.Core.Downloads;

namespace AnimeGoNet.App.Tests.Api;

public sealed class LegacyDownloaderMigrationApiTests
{
    [Fact]
    public async Task UnsupportedTransmissionKeepsWebAvailableAndFailsClosed()
    {
        var state = new LegacyDownloaderMigrationState(
        [
            new LegacyConfigurationDiagnostic(
                "UnsupportedDownloaderType",
                "legacy_yaml",
                "Transmission",
                "Legacy Transmission is unsupported; configure qBittorrent explicitly.",
                BlocksDownloads: true),
        ]);
        var transport = new MustNotBeUsedTransport();
        await using var app = await RunningApp.StartAsync(
            downloadClientRegistry: new MustNotBeUsedRegistry(),
            rssHttpTransport: transport,
            legacyDownloaderMigrationState: state,
            startBackgroundWorkers: true);

        using var configuration = JsonDocument.Parse(
            await app.Client.GetStreamAsync("/api/v1/config"));
        Assert.True(configuration.RootElement.GetProperty("downloads_blocked").GetBoolean());
        Assert.False(
            configuration.RootElement
                .GetProperty("deployment")
                .GetProperty("background_workers_enabled")
                .GetBoolean());
        var diagnostic = Assert.Single(
            configuration.RootElement
                .GetProperty("migration_diagnostics")
                .EnumerateArray());
        Assert.Equal("UnsupportedDownloaderType", diagnostic.GetProperty("code").GetString());
        Assert.Equal("Transmission", diagnostic.GetProperty("legacy_downloader_type").GetString());

        using var status = JsonDocument.Parse(await app.Client.GetStreamAsync("/api/v1/status"));
        Assert.True(status.RootElement.GetProperty("downloads_blocked").GetBoolean());
        Assert.False(
            status.RootElement
                .GetProperty("capabilities")
                .GetProperty("unified_ingest")
                .GetBoolean());
        Assert.False(
            status.RootElement
                .GetProperty("capabilities")
                .GetProperty("qbittorrent")
                .GetBoolean());

        using var downloaders = JsonDocument.Parse(
            await app.Client.GetStreamAsync("/api/v1/downloaders"));
        Assert.True(downloaders.RootElement.GetProperty("downloads_blocked").GetBoolean());
        Assert.All(
            downloaders.RootElement.GetProperty("items").EnumerateArray(),
            item =>
            {
                Assert.False(item.GetProperty("enabled").GetBoolean());
                Assert.Equal(
                    "blocked_by_legacy_migration",
                    item.GetProperty("configuration_source").GetString());
            });

        using var connection = await app.Client.PostAsync("/api/v1/downloaders/bt/test", null);
        await AssertBlocked(connection);
        using var path = await app.Client.PostAsync("/api/v1/downloaders/bt/path-probe", null);
        await AssertBlocked(path);

        using var ingest = await app.Client.PostAsJsonAsync("/api/v1/ingest", new
        {
            source = "mikan",
            data = new[]
            {
                new
                {
                    torrent = "https://mikanani.me/Home/Episode/fixture",
                    info = new { title = "Blocked fixture", mikanid = 3951, bgmid = 547888 },
                },
            },
        });
        Assert.Equal(HttpStatusCode.OK, ingest.StatusCode);
        using var ingestBody = JsonDocument.Parse(await ingest.Content.ReadAsStreamAsync());
        Assert.Equal(0, ingestBody.RootElement.GetProperty("accepted_count").GetInt32());
        Assert.Equal(1, ingestBody.RootElement.GetProperty("rejected_count").GetInt32());
        Assert.Contains(
            "UnsupportedDownloaderType",
            ingestBody.RootElement
                .GetProperty("items")[0]
                .GetProperty("errors")[0]
                .GetString(),
            StringComparison.Ordinal);

        using var rss = await app.Client.PostAsJsonAsync("/api/v1/rss/ingest", new
        {
            source_profile_id = "mikan",
            url = "https://mikanani.me/RSS/Bangumi?bangumiId=3951",
        });
        await AssertBlocked(rss);
        using var legacyRss = await app.Client.PostAsJsonAsync("/api/rss", new
        {
            source = "mikan",
            rss = new { url = "https://mikanani.me/RSS/Bangumi?bangumiId=3951" },
            is_select_ep = false,
            ep_links = Array.Empty<string>(),
        });
        Assert.Equal(HttpStatusCode.OK, legacyRss.StatusCode);
        using var legacyRssBody = JsonDocument.Parse(
            await legacyRss.Content.ReadAsStreamAsync());
        Assert.Equal(300, legacyRssBody.RootElement.GetProperty("code").GetInt32());
        Assert.Contains(
            "UnsupportedDownloaderType",
            legacyRssBody.RootElement.GetProperty("msg").GetString(),
            StringComparison.Ordinal);
        Assert.False(transport.Called);

        var script = await app.Client.GetStringAsync("/app.js");
        Assert.Contains("migration_diagnostics", script, StringComparison.Ordinal);
        Assert.Contains("旧配置迁移阻断", script, StringComparison.Ordinal);
    }

    private static async Task AssertBlocked(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        Assert.Equal("UnsupportedDownloaderType", body.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("password", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    private sealed class MustNotBeUsedRegistry : IDownloadClientRegistry
    {
        public IReadOnlyCollection<string> InstanceIds =>
            throw new InvalidOperationException("Legacy migration must replace the supplied registry.");

        public IDownloadClient GetRequired(string instanceId) =>
            throw new InvalidOperationException($"Unexpected downloader access: {instanceId}");
    }

    private sealed class MustNotBeUsedTransport : ITorrentHttpTransport
    {
        public bool Called { get; private set; }

        public ValueTask<TorrentHttpResponse> SendAsync(
            Uri uri,
            IReadOnlyList<IPAddress> validatedAddresses,
            CancellationToken cancellationToken)
        {
            Called = true;
            throw new InvalidOperationException(
                $"Legacy migration should block RSS before HTTP: {uri.Host}");
        }
    }
}
