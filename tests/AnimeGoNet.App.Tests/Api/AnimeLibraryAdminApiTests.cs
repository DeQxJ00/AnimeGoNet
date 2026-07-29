using System.Net;
using System.Text;
using System.Text.Json;
using AnimeGoNet.Core.Metadata;
using AnimeGoNet.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeGoNet.App.Tests.Api;

public sealed class AnimeLibraryAdminApiTests
{
    [Fact]
    public async Task CreateValidatesTmdbAndPublishesCanonicalSeasonSnapshot()
    {
        var tmdb = new MutableTmdbClient();
        await using var app = await RunningApp.StartAsync(tmdbClient: tmdb);

        using var created = await app.Client.PostAsync(
            "/api/v1/library/seasons",
            Json("""{"tmdb_series_id":100,"tmdb_season_number":1}"""));
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStreamAsync());
        var revision = createdJson.RootElement.GetProperty("resource_revision").GetString();
        using var detail = await app.Client.GetAsync("/api/v1/library/seasons/100/1");
        using var detailJson = JsonDocument.Parse(await detail.Content.ReadAsStreamAsync());
        using var duplicate = await app.Client.PostAsync(
            "/api/v1/library/seasons",
            Json("""{"tmdb_series_id":100,"tmdb_season_number":1}"""));
        using var duplicateJson = JsonDocument.Parse(await duplicate.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("created", createdJson.RootElement.GetProperty("status").GetString());
        Assert.Equal(64, revision!.Length);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal("Canonical Series", detailJson.RootElement.GetProperty("display_name").GetString());
        Assert.Equal(revision, detailJson.RootElement.GetProperty("resource_revision").GetString());
        Assert.Equal(2, detailJson.RootElement.GetProperty("episode_total").GetInt32());
        Assert.Equal(2, detailJson.RootElement.GetProperty("episodes").GetArrayLength());
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(
            "library_season_exists",
            duplicateJson.RootElement.GetProperty("code").GetString());
        Assert.Equal(4, tmdb.CallCount);
    }

    [Fact]
    public async Task RefreshUsesOptimisticRevisionAndReplacesTmdbSnapshot()
    {
        var tmdb = new MutableTmdbClient();
        await using var app = await RunningApp.StartAsync(tmdbClient: tmdb);
        using var created = await app.Client.PostAsync(
            "/api/v1/library/seasons",
            Json("""{"tmdb_series_id":100,"tmdb_season_number":1}"""));
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStreamAsync());
        var revision = createdJson.RootElement.GetProperty("resource_revision").GetString()!;
        tmdb.Series = tmdb.Series with { Name = "Changed Series" };
        tmdb.Season = tmdb.Season with
        {
            Name = "Changed Season",
            EpisodeCount = 1,
            Episodes = [tmdb.Season.Episodes![0] with { Name = "Changed Episode" }],
        };

        using var refreshed = await app.Client.PutAsync(
            "/api/v1/library/seasons/100/1",
            Json($$"""{"expected_revision":"{{revision}}"}"""));
        using var refreshedJson = JsonDocument.Parse(await refreshed.Content.ReadAsStreamAsync());
        var newRevision = refreshedJson.RootElement.GetProperty("resource_revision").GetString();
        using var stale = await app.Client.PutAsync(
            "/api/v1/library/seasons/100/1",
            Json($$"""{"expected_revision":"{{revision}}"}"""));
        using var staleJson = JsonDocument.Parse(await stale.Content.ReadAsStreamAsync());
        using var detail = await app.Client.GetAsync("/api/v1/library/seasons/100/1");
        using var detailJson = JsonDocument.Parse(await detail.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        Assert.Equal("refreshed", refreshedJson.RootElement.GetProperty("status").GetString());
        Assert.NotEqual(revision, newRevision);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(
            "library_revision_conflict",
            staleJson.RootElement.GetProperty("code").GetString());
        Assert.Equal("Changed Series", detailJson.RootElement.GetProperty("display_name").GetString());
        Assert.Equal("Changed Season", detailJson.RootElement.GetProperty("season_name").GetString());
        Assert.Equal(1, detailJson.RootElement.GetProperty("episodes").GetArrayLength());
        Assert.Equal(4, tmdb.CallCount);
    }

    [Fact]
    public async Task DeleteRejectsBusinessReferencesThenRemovesUnreferencedProjection()
    {
        var tmdb = new MutableTmdbClient();
        await using var app = await RunningApp.StartAsync(tmdbClient: tmdb);
        using var created = await app.Client.PostAsync(
            "/api/v1/library/seasons",
            Json("""{"tmdb_series_id":100,"tmdb_season_number":1}"""));
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStreamAsync());
        var revision = createdJson.RootElement.GetProperty("resource_revision").GetString()!;
        var database = app.App.Services.GetRequiredService<AnimeGoSqliteDatabase>();
        await using (var connection = await database.OpenConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO completion_records (
                    id, tmdb_series_id, tmdb_season_number, tmdb_episode_number,
                    source_id, media_path, completed_at_utc)
                VALUES ('completion', 100, 1, 1, 'test', NULL, $now);
                """;
            command.Parameters.AddWithValue("$now", "2026-07-30T03:00:00.0000000+00:00");
            await command.ExecuteNonQueryAsync();
        }

        using var inUse = await app.Client.DeleteAsync(
            $"/api/v1/library/seasons/100/1?expected_revision={revision}");
        var inUseBody = await inUse.Content.ReadAsStringAsync();
        using var inUseJson = JsonDocument.Parse(inUseBody);
        Assert.Equal(HttpStatusCode.Conflict, inUse.StatusCode);
        Assert.Equal(
            "library_season_in_use",
            inUseJson.RootElement.GetProperty("code").GetString());
        Assert.Contains("completion records: 1", inUseBody, StringComparison.Ordinal);
        Assert.Contains("four-part deletion workflow", inUseBody, StringComparison.Ordinal);

        await using (var connection = await database.OpenConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "DELETE FROM completion_records WHERE id = 'completion';";
            await command.ExecuteNonQueryAsync();
        }

        using var deleted = await app.Client.DeleteAsync(
            $"/api/v1/library/seasons/100/1?expected_revision={revision}");
        using var deletedJson = JsonDocument.Parse(await deleted.Content.ReadAsStreamAsync());
        using var missing = await app.Client.GetAsync("/api/v1/library/seasons/100/1");

        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        Assert.Equal("deleted", deletedJson.RootElement.GetProperty("status").GetString());
        Assert.True(deletedJson.RootElement.GetProperty("series_removed").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task TmdbFailureReturnsSafeStatusAndCode()
    {
        var tmdb = new MutableTmdbClient
        {
            Failure = new TmdbClientException(
                MetadataFailureKind.Network,
                "tmdb_network_error",
                tmdbAccessConfirmed: false),
        };
        await using var app = await RunningApp.StartAsync(tmdbClient: tmdb);

        using var response = await app.Client.PostAsync(
            "/api/v1/library/seasons",
            Json("""{"tmdb_series_id":100,"tmdb_season_number":1}"""));
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(
            "tmdb_network_error",
            json.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("secret", body, StringComparison.OrdinalIgnoreCase);
    }

    private static StringContent Json(string value) =>
        new(value, Encoding.UTF8, "application/json");

    private sealed class MutableTmdbClient : ITmdbClient
    {
        public TmdbSeries Series { get; set; } = new(
            100,
            "Canonical Series",
            "Original Series",
            new DateOnly(2024, 1, 1),
            "/series.jpg");

        public TmdbSeason Season { get; set; } = new(
            1001,
            100,
            1,
            "Season One",
            new DateOnly(2024, 1, 1),
            2,
            "/season.jpg",
            [
                new TmdbEpisode(10001, 100, 1, 1, "Episode One", new DateOnly(2024, 1, 1)),
                new TmdbEpisode(10002, 100, 1, 2, "Episode Two", new DateOnly(2024, 1, 8)),
            ]);

        public TmdbClientException? Failure { get; init; }

        public int CallCount { get; private set; }

        public Task<IReadOnlyList<TmdbSeries>> SearchSeriesAsync(
            string title,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TmdbSeries?> GetSeriesAsync(
            int seriesId,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (Failure is not null)
            {
                throw Failure;
            }

            return Task.FromResult<TmdbSeries?>(seriesId == Series.Id ? Series : null);
        }

        public Task<TmdbSeriesDetails?> GetSeriesDetailsAsync(
            int seriesId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TmdbSeason?> GetSeasonAsync(
            int seriesId,
            int seasonNumber,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult<TmdbSeason?>(
                seriesId == Season.SeriesId && seasonNumber == Season.SeasonNumber
                    ? Season
                    : null);
        }

        public Task<TmdbEpisode?> GetEpisodeAsync(
            int seriesId,
            int seasonNumber,
            int episodeNumber,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
