using System.Text.Json;
using AnimeGoNet.Data.Library;

namespace AnimeGoNet.Data.Tests.Library;

public sealed class DirectoryDatabaseTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task WriterProducesUpstreamCompatibleSidecarsAndPreservesCreationState()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var saveRoot = Path.Combine(fixture.RootPath, "save");
        var media = Path.Combine(saveRoot, "Show", "S02", "E003.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(media)!);
        await File.WriteAllBytesAsync(media, [1, 2, 3]);
        var writer = new DirectoryDatabaseWriter();

        var first = await writer.WriteAsync(
            new DirectoryDatabaseWriteRequest(
                saveRoot,
                "ABCDEF",
                "Show",
                2,
                [new DirectoryDatabaseEpisodeWrite(media, 1, 3)]),
            Now);
        var second = await writer.WriteAsync(
            new DirectoryDatabaseWriteRequest(
                saveRoot,
                "123456",
                "Show",
                2,
                [new DirectoryDatabaseEpisodeWrite(media, 1, 3)],
                Seeded: true),
            Now.AddMinutes(1));

        Assert.Equal(3, first.Count);
        Assert.Equal(3, second.Count);
        var animePath = Path.Combine(saveRoot, "Show", "anime.a_json");
        var seasonPath = Path.Combine(saveRoot, "Show", "S02", "anime.s_json");
        var episodePath = Path.Combine(saveRoot, "Show", "S02", "E003.e_json");
        Assert.All([animePath, seasonPath, episodePath], path => Assert.True(File.Exists(path)));

        using var anime = JsonDocument.Parse(await File.ReadAllTextAsync(animePath));
        Assert.Equal("ABCDEF", anime.RootElement.GetProperty("info").GetProperty("hash").GetString());
        Assert.Equal(Now.ToUnixTimeSeconds(),
            anime.RootElement.GetProperty("info").GetProperty("create_at").GetInt64());
        Assert.Equal(Now.AddMinutes(1).ToUnixTimeSeconds(),
            anime.RootElement.GetProperty("info").GetProperty("update_at").GetInt64());
        using var episode = JsonDocument.Parse(await File.ReadAllTextAsync(episodePath));
        Assert.True(episode.RootElement.GetProperty("state").GetProperty("seeded").GetBoolean());
        Assert.True(episode.RootElement.GetProperty("state").GetProperty("downloaded").GetBoolean());
        Assert.True(episode.RootElement.GetProperty("state").GetProperty("renamed").GetBoolean());
        Assert.True(episode.RootElement.GetProperty("state").GetProperty("scraped").GetBoolean());
        Assert.Equal(2, episode.RootElement.GetProperty("season").GetInt32());
        Assert.Equal(1, episode.RootElement.GetProperty("type").GetInt32());
        Assert.Equal(3, episode.RootElement.GetProperty("ep").GetInt32());
    }

    [Fact]
    public async Task ScannerIndexesValidFilesAndReportsMalformedFilesWithoutAborting()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var saveRoot = Path.Combine(fixture.RootPath, "save");
        var media = Path.Combine(saveRoot, "Show", "S01", "E001.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(media)!);
        await File.WriteAllBytesAsync(media, [1]);
        await new DirectoryDatabaseWriter().WriteAsync(
            new DirectoryDatabaseWriteRequest(
                saveRoot,
                "HASH",
                "Show",
                1,
                [new DirectoryDatabaseEpisodeWrite(media, 1, 1)]),
            Now);
        var badPath = Path.Combine(saveRoot, "Broken", "bad.e_json");
        Directory.CreateDirectory(Path.GetDirectoryName(badPath)!);
        await File.WriteAllTextAsync(badPath, """{"info":{"hash":"secret"}}""");

        var result = await new DirectoryDatabaseScanner().ScanAsync(saveRoot);

        Assert.Equal(4, result.ScannedCount);
        Assert.Equal(3, result.Entries.Count);
        var issue = Assert.Single(result.Issues);
        Assert.Equal("Broken/bad.e_json", issue.RelativePath);
        Assert.Equal("directory_database_shape_invalid", issue.ErrorCode);
        Assert.Contains(result.Entries, item =>
            item.Kind == DirectoryDatabaseEntryKind.Episode
            && item.RelativePath == "Show/S01/E001.e_json"
            && item.EpisodeNumber == 1);
    }

    [Fact]
    public async Task RefreshReplacesIndexAndPersistsAuditableIssues()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var saveRoot = Path.Combine(fixture.RootPath, "save");
        Directory.CreateDirectory(saveRoot);
        var writer = new DirectoryDatabaseWriter();
        var media = Path.Combine(saveRoot, "Show", "S01", "E001.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(media)!);
        await File.WriteAllBytesAsync(media, [1]);
        await writer.WriteAsync(
            new DirectoryDatabaseWriteRequest(
                saveRoot,
                "HASH",
                "Show",
                1,
                [new DirectoryDatabaseEpisodeWrite(media, 1, 1)]),
            Now);
        var badPath = Path.Combine(saveRoot, "bad.e_json");
        await File.WriteAllTextAsync(badPath, "not json");
        var store = new DirectoryDatabaseIndexStore(
            fixture.Database,
            new DirectoryDatabaseScanner());

        var result = await store.RefreshAsync(saveRoot, Now);

        Assert.Equal(4, result.ScannedCount);
        Assert.Equal(3, result.IndexedCount);
        Assert.Equal(1, result.RejectedCount);
        await using (var connection = await fixture.Database.OpenConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT
                    (SELECT COUNT(*) FROM directory_database_entries),
                    (SELECT COUNT(*) FROM directory_database_scan_issues WHERE run_id = $run),
                    (SELECT status FROM directory_database_scan_runs WHERE id = $run);
                """;
            command.Parameters.AddWithValue("$run", result.RunId);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(3, reader.GetInt32(0));
            Assert.Equal(1, reader.GetInt32(1));
            Assert.Equal("completed", reader.GetString(2));
        }

        File.Delete(Path.Combine(saveRoot, "Show", "S01", "E001.e_json"));
        var next = await store.RefreshAsync(saveRoot, Now.AddHours(1));
        Assert.Equal(2, next.IndexedCount);
        await using var verifyConnection = await fixture.Database.OpenConnectionAsync();
        await using var count = verifyConnection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM directory_database_entries;";
        Assert.Equal(2L, await count.ExecuteScalarAsync());
    }

    [Fact]
    public async Task WriterRejectsMediaOutsideCapturedSaveRoot()
    {
        await using var fixture = await SqliteDatabaseFixture.CreateAsync();
        var saveRoot = Path.Combine(fixture.RootPath, "save");
        Directory.CreateDirectory(saveRoot);

        await Assert.ThrowsAsync<IOException>(() =>
            new DirectoryDatabaseWriter().WriteAsync(
                new DirectoryDatabaseWriteRequest(
                    saveRoot,
                    "HASH",
                    "Show",
                    1,
                    [new DirectoryDatabaseEpisodeWrite(
                        Path.Combine(fixture.RootPath, "outside.mkv"),
                        1,
                        1)]),
                Now));
    }
}
