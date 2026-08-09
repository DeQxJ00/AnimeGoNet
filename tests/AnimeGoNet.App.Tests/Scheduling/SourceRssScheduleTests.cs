using System.Net;
using System.Text;
using System.Text.Json;
using AnimeGo.Plugin.Abstractions;
using AnimeGoNet.App.Configuration;
using AnimeGoNet.App.Scheduling;
using AnimeGoNet.App.Torrents;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Data.Sources;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeGoNet.App.Tests.Scheduling;

public sealed class SourceRssScheduleTests
{
    private static readonly string[] MikanHosts = ["mikan.example"];

    [Fact]
    public async Task ScheduledPluginIngestsFeedAndPersistsSuccessWithoutLeakingUrl()
    {
        const string secretUrl =
            "https://mikan.example/rss?token=schedule-private-passkey";
        var transport = new StaticFeedTransport(HttpStatusCode.OK);
        await using var app = await RunningApp.StartAsync(
            configure: WithMikanTestOrigin,
            rssDnsResolver: new PublicDnsResolver(),
            rssHttpTransport: transport);
        await CreateScheduledSourceAsync(app, secretUrl);
        var plugin = app.App.Services
            .GetRequiredService<PluginCatalog>()
            .Require<IScheduledPlugin>(SourceRssScheduleManager.PluginId);

        var result = await plugin.ExecuteAsync(
            Context(revision: 1),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.StartsWith("batch=", result.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("schedule-private-passkey", result.Message, StringComparison.Ordinal);
        Assert.Equal(secretUrl, Assert.Single(transport.Requests).AbsoluteUri);
        var stored = Assert.IsType<SourceProfileAdminRecord>(await app.App.Services
            .GetRequiredService<SourceProfileStore>()
            .GetAsync("mikan-scheduled"));
        Assert.Equal("succeeded", stored.RssLastRunState);
        Assert.NotNull(stored.RssLastBatchId);
        Assert.Null(stored.RssLastFailureCode);

        Assert.True(await app.App.Services
            .GetRequiredService<SourceProfileStore>()
            .TryStartScheduledRunAsync("mikan-scheduled", 1, DateTimeOffset.UtcNow));
        var requestCount = transport.Requests.Count;
        var overlapping = await plugin.ExecuteAsync(Context(1), CancellationToken.None);
        Assert.True(overlapping.Succeeded);
        Assert.Equal("already-running-or-stale", overlapping.Message);
        Assert.Equal(requestCount, transport.Requests.Count);
    }

    [Fact]
    public async Task ScheduledPluginAuditsStableFailureCodeAndNeverReturnsSecretUrl()
    {
        const string secretUrl =
            "https://mikan.example/rss?token=failure-private-passkey";
        var transport = new StaticFeedTransport(HttpStatusCode.InternalServerError);
        await using var app = await RunningApp.StartAsync(
            configure: WithMikanTestOrigin,
            rssDnsResolver: new PublicDnsResolver(),
            rssHttpTransport: transport);
        await CreateScheduledSourceAsync(app, secretUrl);
        var plugin = app.App.Services
            .GetRequiredService<PluginCatalog>()
            .Require<IScheduledPlugin>(SourceRssScheduleManager.PluginId);

        var result = await plugin.ExecuteAsync(Context(1), CancellationToken.None);

        Assert.False(result.Succeeded);
        var error = Assert.Single(result.Errors);
        Assert.Equal("rss_request_failed", error.Code);
        Assert.DoesNotContain("failure-private-passkey", error.Message, StringComparison.Ordinal);
        var stored = Assert.IsType<SourceProfileAdminRecord>(await app.App.Services
            .GetRequiredService<SourceProfileStore>()
            .GetAsync("mikan-scheduled"));
        Assert.Equal("failed", stored.RssLastRunState);
        Assert.Equal("rss_request_failed", stored.RssLastFailureCode);
        Assert.Null(stored.RssLastBatchId);
    }

    [Fact]
    public async Task ManagerRegistersReplacesAndRemovesOnlyCompileTimeSourceSchedule()
    {
        const string secretUrl = "https://mikan.example/rss?token=manager-private";
        await using var app = await RunningApp.StartAsync();
        await CreateScheduledSourceAsync(app, secretUrl);
        var coordinator = app.App.Services.GetRequiredService<PluginScheduleCoordinator>();
        var store = app.App.Services.GetRequiredService<SourceProfileStore>();
        using var manager = new SourceRssScheduleManager(
            coordinator,
            store,
            new RuntimeConfigurationState(false, true, false));

        await manager.ApplyAllAsync();
        var first = Assert.IsType<PluginScheduleSnapshot>(manager.Get("mikan-scheduled"));
        Assert.Equal(SourceRssScheduleManager.PluginId, first.PluginId);
        Assert.Equal("0 5/15 * * * ?", first.Cron);
        Assert.DoesNotContain("manager-private", first.ToString(), StringComparison.Ordinal);

        var current = Assert.IsType<SourceProfileAdminRecord>(
            await store.GetAsync("mikan-scheduled"));
        var updated = await store.UpdateAsync(
            current.Id,
            Definition(current) with { RssScheduleCron = "0 10/15 * * * ?" },
            current.Revision,
            DateTimeOffset.UtcNow);
        await manager.ApplyAsync(updated);
        Assert.Equal("0 10/15 * * * ?", manager.Get(current.Id)?.Cron);
        Assert.Single(coordinator.List(), item =>
            item.Name == SourceRssScheduleManager.ScheduleName(current.Id));

        var newest = await store.UpdateAsync(
            current.Id,
            Definition(updated) with { RssScheduleCron = "0 12/15 * * * ?" },
            updated.Revision,
            DateTimeOffset.UtcNow);
        Assert.Equal(3, newest.Revision);
        await manager.ApplyAsync(updated);
        Assert.Equal("0 12/15 * * * ?", manager.Get(current.Id)?.Cron);

        await manager.RemoveAsync(current.Id);
        Assert.Null(manager.Get(current.Id));
        Assert.Equal(64, SourceRssScheduleManager.ScheduleName(new string('a', 64)).Length);
    }

    [Fact]
    public async Task ScheduledPluginIgnoresStaleRevisionBeforeNetworkRequest()
    {
        const string secretUrl = "https://mikan.example/rss?token=stale-private";
        var transport = new StaticFeedTransport(HttpStatusCode.OK);
        await using var app = await RunningApp.StartAsync(
            rssDnsResolver: new PublicDnsResolver(),
            rssHttpTransport: transport);
        await CreateScheduledSourceAsync(app, secretUrl);
        var store = app.App.Services.GetRequiredService<SourceProfileStore>();
        var current = Assert.IsType<SourceProfileAdminRecord>(
            await store.GetAsync("mikan-scheduled"));
        _ = await store.UpdateAsync(
            current.Id,
            Definition(current),
            current.Revision,
            DateTimeOffset.UtcNow);
        var plugin = app.App.Services
            .GetRequiredService<PluginCatalog>()
            .Require<IScheduledPlugin>(SourceRssScheduleManager.PluginId);

        var result = await plugin.ExecuteAsync(Context(1), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("stale", result.Message);
        Assert.Empty(transport.Requests);
    }

    [Fact]
    public async Task ApiHotApplyRegistersAndDisablesScheduleWhenWorkersAreRunning()
    {
        const string secretUrl = "https://mikan.example/rss?token=hosted-private";
        await using var app = await RunningApp.StartAsync(startBackgroundWorkers: true);
        await CreateScheduledSourceAsync(app, secretUrl, "0 0 4 * * ?");

        using var configured = JsonDocument.Parse(
            await app.Client.GetStreamAsync("/api/v1/sources/mikan-scheduled"));
        Assert.True(configured.RootElement.GetProperty("rss_schedule_registered").GetBoolean());
        Assert.NotEqual(
            JsonValueKind.Null,
            configured.RootElement.GetProperty("rss_schedule_next_at_utc").ValueKind);
        Assert.NotNull(app.App.Services
            .GetRequiredService<SourceRssScheduleManager>()
            .Get("mikan-scheduled"));

        using var disable = await app.Client.PutAsync(
            "/api/v1/sources/mikan-scheduled",
            new StringContent(
                JsonSerializer.Serialize(new
                {
                    display_name = "Mikan Scheduled",
                    downloader_id = "bt",
                    file_strategy = "move",
                    allowed_torrent_hosts = MikanHosts,
                    enabled = true,
                    rss_schedule_enabled = false,
                    expected_revision = 1,
                }),
                Encoding.UTF8,
                "application/json"));
        using var disabled = JsonDocument.Parse(await disable.Content.ReadAsStreamAsync());
        Assert.Equal(HttpStatusCode.OK, disable.StatusCode);
        Assert.False(disabled.RootElement.GetProperty("rss_schedule_registered").GetBoolean());
        Assert.Null(app.App.Services
            .GetRequiredService<SourceRssScheduleManager>()
            .Get("mikan-scheduled"));
    }

    private static AnimeGoOptions WithMikanTestOrigin(AnimeGoOptions options) =>
        options with
        {
            Metadata = options.Metadata with
            {
                Mikan = new MikanClientOptions
                {
                    BaseUrl = new Uri("https://mikan.example/"),
                },
            },
        };

    private static ScheduledContext Context(long revision) =>
        new(
            "source-rss-mikan-scheduled",
            DateTimeOffset.UtcNow,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["source_profile_id"] = "mikan-scheduled",
                ["source_profile_revision"] = revision.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
            });

    private static SourceProfileDefinition Definition(SourceProfileAdminRecord current) =>
        new(
            current.DisplayName,
            current.Adapter,
            current.DownloaderId,
            current.FileStrategy,
            current.AllowedTorrentHosts,
            current.Category,
            current.Tags,
            current.SeedingTimeMinutes,
            current.RssFilterEnabled,
            current.RssPriorityEnabled,
            current.Enabled,
            current.MikanIdentityCookie,
            current.DynamicTagTemplate,
            current.RssFeedUrl,
            current.RssScheduleEnabled,
            current.RssScheduleCron);

    private static async Task CreateScheduledSourceAsync(
        RunningApp app,
        string url,
        string cron = "0 5/15 * * * ?")
    {
        using var response = await app.Client.PostAsync(
            "/api/v1/sources",
            new StringContent(
                JsonSerializer.Serialize(new
                {
                    id = "mikan-scheduled",
                    display_name = "Mikan Scheduled",
                    adapter = "mikan",
                    downloader_id = "bt",
                    file_strategy = "move",
                    allowed_torrent_hosts = MikanHosts,
                    enabled = true,
                    rss_feed_url = url,
                    rss_schedule_enabled = true,
                    rss_schedule_cron = cron,
                }),
                Encoding.UTF8,
                "application/json"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.DoesNotContain(
            new Uri(url).Query,
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    private sealed class PublicDnsResolver : ITorrentDnsResolver
    {
        public ValueTask<IReadOnlyList<IPAddress>> ResolveAsync(
            string host,
            CancellationToken cancellationToken)
        {
            _ = host;
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<IReadOnlyList<IPAddress>>(
                [IPAddress.Parse("1.1.1.1")]);
        }
    }

    private sealed class StaticFeedTransport(HttpStatusCode status) : ITorrentHttpTransport
    {
        private static readonly byte[] Feed = Encoding.UTF8.GetBytes(
            "<rss><channel></channel></rss>");

        public List<Uri> Requests { get; } = [];

        public ValueTask<TorrentHttpResponse> SendAsync(
            Uri uri,
            IReadOnlyList<IPAddress> validatedAddresses,
            CancellationToken cancellationToken)
        {
            _ = validatedAddresses;
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(uri);
            return ValueTask.FromResult(new TorrentHttpResponse(
                status,
                null,
                Feed.Length,
                new MemoryStream(Feed, writable: false)));
        }
    }
}
