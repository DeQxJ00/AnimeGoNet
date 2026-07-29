using AnimeGo.Plugin.Abstractions;
using AnimeGoNet.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeGoNet.App.Tests.Plugins;

public sealed class BuiltInApplicationPluginsTests
{
    [Fact]
    public async Task HostCatalogContainsAllSixCategoriesWithoutPythonEntries()
    {
        await using var app = await RunningApp.StartAsync();
        var catalog = app.App.Services.GetRequiredService<PluginCatalog>();

        Assert.Equal(3, catalog.GetAll<IInputSourceAdapter>().Count);
        Assert.Single(catalog.GetAll<IFeedPlugin>());
        Assert.Single(catalog.GetAll<ITitleParserPlugin>());
        Assert.Single(catalog.GetAll<IFeedFilterPlugin>());
        Assert.Single(catalog.GetAll<IRenamePlugin>());
        Assert.Equal(3, catalog.GetAll<IScheduledPlugin>().Count);
        Assert.Contains(
            catalog.GetAll<IScheduledPlugin>(),
            plugin => plugin.Descriptor.Id == "animegonet-data-update");
        Assert.DoesNotContain(catalog.All, plugin =>
            plugin.Descriptor.Id.EndsWith(".py", StringComparison.Ordinal)
            || plugin.Descriptor.Id.Contains("python", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FeedPluginMapsReaderFailureToStablePluginError()
    {
        await using var app = await RunningApp.StartAsync();
        var plugin = app.App.Services
            .GetRequiredService<PluginCatalog>()
            .Require<IFeedPlugin>("mikan-rss");

        var result = await plugin.FetchAsync(
            new FeedContext(
                "mikan",
                "file:///not-an-rss-feed.xml",
                new Dictionary<string, string>(StringComparer.Ordinal)),
            CancellationToken.None);

        Assert.Empty(result.Items);
        Assert.Equal("rss_url_invalid", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task FilterPluginUsesThePersistedMikanConfiguration()
    {
        await using var app = await RunningApp.StartAsync();
        var plugin = app.App.Services
            .GetRequiredService<PluginCatalog>()
            .Require<IFeedFilterPlugin>("mikan-tool");

        var result = await plugin.FilterAsync(
            new FilterContext(
                "mikan",
                [
                    new FilterItem(
                        0,
                        "[Group] Show [01] [1080p]",
                        "https://tracker.invalid/test.torrent",
                        "https://mikanani.me/Home/Episode/test",
                        null,
                        "3951",
                        "application/x-bittorrent",
                        42,
                        null),
                ],
                new Dictionary<string, string>(StringComparer.Ordinal)),
            CancellationToken.None);

        Assert.Empty(result.Errors);
        Assert.Equal("1", result.Metadata["revision"]);
        Assert.Equal("true", result.Metadata["enabled"]);
        var decision = Assert.Single(result.Decisions);
        Assert.True(decision.Accepted);
        Assert.Equal("Accepted", decision.Outcome);
    }

    [Fact]
    public async Task FilterPluginHonorsExplicitSourceProfileSnapshot()
    {
        await using var app = await RunningApp.StartAsync();
        var database = app.App.Services.GetRequiredService<AnimeGoSqliteDatabase>();
        await using (var connection = await database.OpenConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                UPDATE source_profiles
                SET rss_filter_enabled = 0, revision = revision + 1
                WHERE id = 'mikan';
                """;
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }
        var plugin = app.App.Services
            .GetRequiredService<PluginCatalog>()
            .Require<IFeedFilterPlugin>("mikan-tool");
        var item = new FilterItem(
            0,
            "[Group] Show [01] [1080p]",
            "https://tracker.invalid/test.torrent",
            "https://mikanani.me/Home/Episode/test",
            null,
            "3951",
            "application/x-bittorrent",
            42,
            null);

        var snapshotted = await plugin.FilterAsync(
            new FilterContext(
                "mikan",
                [item],
                new Dictionary<string, string>(StringComparer.Ordinal),
                new FilterSourceProfileSnapshot(1, true, true)),
            CancellationToken.None);
        var current = await plugin.FilterAsync(
            new FilterContext(
                "mikan",
                [item],
                new Dictionary<string, string>(StringComparer.Ordinal)),
            CancellationToken.None);

        Assert.Equal("true", snapshotted.Metadata["enabled"]);
        Assert.Equal("Accepted", Assert.Single(snapshotted.Decisions).Outcome);
        Assert.Equal("false", current.Metadata["enabled"]);
        Assert.Equal("SkippedByConfiguration", Assert.Single(current.Decisions).Outcome);
    }

    [Fact]
    public async Task SchedulePluginExecutesTheRealDispatcherAndSuggestsIdleDelay()
    {
        await using var app = await RunningApp.StartAsync();
        var plugin = app.App.Services
            .GetRequiredService<PluginCatalog>()
            .Require<IScheduledPlugin>("staged-torrent-dispatch");

        var result = await plugin.ExecuteAsync(
            new ScheduledContext(
                "test-dispatch",
                DateTimeOffset.UtcNow,
                new Dictionary<string, string>(StringComparer.Ordinal)),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("NoWork", result.Message);
        Assert.Equal(TimeSpan.FromSeconds(2), result.NextDelay);
    }

    [Fact]
    public async Task DirectoryDatabaseSchedulePluginRefreshesThePersistedIndex()
    {
        await using var app = await RunningApp.StartAsync();
        var plugin = app.App.Services
            .GetRequiredService<PluginCatalog>()
            .Require<IScheduledPlugin>("refresh-directory-database");

        var result = await plugin.ExecuteAsync(
            new ScheduledContext(
                "refresh-test",
                DateTimeOffset.UtcNow,
                new Dictionary<string, string>(StringComparer.Ordinal)),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("indexed=0;rejected=0", result.Message);
        Assert.Null(result.NextDelay);
    }
}
