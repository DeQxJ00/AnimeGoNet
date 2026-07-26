using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Downloads;
using AnimeGoNet.Core.Ingest;
using AnimeGoNet.Core.Metadata;
using AnimeGoNet.Core.Torrents;
using AnimeGoNet.Data.Ingest;
using AnimeGoNet.Data.Mikan;
using AnimeGoNet.Data.Metadata;
using AnimeGoNet.Data.Sources;

namespace AnimeGoNet.Data.Tests.Metadata;

public sealed class MetadataResolutionStoreTests
{
    [Fact]
    public async Task ConcurrentClaimsReturnDownloadedTaskAtMostOnce()
    {
        await using var fixture = await MetadataFixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;

        var claims = await Task.WhenAll(
            fixture.Store.TryClaimNextDownloadedAsync(now, TimeSpan.FromMinutes(1)),
            fixture.Store.TryClaimNextDownloadedAsync(now, TimeSpan.FromMinutes(1)));

        Assert.Single(claims, claim => claim is not null);
    }

    [Fact]
    public async Task ManualClaimRequiresEnabledCompleteTmdbOverride()
    {
        await using var fixture = await MetadataFixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;

        Assert.Null(await fixture.Store.TryClaimNextManualOverrideAsync(now, TimeSpan.FromMinutes(1)));
        var rules = new MikanWorkMetadataRuleStore(fixture.Database);
        await rules.SaveAsync(
            new MikanWorkMetadataRuleUpdate(3951, 547888, 72517, 2, null),
            expectedRevision: 0,
            now);

        var claim = Assert.IsType<MetadataTaskClaim>(await fixture.Store.TryClaimNextManualOverrideAsync(
            now,
            TimeSpan.FromMinutes(1)));
        Assert.Equal(3951, claim.MikanId);
        Assert.Equal(547888, claim.BangumiSubjectId);
        Assert.Equal(999, claim.AniDbAnimeId);
        Assert.Equal("tt1234567", claim.ImdbTitleId);
    }

    [Fact]
    public async Task CompletingSeasonPersistsCanonicalLibraryAndTaskProjectionAtomically()
    {
        await using var fixture = await MetadataFixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var claim = Assert.IsType<MetadataTaskClaim>(await fixture.Store.TryClaimNextDownloadedAsync(
            now,
            TimeSpan.FromMinutes(1)));
        Assert.Single(claim.Files!);
        await fixture.Store.RecordAttemptAsync(
            claim,
            new MetadataAttempt("series", "tmdb_title", null, "matched", null, false, 1, 12),
            now);
        var series = new TmdbSeries(72517, "来自深渊", "メイドインアビス", new DateOnly(2017, 7, 7));
        var season = new TmdbSeason(204984, 72517, 2, "烈日的黄金乡", new DateOnly(2022, 7, 6), 12);

        await fixture.Store.CompleteSeasonAsync(claim, series, season, now);

        var run = Assert.IsType<MetadataRunProjection>(await fixture.Store.GetLatestAsync(fixture.TaskId));
        Assert.Equal("season_resolved", run.Status);
        Assert.Equal(72517, run.TmdbSeriesId);
        Assert.Equal(2, run.TmdbSeasonNumber);
        Assert.True(run.TmdbAccessConfirmed);
        await using var connection = await fixture.Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ingest_tasks.status, anime_series.canonical_name,
                   anime_seasons.season_number, task_files.tmdb_series_id,
                   task_files.tmdb_season_number
            FROM ingest_tasks
            JOIN task_files ON task_files.task_id = ingest_tasks.id
            JOIN anime_series ON anime_series.tmdb_series_id = task_files.tmdb_series_id
            JOIN anime_seasons ON anime_seasons.series_id = anime_series.id
                              AND anime_seasons.season_number = task_files.tmdb_season_number
            WHERE ingest_tasks.id = $task_id AND task_files.disposition = 'pending';
            """;
        command.Parameters.AddWithValue("$task_id", fixture.TaskId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("metadata_season_resolved", reader.GetString(0));
        Assert.Equal("来自深渊", reader.GetString(1));
        Assert.Equal(2, reader.GetInt32(2));
        Assert.Equal(72517, reader.GetInt32(3));
        Assert.Equal(2, reader.GetInt32(4));
    }

    [Fact]
    public async Task AiSeasonSeedsVerifiedEpisodeForEpisodeClaim()
    {
        await using var fixture = await MetadataFixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var claim = Assert.IsType<MetadataTaskClaim>(
            await fixture.Store.TryClaimNextDownloadedAsync(
                now,
                TimeSpan.FromMinutes(1)));
        await fixture.Store.RecordAttemptAsync(
            claim,
            new MetadataAttempt(
                "season",
                "ai_season",
                null,
                "matched",
                null,
                false,
                claim.AttemptNumber,
                10),
            now);
        await fixture.Store.CompleteAiSeasonAsync(
            claim,
            new TmdbSeries(72517, "来自深渊", "メイドインアビス", null),
            new TmdbSeason(204984, 72517, 2, "Season 2", null, 12),
            [new MetadataSeasonFileSeed("episode.mkv", 7, null)],
            now);

        var episodeClaim = Assert.IsType<MetadataEpisodeTaskClaim>(
            await fixture.Store.TryClaimNextSeasonResolvedAsync(
                now.AddSeconds(1),
                TimeSpan.FromMinutes(1)));

        Assert.True(episodeClaim.SeasonResolvedByAi);
        var file = Assert.Single(episodeClaim.Files);
        Assert.Equal(7, file.PreResolvedEpisodeNumber);
        Assert.Null(file.PreResolvedOtherReason);
    }

    [Fact]
    public async Task ExpiredLeaseIsAuditedAndCanBeClaimedAsNextAttempt()
    {
        await using var fixture = await MetadataFixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var first = Assert.IsType<MetadataTaskClaim>(await fixture.Store.TryClaimNextDownloadedAsync(
            now,
            TimeSpan.FromSeconds(1)));

        var second = Assert.IsType<MetadataTaskClaim>(await fixture.Store.TryClaimNextDownloadedAsync(
            now.AddSeconds(2),
            TimeSpan.FromMinutes(1)));

        Assert.Equal(first.TaskId, second.TaskId);
        Assert.Equal(2, second.AttemptNumber);
        Assert.NotEqual(first.LeaseToken, second.LeaseToken);
    }

    [Fact]
    public async Task NonAuthoritativeFailureCannotBeMarkedFallbackEligible()
    {
        await using var fixture = await MetadataFixture.CreateAsync();
        var claim = Assert.IsType<MetadataTaskClaim>(await fixture.Store.TryClaimNextDownloadedAsync(
            DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(1)));
        var failure = new MetadataFailure(MetadataFailureKind.Network, "tmdb_network_error", false);

        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Store.FailAsync(
            claim,
            failure,
            fallbackEligible: true,
            "network_failure",
            DateTimeOffset.UtcNow));

        await fixture.Store.FailAsync(
            claim,
            failure,
            fallbackEligible: false,
            "tmdb_access_not_confirmed",
            DateTimeOffset.UtcNow);
        var run = Assert.IsType<MetadataRunProjection>(await fixture.Store.GetLatestAsync(fixture.TaskId));
        Assert.Equal(MetadataFailureKind.Network, run.FailureKind);
        Assert.False(run.FallbackEligible);
        Assert.False(run.TmdbAccessConfirmed);
    }

    [Fact]
    public async Task FailedTaskCanBeRetriedWithoutDeletingResolutionHistory()
    {
        await using var fixture = await MetadataFixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var first = Assert.IsType<MetadataTaskClaim>(await fixture.Store.TryClaimNextDownloadedAsync(
            now,
            TimeSpan.FromMinutes(1)));
        await fixture.Store.FailAsync(
            first,
            new MetadataFailure(MetadataFailureKind.SemanticNoMatch, "tmdb_series_not_found", true),
            fallbackEligible: false,
            "manual_override_active",
            now);

        Assert.Equal(MetadataRetryResult.Retried, await fixture.Store.RetryFailedAsync(
            fixture.TaskId,
            now.AddSeconds(1)));
        var second = Assert.IsType<MetadataTaskClaim>(await fixture.Store.TryClaimNextDownloadedAsync(
            now.AddSeconds(2),
            TimeSpan.FromMinutes(1)));

        Assert.Equal(2, second.AttemptNumber);
        await using var connection = await fixture.Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM metadata_resolution_runs WHERE task_id = $task_id;";
        command.Parameters.AddWithValue("$task_id", fixture.TaskId);
        Assert.Equal(2L, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task RetryRejectsTaskThatIsNotFailed()
    {
        await using var fixture = await MetadataFixture.CreateAsync();

        Assert.Equal(MetadataRetryResult.InvalidState, await fixture.Store.RetryFailedAsync(
            fixture.TaskId,
            DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task RetryRejectsTaskWithActiveResolutionLease()
    {
        await using var fixture = await MetadataFixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        Assert.NotNull(await fixture.Store.TryClaimNextDownloadedAsync(now, TimeSpan.FromMinutes(1)));

        Assert.Equal(MetadataRetryResult.ActiveLease, await fixture.Store.RetryFailedAsync(
            fixture.TaskId,
            now.AddSeconds(1)));
    }

    [Fact]
    public async Task ConcurrentEpisodeClaimsReturnSeasonResolvedTaskAtMostOnce()
    {
        await using var fixture = await MetadataFixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var seasonClaim = Assert.IsType<MetadataTaskClaim>(await fixture.Store.TryClaimNextDownloadedAsync(
            now,
            TimeSpan.FromMinutes(1)));
        await fixture.Store.CompleteSeasonAsync(
            seasonClaim,
            new TmdbSeries(72517, "来自深渊", "メイドインアビス", new DateOnly(2017, 7, 7)),
            new TmdbSeason(204984, 72517, 2, "烈日的黄金乡", new DateOnly(2022, 7, 6), 12),
            now);

        var claims = await Task.WhenAll(
            fixture.Store.TryClaimNextSeasonResolvedAsync(now, TimeSpan.FromMinutes(1)),
            fixture.Store.TryClaimNextSeasonResolvedAsync(now, TimeSpan.FromMinutes(1)));

        var episodeClaim = Assert.Single(claims, value => value is not null)!;
        Assert.Equal(72517, episodeClaim.TmdbSeriesId);
        Assert.Equal(2, episodeClaim.TmdbSeasonNumber);
        Assert.Single(episodeClaim.Files);
        Assert.Equal(2, episodeClaim.Resolution.AttemptNumber);
        Assert.Equal(999, episodeClaim.Resolution.AniDbAnimeId);
        Assert.Equal("tt1234567", episodeClaim.Resolution.ImdbTitleId);
    }

    private sealed class MetadataFixture : IAsyncDisposable
    {
        private readonly SqliteDatabaseFixture _databaseFixture;

        private MetadataFixture(SqliteDatabaseFixture databaseFixture, string taskId)
        {
            _databaseFixture = databaseFixture;
            Database = databaseFixture.Database;
            Store = new MetadataResolutionStore(Database);
            TaskId = taskId;
        }

        public AnimeGoNet.Data.Sqlite.AnimeGoSqliteDatabase Database { get; }

        public MetadataResolutionStore Store { get; }

        public string TaskId { get; }

        public static async Task<MetadataFixture> CreateAsync()
        {
            var databaseFixture = await SqliteDatabaseFixture.CreateAsync();
            var profiles = new SourceProfileStore(databaseFixture.Database);
            await profiles.EnsureSeedsAsync(AnimeGoDefaults.CreateDocker().InitialSourceProfiles);
            var profile = Assert.IsType<SourceProfileRecord>(await profiles.GetEnabledAsync("mikan"));
            var normalized = Assert.IsType<NormalizedIngestItem>(IngestCommandNormalizer.Normalize(
                "mikan",
                new IngestItemCommand(
                    "https://mikanani.me/passkey/file.torrent",
                    new IngestItemInfo(
                        "Episode",
                        null,
                        "one",
                        "3951",
                        null,
                        null,
                        3951,
                        547888,
                        999,
                        "tt1234567"))).Item);
            var hash = new string('e', 40);
            var tasks = new IngestTaskStore(databaseFixture.Database);
            var staged = await tasks.AddStagedAsync(
                normalized,
                profile,
                new TorrentMetadata("episode.mkv", hash, 100, [new TorrentFile("episode.mkv", 100, false)]),
                "metadata.torrent",
                DateTimeOffset.UtcNow.AddMinutes(15));
            var dispatch = Assert.IsType<ClaimedStagedTorrentRecord>(await tasks.TryClaimNextStagedAsync(
                DateTimeOffset.UtcNow,
                TimeSpan.FromMinutes(1)));
            await tasks.CompleteDispatchAsync(
                dispatch,
                new DownloadTaskSnapshot(hash, "Episode", DownloadTaskState.Waiting, 0, 0, 100, 0, null),
                "/download/incomplete/bt",
                "/download/anime",
                DateTimeOffset.UtcNow);
            await new AnimeGoNet.Data.Downloads.DownloadJobStore(databaseFixture.Database).ApplyInstanceSnapshotAsync(
                "bt",
                [new DownloadTaskSnapshot(hash, "Episode", DownloadTaskState.Complete, 1, 100, 100, 0, 0)],
                DateTimeOffset.UtcNow);
            return new MetadataFixture(databaseFixture, staged.Id);
        }

        public ValueTask DisposeAsync() => _databaseFixture.DisposeAsync();
    }
}
