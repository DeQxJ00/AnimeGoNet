using AnimeGoNet.App.Library;
using AnimeGoNet.App.Metadata;
using AnimeGoNet.Data.Metadata;
using AnimeGoNet.Data.Sqlite;

namespace AnimeGoNet.App.Tests.Metadata;

public sealed class PendingTmdbNfoRewriteProcessorTests
{
    [Fact]
    public async Task WritesCanonicalNfoInsideExistingFallbackSeriesDirectory()
    {
        await using var fixture = await RewriteFixture.CreateAsync(blockedRoot: false);
        var result = await fixture.Processor.RunOnceAsync();

        Assert.Equal(PendingTmdbNfoRewriteResult.Completed, result);
        var target = Path.Combine(fixture.SaveRoot, "Fallback Anime", "tvshow.nfo");
        var content = await File.ReadAllTextAsync(target);
        Assert.Contains("<title>Canonical Anime</title>", content, StringComparison.Ordinal);
        Assert.Contains("<tmdbid>700</tmdbid>", content, StringComparison.Ordinal);
        Assert.DoesNotContain("<bangumiid>", content, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(fixture.SaveRoot, "Canonical Anime")));
        Assert.Equal(PendingTmdbNfoRewriteResult.NoWork, await fixture.Processor.RunOnceAsync());
        Assert.Equal("completed", await fixture.ReadStateAsync());
    }

    [Fact]
    public async Task FileFailureIsPersistedForRetryWithoutCompletingJob()
    {
        await using var fixture = await RewriteFixture.CreateAsync(blockedRoot: true);

        var result = await fixture.Processor.RunOnceAsync();

        Assert.Equal(PendingTmdbNfoRewriteResult.RetryScheduled, result);
        Assert.Equal("failed", await fixture.ReadStateAsync());
        Assert.Equal("nfo_rewrite_failed", await fixture.ReadFailureAsync());
    }

    private sealed class RewriteFixture : IAsyncDisposable
    {
        private readonly string root;

        private RewriteFixture(
            string root,
            string saveRoot,
            AnimeGoSqliteDatabase database)
        {
            this.root = root;
            SaveRoot = saveRoot;
            Database = database;
            Processor = new PendingTmdbNfoRewriteProcessor(
                new PendingTmdbNfoRewriteStore(database),
                new TvShowNfoWriter());
        }

        public string SaveRoot { get; }

        public AnimeGoSqliteDatabase Database { get; }

        public PendingTmdbNfoRewriteProcessor Processor { get; }

        public static async Task<RewriteFixture> CreateAsync(bool blockedRoot)
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "animegonet-nfo-rewrite-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var saveRoot = Path.Combine(root, blockedRoot ? "blocked-save-root" : "save");
            if (blockedRoot)
            {
                await File.WriteAllTextAsync(saveRoot, "not a directory");
            }
            else
            {
                Directory.CreateDirectory(Path.Combine(saveRoot, "Fallback Anime"));
                await File.WriteAllTextAsync(
                    Path.Combine(saveRoot, "Fallback Anime", "tvshow.nfo"),
                    "<tmdbid>0</tmdbid>");
            }

            var database = new AnimeGoSqliteDatabase(Path.Combine(root, "animegonet.db"));
            await database.InitializeAsync();
            await using var connection = await database.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO pending_tmdb_nfo_rewrite_jobs (
                    id, bangumi_subject_id, tmdb_series_id, save_root_path,
                    series_directory_name, canonical_series_name, state,
                    created_at_utc, updated_at_utc)
                VALUES (
                    'rewrite-job', 547888, 700, $save_root,
                    'Fallback Anime', 'Canonical Anime', 'pending', $now, $now);
                """;
            command.Parameters.AddWithValue("$save_root", saveRoot);
            command.Parameters.AddWithValue("$now", "2026-07-28T10:00:00.0000000+00:00");
            await command.ExecuteNonQueryAsync();
            return new RewriteFixture(root, saveRoot, database);
        }

        public async Task<string> ReadStateAsync()
        {
            await using var connection = await Database.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT state FROM pending_tmdb_nfo_rewrite_jobs WHERE id = 'rewrite-job';
                """;
            return (string)(await command.ExecuteScalarAsync())!;
        }

        public async Task<string?> ReadFailureAsync()
        {
            await using var connection = await Database.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT failure_code FROM pending_tmdb_nfo_rewrite_jobs WHERE id = 'rewrite-job';
                """;
            return (string?)await command.ExecuteScalarAsync();
        }

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }

            return ValueTask.CompletedTask;
        }
    }
}
