using AnimeGoNet.App.Library;
using AnimeGoNet.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeGoNet.App.Tests.Library;

public sealed class AnimeCoverServiceTests
{
    [Fact]
    public async Task DownloadsSeasonPosterOnceAndThenUsesLocalCache()
    {
        var transport = new RecordingPosterTransport();
        await using var app = await RunningApp.StartAsync(
            configure: options => options with
            {
                Metadata = options.Metadata with
                {
                    Tmdb = options.Metadata.Tmdb with
                    {
                        ApiKey = "test-api-key-never-forward",
                    },
                },
            },
            tmdbPosterTransport: transport);
        await SeedAsync(app);
        var service = app.App.Services.GetRequiredService<AnimeCoverService>();

        var first = await service.GetAsync(100, 1);
        var second = await service.GetAsync(100, 1);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal("season", first.Source);
        Assert.False(first.CacheHit);
        Assert.True(second.CacheHit);
        Assert.Equal("image/jpeg", first.ContentType);
        Assert.Equal(first.Content, second.Content);
        Assert.Equal(1, transport.CallCount);
        var uri = Assert.Single(transport.Requests);
        Assert.Equal("https://image.tmdb.org/t/p/w500/season.jpg", uri.AbsoluteUri);
        Assert.DoesNotContain("test-api-key-never-forward", uri.AbsoluteUri, StringComparison.Ordinal);
        Assert.Single(Directory.EnumerateFiles(
            Path.Combine(app.RootPath, "data", "cache", "covers"),
            "*.bin"));
    }

    [Fact]
    public async Task UsesSeriesFallbackAndLocalPlaceholderWithoutInventingRemotePaths()
    {
        var transport = new RecordingPosterTransport();
        await using var app = await RunningApp.StartAsync(tmdbPosterTransport: transport);
        await SeedAsync(app);
        var service = app.App.Services.GetRequiredService<AnimeCoverService>();

        var series = await service.GetAsync(200, 1);
        var placeholder = await service.GetAsync(300, 1);
        var missing = await service.GetAsync(999, 1);

        Assert.NotNull(series);
        Assert.Equal("series", series.Source);
        Assert.Equal("https://image.tmdb.org/t/p/w500/series.jpg",
            Assert.Single(transport.Requests).AbsoluteUri);
        Assert.NotNull(placeholder);
        Assert.Equal("placeholder", placeholder.Source);
        Assert.Equal("image/svg+xml; charset=utf-8", placeholder.ContentType);
        Assert.Contains("AnimeGoNet poster placeholder",
            System.Text.Encoding.UTF8.GetString(placeholder.Content),
            StringComparison.Ordinal);
        Assert.Null(missing);
        Assert.Equal(1, transport.CallCount);
    }

    [Theory]
    [InlineData(true, "cover_upstream_unavailable")]
    [InlineData(false, "cover_upstream_invalid")]
    public async Task UpstreamFailureOrInvalidContentReturnsUncachedPlaceholder(
        bool throwFailure,
        string expectedWarning)
    {
        var transport = throwFailure
            ? new RecordingPosterTransport(failure: new HttpRequestException("offline"))
            : new RecordingPosterTransport(content: [0x00, 0x01, 0x02]);
        await using var app = await RunningApp.StartAsync(tmdbPosterTransport: transport);
        await SeedAsync(app);
        var service = app.App.Services.GetRequiredService<AnimeCoverService>();

        var first = await service.GetAsync(100, 1);
        var second = await service.GetAsync(100, 1);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal("placeholder", first.Source);
        Assert.Equal(expectedWarning, first.WarningCode);
        Assert.False(first.CacheHit);
        Assert.Equal(2, transport.CallCount);
    }

    [Fact]
    public async Task ConcurrentRequestsShareOneUpstreamDownload()
    {
        var transport = new RecordingPosterTransport(delay: TimeSpan.FromMilliseconds(50));
        await using var app = await RunningApp.StartAsync(tmdbPosterTransport: transport);
        await SeedAsync(app);
        var service = app.App.Services.GetRequiredService<AnimeCoverService>();

        var assets = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => service.GetAsync(100, 1)));

        Assert.All(assets, asset => Assert.NotNull(asset));
        Assert.Equal(1, transport.CallCount);
        Assert.Equal(1, assets.Count(asset => asset!.CacheHit is false));
        Assert.Equal(7, assets.Count(asset => asset!.CacheHit));
    }

    private static async Task SeedAsync(RunningApp app)
    {
        var database = app.App.Services.GetRequiredService<AnimeGoSqliteDatabase>();
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO anime_series (
                id, tmdb_series_id, canonical_name, original_name, poster_path,
                needs_tmdb_completion, created_at_utc, updated_at_utc)
            VALUES
                ('series-season', 100, 'Season', 'Season', '/series-unused.jpg', 0, $now, $now),
                ('series-fallback', 200, 'Series', 'Series', '/series.jpg', 0, $now, $now),
                ('series-placeholder', 300, 'Placeholder', 'Placeholder', NULL, 0, $now, $now);

            INSERT INTO anime_seasons (
                id, series_id, season_number, canonical_name, poster_path,
                created_at_utc, updated_at_utc, episode_count)
            VALUES
                ('season-season', 'series-season', 1, 'Season One', '/season.jpg', $now, $now, 0),
                ('season-fallback', 'series-fallback', 1, 'Series One', NULL, $now, $now, 0),
                ('season-placeholder', 'series-placeholder', 1, 'Placeholder One', NULL, $now, $now, 0);
            """;
        command.Parameters.AddWithValue("$now", "2026-01-01T00:00:00.0000000+00:00");
        await command.ExecuteNonQueryAsync();
    }
}
