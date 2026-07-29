using AnimeGoNet.Core.Metadata;
using AnimeGoNet.Data.Library;

namespace AnimeGoNet.Data.Tests.Library;

public sealed class AnimeLibraryAdminStoreTests
{
    [Fact]
    public async Task CreatePersistsTmdbCanonicalSeasonAndCompleteEpisodeSnapshot()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var admin = new AnimeLibraryAdminStore(fixture.Database);
        var library = new AnimeLibraryStore(fixture.Database);
        var now = new DateTimeOffset(2026, 7, 30, 1, 0, 0, TimeSpan.Zero);

        var created = await admin.CreateAsync(Series(), Season(1, "Season One", 1, 2), now);
        var duplicate = await admin.CreateAsync(Series(), Season(1, "Season One", 1, 2), now);
        var detail = await library.GetSeasonAsync(100, 1);

        Assert.Equal(AnimeLibraryMutationStatus.Created, created.Status);
        Assert.Equal(64, created.ResourceRevision!.Length);
        Assert.Equal(AnimeLibraryMutationStatus.AlreadyExists, duplicate.Status);
        Assert.Equal(created.ResourceRevision, duplicate.ResourceRevision);
        Assert.NotNull(detail);
        Assert.Equal("Canonical Series", detail.Season.DisplayName);
        Assert.Equal("Season One", detail.Season.SeasonName);
        Assert.Equal(created.ResourceRevision, detail.Season.ResourceRevision);
        Assert.Equal(2, detail.Season.EpisodeTotal);
        Assert.Equal(2, detail.Season.EpisodeSnapshotCount);
        Assert.Equal([1, 2], detail.Episodes.Select(value => value.EpisodeNumber).ToArray());
    }

    [Fact]
    public async Task RefreshRequiresRevisionAndReplacesStaleTmdbEpisodeSnapshot()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var admin = new AnimeLibraryAdminStore(fixture.Database);
        var library = new AnimeLibraryStore(fixture.Database);
        var created = await admin.CreateAsync(
            Series(),
            Season(1, "Old Season", 1, 2),
            new DateTimeOffset(2026, 7, 30, 1, 0, 0, TimeSpan.Zero));

        var conflict = await admin.RefreshAsync(
            Series("Changed"),
            Season(1, "New Season", 1),
            new string('0', 64),
            new DateTimeOffset(2026, 7, 30, 2, 0, 0, TimeSpan.Zero));
        var updated = await admin.RefreshAsync(
            Series("Changed"),
            Season(1, "New Season", 1),
            created.ResourceRevision!,
            new DateTimeOffset(2026, 7, 30, 2, 0, 0, TimeSpan.Zero));
        var detail = await library.GetSeasonAsync(100, 1);

        Assert.Equal(AnimeLibraryMutationStatus.RevisionConflict, conflict.Status);
        Assert.Equal(created.ResourceRevision, conflict.ResourceRevision);
        Assert.Equal(AnimeLibraryMutationStatus.Updated, updated.Status);
        Assert.NotEqual(created.ResourceRevision, updated.ResourceRevision);
        Assert.NotNull(detail);
        Assert.Equal("Changed", detail.Season.DisplayName);
        Assert.Equal("New Season", detail.Season.SeasonName);
        Assert.Equal(updated.ResourceRevision, detail.Season.ResourceRevision);
        Assert.Equal(1, detail.Season.EpisodeTotal);
        var episode = Assert.Single(detail.Episodes);
        Assert.Equal(1, episode.EpisodeNumber);
    }

    [Fact]
    public async Task DeletePreservesSeriesWithOtherSeasonsAndRejectsBusinessReferences()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var admin = new AnimeLibraryAdminStore(fixture.Database);
        var library = new AnimeLibraryStore(fixture.Database);
        var first = await admin.CreateAsync(
            Series(),
            Season(1, "Season One", 1),
            new DateTimeOffset(2026, 7, 30, 1, 0, 0, TimeSpan.Zero));
        var second = await admin.CreateAsync(
            Series(),
            Season(2, "Season Two", 1),
            new DateTimeOffset(2026, 7, 30, 2, 0, 0, TimeSpan.Zero));
        var refreshedFirst = await library.GetSeasonAsync(100, 1);

        var deletedFirst = await admin.DeleteAsync(
            100,
            1,
            refreshedFirst!.Season.ResourceRevision);

        Assert.Equal(AnimeLibraryMutationStatus.Deleted, deletedFirst.Status);
        Assert.False(deletedFirst.SeriesRemoved);
        Assert.Null(await library.GetSeasonAsync(100, 1));
        Assert.NotNull(await library.GetSeasonAsync(100, 2));

        await using (var connection = await fixture.Database.OpenConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO completion_records (
                    id, tmdb_series_id, tmdb_season_number, tmdb_episode_number,
                    source_id, media_path, completed_at_utc)
                VALUES ('completion', 100, 2, 1, 'test', NULL, $now);
                """;
            command.Parameters.AddWithValue("$now", "2026-07-30T03:00:00.0000000+00:00");
            await command.ExecuteNonQueryAsync();
        }

        var inUse = await admin.DeleteAsync(100, 2, second.ResourceRevision!);
        Assert.Equal(AnimeLibraryMutationStatus.InUse, inUse.Status);
        Assert.Equal(1, inUse.References!.CompletionRecords);

        await using (var connection = await fixture.Database.OpenConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "DELETE FROM completion_records WHERE id = 'completion';";
            await command.ExecuteNonQueryAsync();
        }

        var deletedSecond = await admin.DeleteAsync(100, 2, second.ResourceRevision!);
        Assert.Equal(AnimeLibraryMutationStatus.Deleted, deletedSecond.Status);
        Assert.True(deletedSecond.SeriesRemoved);
        Assert.Null(await library.GetSeasonAsync(100, 2));
        Assert.Equal(AnimeLibraryMutationStatus.Created, first.Status);
    }

    private static TmdbSeries Series(string name = "Canonical Series") =>
        new(
            100,
            name,
            "Original Series",
            new DateOnly(2024, 1, 1),
            "/series.jpg");

    private static TmdbSeason Season(
        int number,
        string name,
        params int[] episodeNumbers) =>
        new(
            1000 + number,
            100,
            number,
            name,
            new DateOnly(2024 + number - 1, 1, 1),
            episodeNumbers.Length,
            $"/season-{number}.jpg",
            episodeNumbers.Select(episode => new TmdbEpisode(
                number * 1000 + episode,
                100,
                number,
                episode,
                $"Episode {episode}",
                new DateOnly(2024 + number - 1, 1, episode))).ToArray());
}
