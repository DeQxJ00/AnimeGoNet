using AnimeGoNet.App.Metadata;
using AnimeGoNet.Core.Metadata;
using AnimeGoNet.Data.Metadata;
using AnimeGoNet.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeGoNet.App.Tests.Metadata;

public sealed class BangumiArchiveCachingClientTests
{
    [Fact]
    public async Task DefaultApplicationCompositionUsesActiveArchive()
    {
        await using var app = await RunningApp.StartAsync();
        var database = app.App.Services
            .GetRequiredService<AnimeGoSqliteDatabase>();
        await ArchiveFixture.SeedAsync(
            database,
            episodeCount: 1,
            storedEpisodeCount: 1);

        var subjects = app.App.Services
            .GetRequiredService<IBangumiSubjectClient>();
        var episodes = app.App.Services
            .GetRequiredService<IBangumiEpisodeClient>();

        Assert.Equal(
            "Archive Subject",
            (await subjects.GetSubjectAsync(51))?.Name);
        Assert.Equal(1001, Assert.Single(
            await episodes.GetEpisodesAsync(51)).Id);
    }

    [Fact]
    public async Task VersionOneArchiveServesSubjectAndEpisodesButNotRelations()
    {
        await using var fixture = await ArchiveFixture.CreateAsync(
            episodeCount: 1,
            storedEpisodeCount: 1);
        var upstream = new RecordingBangumiClient();
        using var client = new BangumiArchiveCachingClient(
            fixture.Store,
            upstream,
            upstream);

        var subject = Assert.IsType<BangumiSubject>(
            await client.GetSubjectAsync(51));
        var episodes = await client.GetEpisodesAsync(51);
        var relations = await client.GetRelatedSubjectsAsync(51);

        Assert.Equal("Archive Subject", subject.Name);
        Assert.Equal(1001, Assert.Single(episodes).Id);
        Assert.Equal(9001, Assert.Single(relations).Id);
        Assert.Equal(0, upstream.SubjectCalls);
        Assert.Equal(0, upstream.EpisodeCalls);
        Assert.Equal(1, upstream.RelationCalls);
    }

    [Fact]
    public async Task VersionTwoArchiveServesRelationsWithoutOnlineRequest()
    {
        await using var fixture = await ArchiveFixture.CreateAsync(
            episodeCount: 1,
            storedEpisodeCount: 1,
            schemaVersion: 2,
            seedRelation: true);
        var upstream = new RecordingBangumiClient();
        using var client = new BangumiArchiveCachingClient(
            fixture.Store,
            upstream,
            upstream);

        var relations = await client.GetRelatedSubjectsAsync(51);

        var prequel = Assert.Single(relations);
        Assert.Equal(52, prequel.Id);
        Assert.Equal("前传", prequel.Relation);
        Assert.Equal(0, upstream.RelationCalls);
    }

    [Fact]
    public async Task MissingSubjectFallsBackToOnlineClients()
    {
        await using var fixture = await ArchiveFixture.CreateAsync(
            episodeCount: 1,
            storedEpisodeCount: 1);
        var upstream = new RecordingBangumiClient();
        using var client = new BangumiArchiveCachingClient(
            fixture.Store,
            upstream,
            upstream);

        var subject = Assert.IsType<BangumiSubject>(
            await client.GetSubjectAsync(52));
        var episodes = await client.GetEpisodesAsync(52);

        Assert.Equal("Online Subject", subject.Name);
        Assert.Equal(8001, Assert.Single(episodes).Id);
        Assert.Equal(1, upstream.SubjectCalls);
        Assert.Equal(1, upstream.EpisodeCalls);
    }

    [Fact]
    public async Task IncompleteArchiveSubjectIsUsableButEpisodesRefreshOnline()
    {
        await using var fixture = await ArchiveFixture.CreateAsync(
            episodeCount: 2,
            storedEpisodeCount: 1);
        var upstream = new RecordingBangumiClient();
        using var client = new BangumiArchiveCachingClient(
            fixture.Store,
            upstream,
            upstream);

        var subject = Assert.IsType<BangumiSubject>(
            await client.GetSubjectAsync(51));
        var episodes = await client.GetEpisodesAsync(51);

        Assert.Equal("Archive Subject", subject.Name);
        Assert.Equal(8001, Assert.Single(episodes).Id);
        Assert.Equal(0, upstream.SubjectCalls);
        Assert.Equal(1, upstream.EpisodeCalls);
    }

    [Fact]
    public async Task EmptyUnknownEpisodeCountDoesNotHideNewOnlineEpisodes()
    {
        await using var fixture = await ArchiveFixture.CreateAsync(
            episodeCount: 0,
            storedEpisodeCount: 0);
        var upstream = new RecordingBangumiClient();
        using var client = new BangumiArchiveCachingClient(
            fixture.Store,
            upstream,
            upstream);

        var episodes = await client.GetEpisodesAsync(51);

        Assert.Equal(8001, Assert.Single(episodes).Id);
        Assert.Equal(1, upstream.EpisodeCalls);
    }

    private sealed class RecordingBangumiClient
        : IBangumiSubjectClient, IBangumiEpisodeClient
    {
        public int SubjectCalls { get; private set; }

        public int RelationCalls { get; private set; }

        public int EpisodeCalls { get; private set; }

        public Task<BangumiSubject?> GetSubjectAsync(
            int subjectId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SubjectCalls++;
            return Task.FromResult<BangumiSubject?>(new BangumiSubject(
                subjectId,
                "Online Subject",
                "在线作品",
                new DateOnly(2026, 7, 30),
                1));
        }

        public Task<IReadOnlyList<BangumiSubjectRelation>>
            GetRelatedSubjectsAsync(
                int subjectId,
                CancellationToken cancellationToken = default)
        {
            _ = subjectId;
            cancellationToken.ThrowIfCancellationRequested();
            RelationCalls++;
            return Task.FromResult<IReadOnlyList<BangumiSubjectRelation>>(
            [
                new(
                    9001,
                    2,
                    "Relation",
                    "关系",
                    "前传"),
            ]);
        }

        public Task<IReadOnlyList<BangumiEpisode>> GetEpisodesAsync(
            int subjectId,
            CancellationToken cancellationToken = default)
        {
            _ = subjectId;
            cancellationToken.ThrowIfCancellationRequested();
            EpisodeCalls++;
            return Task.FromResult<IReadOnlyList<BangumiEpisode>>(
            [
                new(
                    8001,
                    0,
                    1,
                    new DateOnly(2026, 7, 30)),
            ]);
        }
    }

    private sealed class ArchiveFixture : IAsyncDisposable
    {
        private readonly string _root;

        private ArchiveFixture(string root, AnimeGoSqliteDatabase database)
        {
            _root = root;
            Store = new BangumiArchiveStore(database);
        }

        public BangumiArchiveStore Store { get; }

        public static async Task<ArchiveFixture> CreateAsync(
            int episodeCount,
            int storedEpisodeCount,
            int schemaVersion = 1,
            bool seedRelation = false)
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "animegonet-bangumi-cache-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var database = new AnimeGoSqliteDatabase(
                Path.Combine(root, "animegonet.db"));
            await database.InitializeAsync();
            await SeedAsync(
                database,
                episodeCount,
                storedEpisodeCount,
                schemaVersion,
                seedRelation);
            return new ArchiveFixture(root, database);
        }

        public static async Task SeedAsync(
            AnimeGoSqliteDatabase database,
            int episodeCount,
            int storedEpisodeCount,
            int schemaVersion = 1,
            bool seedRelation = false)
        {
            await using var connection = await database.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO data_update_versions (
                    data_version, schema_version, generated_at_utc,
                    minimum_client_version, manifest_sha256,
                    upstream_repository, upstream_release, upstream_asset,
                    upstream_sha256, subject_count, episode_count, state,
                    installed_at_utc, activated_at_utc)
                VALUES (
                    '2026.07.30.1', $schema_version, $now, '0.1.0', $sha,
                    'https://github.com/bangumi/Archive', 'test', 'test.zip',
                    $sha, 1, 1, 'active', $now, $now);

                UPDATE data_update_state
                SET active_version = '2026.07.30.1', updated_at_utc = $now
                WHERE singleton = 1;

                INSERT INTO bangumi_archive_subjects (
                    data_version, subject_id, name, name_cn, air_date,
                    episode_count)
                VALUES (
                    '2026.07.30.1', 51, 'Archive Subject', '归档作品',
                    '2026-07-30', $episode_count);
                """;
            command.Parameters.AddWithValue(
                "$now",
                "2026-07-30T00:00:00.0000000+00:00");
            command.Parameters.AddWithValue("$sha", new string('a', 64));
            command.Parameters.AddWithValue("$schema_version", schemaVersion);
            command.Parameters.AddWithValue("$episode_count", episodeCount);
            await command.ExecuteNonQueryAsync();

            for (var index = 1; index <= storedEpisodeCount; index++)
            {
                await using var episode = connection.CreateCommand();
                episode.CommandText = """
                    INSERT INTO bangumi_archive_episodes (
                        data_version, episode_id, subject_id, sort_number,
                        episode_number, air_date)
                    VALUES (
                        '2026.07.30.1', $id, 51, $sort, $number,
                        '2026-07-30');
                    """;
                episode.Parameters.AddWithValue("$id", 1000 + index);
                episode.Parameters.AddWithValue("$sort", index);
                episode.Parameters.AddWithValue(
                    "$number",
                    index.ToString(
                        System.Globalization.CultureInfo.InvariantCulture));
                await episode.ExecuteNonQueryAsync();
            }

            if (seedRelation)
            {
                await using var relation = connection.CreateCommand();
                relation.CommandText = """
                    INSERT INTO bangumi_archive_subjects (
                        data_version, subject_id, name, name_cn, air_date,
                        episode_count)
                    VALUES (
                        '2026.07.30.1', 52, 'Archive Prequel', '归档前传',
                        '2025-01-01', 1);

                    INSERT INTO bangumi_archive_subject_relations (
                        data_version, subject_id, related_subject_id,
                        relation_type, relation_order)
                    VALUES ('2026.07.30.1', 51, 52, 2, 0);
                    """;
                await relation.ExecuteNonQueryAsync();
            }
        }

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }

            return ValueTask.CompletedTask;
        }
    }
}
