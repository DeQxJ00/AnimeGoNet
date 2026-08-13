using AnimeGoNet.Data.Library;
using System.Globalization;

namespace AnimeGoNet.Data.Tests.Library;

public sealed class ExternalMediaImportStoreTests
{
    [Fact]
    public async Task ExplicitSeasonScanImportsOnlyUniqueCanonicalEpisodeFiles()
    {
        await using var fixture = await ImportFixture.CreateAsync();
        var seasonPath = fixture.SeasonPath("Alpha", 1);
        Write(seasonPath, "E001.mkv", [1]);
        Write(seasonPath, "E002.mp4", [2]);
        Write(seasonPath, "E003.mkv", [3]);
        Write(seasonPath, "E003.mp4", [4]);
        Write(seasonPath, "E099.mkv", [5]);
        Write(seasonPath, "episode.mkv", [6]);
        Write(seasonPath, "E001.zh.ass", [7]);
        Write(Path.Combine(seasonPath, "Other"), "E003.mkv", [8]);

        var result = await fixture.Store.ScanSeasonAsync(
            fixture.SaveRoot,
            100,
            1,
            new DateTimeOffset(2026, 8, 13, 1, 2, 3, TimeSpan.Zero));

        Assert.NotNull(result);
        Assert.Equal(1, result.ScannedSeasonCount);
        Assert.Equal(6, result.CandidateFileCount);
        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(1, result.AlreadyRecordedCount);
        Assert.Equal(4, result.SkippedCount);
        Assert.Contains(result.Items, item =>
            item.TmdbEpisodeNumber == 1
            && item.Status == "imported"
            && item.RelativePath == "Alpha/S01/E001.mkv");
        Assert.Contains(result.Items, item =>
            item.TmdbEpisodeNumber == 2 && item.Status == "already_recorded");
        Assert.Equal(2, result.Items.Count(item =>
            item.ReasonCode == "external_media_episode_ambiguous"));
        Assert.Contains(result.Items, item =>
            item.ReasonCode == "external_media_tmdb_episode_missing");
        Assert.Contains(result.Items, item =>
            item.ReasonCode == "external_media_filename_invalid");
        Assert.DoesNotContain(result.Items, item => item.RelativePath.Contains("Other", StringComparison.Ordinal));

        await using var connection = await fixture.Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT source_id, source_item_id, media_path, completed_at_utc
            FROM completion_records
            WHERE tmdb_series_id = 100 AND tmdb_season_number = 1
              AND tmdb_episode_number = 1;
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(ExternalMediaImportStore.SourceId, reader.GetString(0));
        Assert.Equal("Alpha/S01/E001.mkv", reader.GetString(1));
        Assert.Equal(Path.Combine(seasonPath, "E001.mkv"), reader.GetString(2));
        Assert.Equal("2026-08-13T01:02:03.0000000+00:00", reader.GetString(3));
        await reader.DisposeAsync();
        command.CommandText = "SELECT state FROM episode_claims WHERE id = 'active-claim';";
        Assert.Equal("completed", await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task ScanIsNeverImplicitAndScopedMissingSeasonReturnsNull()
    {
        await using var fixture = await ImportFixture.CreateAsync();
        Write(fixture.SeasonPath("Alpha", 1), "E001.mkv", [1]);

        Assert.Equal(1, await fixture.CompletionCountAsync());
        Assert.Null(await fixture.Store.ScanSeasonAsync(
            fixture.SaveRoot,
            999,
            1,
            DateTimeOffset.UtcNow));
        Assert.Equal(1, await fixture.CompletionCountAsync());

        var result = await fixture.Store.ScanAllAsync(
            fixture.SaveRoot,
            DateTimeOffset.UtcNow);

        Assert.Equal(2, result.ScannedSeasonCount);
        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(2, await fixture.CompletionCountAsync());
    }

    [Fact]
    public async Task EmptyAndNonCanonicalFilesAreReportedWithoutCreatingProgress()
    {
        await using var fixture = await ImportFixture.CreateAsync();
        var seasonPath = fixture.SeasonPath("Alpha", 1);
        Write(seasonPath, "E001.mkv", []);
        Write(seasonPath, "E1.mkv", [1]);

        var result = await fixture.Store.ScanSeasonAsync(
            fixture.SaveRoot,
            100,
            1,
            DateTimeOffset.UtcNow);

        Assert.NotNull(result);
        Assert.Equal(2, result.CandidateFileCount);
        Assert.Equal(0, result.ImportedCount);
        Assert.Equal(2, result.SkippedCount);
        Assert.Contains(result.Items, item => item.ReasonCode == "external_media_file_empty");
        Assert.Contains(result.Items, item => item.ReasonCode == "external_media_filename_invalid");
        Assert.Equal(1, await fixture.CompletionCountAsync());
    }

    private static void Write(string directory, string name, byte[] content)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, name), content);
    }

    private sealed class ImportFixture : IAsyncDisposable
    {
        private readonly SqliteDatabaseFixture _fixture;

        private ImportFixture(SqliteDatabaseFixture fixture)
        {
            _fixture = fixture;
            SaveRoot = Path.Combine(fixture.RootPath, "media");
            Store = new ExternalMediaImportStore(fixture.Database);
        }

        public string SaveRoot { get; }

        public ExternalMediaImportStore Store { get; }

        public AnimeGoNet.Data.Sqlite.AnimeGoSqliteDatabase Database => _fixture.Database;

        public static async Task<ImportFixture> CreateAsync()
        {
            var value = new ImportFixture(await SqliteDatabaseFixture.CreateAsync());
            await value.SeedAsync();
            return value;
        }

        public string SeasonPath(string seriesName, int seasonNumber) =>
            Path.Combine(SaveRoot, seriesName, $"S{seasonNumber:00}");

        public async Task<int> CompletionCountAsync()
        {
            await using var connection = await Database.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM completion_records;";
            return Convert.ToInt32(
                await command.ExecuteScalarAsync(),
                CultureInfo.InvariantCulture);
        }

        private async Task SeedAsync()
        {
            await using var connection = await Database.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO source_profiles (
                    id, display_name, adapter, downloader_id, file_strategy,
                    rss_filter_enabled, rss_priority_enabled, revision, enabled,
                    created_at_utc, updated_at_utc)
                VALUES ('external-test', 'External test', 'mikan', 'bt', 'move',
                        0, 0, 1, 1, $now, $now);

                INSERT INTO anime_series (
                    id, tmdb_series_id, canonical_name, original_name,
                    needs_tmdb_completion, created_at_utc, updated_at_utc)
                VALUES ('alpha', 100, 'Alpha', 'Alpha JP', 0, $now, $now);

                INSERT INTO anime_seasons (
                    id, series_id, season_number, canonical_name,
                    created_at_utc, updated_at_utc)
                VALUES
                    ('alpha-1', 'alpha', 1, 'Season 1', $now, $now),
                    ('alpha-2', 'alpha', 2, 'Season 2', $now, $now);

                INSERT INTO tmdb_episodes (
                    tmdb_episode_id, series_id, season_number, episode_number,
                    name, fetched_at_utc)
                VALUES
                    (1001, 'alpha', 1, 1, 'One', $now),
                    (1002, 'alpha', 1, 2, 'Two', $now),
                    (1003, 'alpha', 1, 3, 'Three', $now),
                    (2001, 'alpha', 2, 1, 'Season Two One', $now);

                INSERT INTO completion_records (
                    id, tmdb_series_id, tmdb_season_number, tmdb_episode_number,
                    source_id, media_path, completed_at_utc)
                VALUES ('existing', 100, 1, 2, 'mikan', '/old/E002.mp4', $now);

                INSERT INTO ingest_tasks (
                    id, source_profile_id, source_profile_revision, source_id,
                    title, torrent_url_fingerprint, downloader_id,
                    route_snapshot_json, status, created_at_utc, updated_at_utc)
                VALUES ('active-task', 'external-test', 1, 'mikan', 'Active',
                        'external-active-fingerprint', 'bt', '{}',
                        'metadata_resolved', $now, $now);

                INSERT INTO task_files (
                    id, task_id, relative_path, size_bytes, tmdb_series_id,
                    tmdb_season_number, tmdb_episode_number, tmdb_episode_id,
                    disposition)
                VALUES ('active-file', 'active-task', 'episode.mkv', 1,
                        100, 1, 1, 1001, 'episode');

                INSERT INTO episode_claims (
                    id, tmdb_series_id, tmdb_season_number, tmdb_episode_number,
                    task_file_id, state, claimed_at_utc, expires_at_utc)
                VALUES ('active-claim', 100, 1, 1, 'active-file', 'active',
                        $now, '2026-08-14T00:00:00.0000000+00:00');
                """;
            command.Parameters.AddWithValue("$now", "2026-08-13T00:00:00.0000000+00:00");
            await command.ExecuteNonQueryAsync();
        }

        public async ValueTask DisposeAsync()
        {
            Store.Dispose();
            await _fixture.DisposeAsync();
        }
    }
}
