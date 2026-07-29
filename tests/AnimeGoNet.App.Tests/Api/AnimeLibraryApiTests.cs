using System.Net;
using System.Text.Json;
using AnimeGoNet.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeGoNet.App.Tests.Api;

public sealed class AnimeLibraryApiTests
{
    [Fact]
    public async Task ListsCanonicalSeasonProjectionWithoutMediaPathsOrFallbackRows()
    {
        await using var app = await RunningApp.StartAsync();
        await SeedAsync(app.App.Services.GetRequiredService<AnimeGoSqliteDatabase>());

        using var response = await app.Client.GetAsync("/api/v1/library/seasons");
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToArray();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, json.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(24, json.RootElement.GetProperty("page_size").GetInt32());
        Assert.Equal(2, json.RootElement.GetProperty("total_items").GetInt32());
        Assert.Equal("last_updated", json.RootElement.GetProperty("sort").GetString());
        Assert.Equal("desc", json.RootElement.GetProperty("direction").GetString());
        Assert.Equal("tmdb:200:s1", items[0].GetProperty("id").GetString());
        Assert.Equal("tmdb:100:s1", items[1].GetProperty("id").GetString());
        Assert.Equal("/alpha-season.jpg", items[1].GetProperty("poster_path").GetString());
        Assert.Equal("season", items[1].GetProperty("poster_source").GetString());
        Assert.Equal(2, items[1].GetProperty("episode_total").GetInt32());
        Assert.Equal(2, items[1].GetProperty("episode_snapshot_count").GetInt32());
        Assert.Equal(1, items[1].GetProperty("episode_downloaded").GetInt32());
        Assert.DoesNotContain("/media/alpha.mkv", body, StringComparison.Ordinal);
        Assert.DoesNotContain("fallback-row", body, StringComparison.Ordinal);
        Assert.DoesNotContain("series-alpha", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SortDirectionAndPaginationAreAppliedBeforeReturningItems()
    {
        await using var app = await RunningApp.StartAsync();
        await SeedAsync(app.App.Services.GetRequiredService<AnimeGoSqliteDatabase>());

        using var response = await app.Client.GetAsync(
            "/api/v1/library/seasons?sort=air_date&direction=asc&page=2&page_size=1");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, json.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(2, json.RootElement.GetProperty("total_items").GetInt32());
        var item = Assert.Single(json.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(200, item.GetProperty("tmdb_series_id").GetInt32());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("air_date").ValueKind);
    }

    [Theory]
    [InlineData("?page=0", "library_page_invalid")]
    [InlineData("?page_size=101", "library_page_size_invalid")]
    [InlineData("?sort=unknown", "library_sort_invalid")]
    [InlineData("?direction=sideways", "library_direction_invalid")]
    public async Task InvalidQueryUsesStableErrors(string query, string expectedCode)
    {
        await using var app = await RunningApp.StartAsync();

        using var response = await app.Client.GetAsync("/api/v1/library/seasons" + query);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expectedCode, json.RootElement.GetProperty("code").GetString());
    }

    private static async Task SeedAsync(AnimeGoSqliteDatabase database)
    {
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO anime_series (
                id, tmdb_series_id, bangumi_subject_id, canonical_name, original_name,
                poster_path, needs_tmdb_completion, created_at_utc, updated_at_utc,
                first_air_date)
            VALUES
                ('series-alpha', 100, NULL, 'Alpha', 'Alpha', '/alpha-series.jpg', 0,
                 '2026-01-01T00:00:00.0000000+00:00',
                 '2026-01-02T00:00:00.0000000+00:00', '2024-01-01'),
                ('series-beta', 200, NULL, 'Beta', 'Beta', NULL, 0,
                 '2026-01-02T00:00:00.0000000+00:00',
                 '2026-01-03T00:00:00.0000000+00:00', NULL),
                ('fallback-row', 0, 547888, 'Fallback', 'Fallback', NULL, 1,
                 '2026-01-03T00:00:00.0000000+00:00',
                 '2026-01-04T00:00:00.0000000+00:00', NULL);

            INSERT INTO anime_seasons (
                id, series_id, season_number, canonical_name, poster_path,
                created_at_utc, updated_at_utc, air_date, episode_count)
            VALUES
                ('season-alpha', 'series-alpha', 1, 'Alpha One', '/alpha-season.jpg',
                 '2026-01-01T00:00:00.0000000+00:00',
                 '2026-01-02T00:00:00.0000000+00:00', '2024-01-01', 2),
                ('season-beta', 'series-beta', 1, 'Beta One', NULL,
                 '2026-01-02T00:00:00.0000000+00:00',
                 '2026-01-03T00:00:00.0000000+00:00', NULL, 1),
                ('season-fallback', 'fallback-row', 1, 'Fallback One', NULL,
                 '2026-01-03T00:00:00.0000000+00:00',
                 '2026-01-04T00:00:00.0000000+00:00', NULL, 0);

            INSERT INTO tmdb_episodes (
                tmdb_episode_id, series_id, season_number, episode_number,
                name, air_date, fetched_at_utc)
            VALUES
                (1001, 'series-alpha', 1, 1, 'Alpha 1', '2024-01-01', $now),
                (1002, 'series-alpha', 1, 2, 'Alpha 2', '2024-01-08', $now);

            INSERT INTO completion_records (
                id, tmdb_series_id, tmdb_season_number, tmdb_episode_number,
                source_id, media_path, completed_at_utc)
            VALUES (
                'completion-alpha', 100, 1, 1, 'test', '/media/alpha.mkv',
                '2026-01-02T00:00:00.0000000+00:00');
            """;
        command.Parameters.AddWithValue("$now", "2026-01-01T00:00:00.0000000+00:00");
        await command.ExecuteNonQueryAsync();
    }
}
