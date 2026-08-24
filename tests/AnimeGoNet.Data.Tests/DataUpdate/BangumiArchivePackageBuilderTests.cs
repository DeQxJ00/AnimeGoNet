using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AnimeGoNet.Core.DataUpdate;
using AnimeGoNet.Data.DataUpdate;
using AnimeGoNet.Data.Metadata;
using AnimeGoNet.DataBuilder;

namespace AnimeGoNet.Data.Tests.DataUpdate;

public sealed class BangumiArchivePackageBuilderTests
{
    private static readonly string[] ManifestEntryName = ["manifest.json"];

    [Fact]
    public async Task BuildsShardedPackageThatImportsIntoTheProductionStore()
    {
        var root = CreateRoot();
        try
        {
            var archivePath = await WriteArchiveAsync(root);
            var hash = await Sha256Async(archivePath);
            var output = Path.Combine(root, "package");

            var result = await BangumiArchivePackageBuilder.BuildAsync(
                Options(archivePath, output, hash));

            Assert.Equal(3, result.Manifest.SubjectCount);
            Assert.Equal(3, result.Manifest.EpisodeCount);
            Assert.Equal(2, result.Manifest.RelationCount);
            Assert.Equal(7, result.Manifest.Assets.Count);
            foreach (var asset in result.Manifest.Assets)
            {
                Assert.True(File.Exists(Path.Combine(output, asset.FileName)));
                Assert.Equal(
                    asset.Sha256,
                    await Sha256Async(Path.Combine(output, asset.FileName)));
            }
            var parsedManifest = DataManifestParser.Parse(
                await File.ReadAllBytesAsync(result.ManifestPath));
            Assert.Equal(result.Manifest.DataVersion, parsedManifest.DataVersion);
            Assert.Equal(result.Manifest.Upstream, parsedManifest.Upstream);
            Assert.Equal(result.Manifest.Assets, parsedManifest.Assets);
            Assert.Equal(result.Manifest.SubjectCount, parsedManifest.SubjectCount);
            Assert.Equal(result.Manifest.EpisodeCount, parsedManifest.EpisodeCount);
            Assert.Equal(result.Manifest.RelationCount, parsedManifest.RelationCount);

            var subjectLines = await ReadAssetsAsync(
                output,
                result.Manifest.Assets.Where(asset => asset.Kind == DataAssetKind.Subjects));
            var episodeLines = await ReadAssetsAsync(
                output,
                result.Manifest.Assets.Where(asset => asset.Kind == DataAssetKind.Episodes));
            var relationLines = await ReadAssetsAsync(
                output,
                result.Manifest.Assets.Where(asset => asset.Kind == DataAssetKind.Relations));
            Assert.Equal([2, 3, 4], subjectLines.Select(line => line.GetProperty("id").GetInt32()).ToArray());
            Assert.Equal([20, 21, 30], episodeLines.Select(line => line.GetProperty("id").GetInt32()).ToArray());
            var subjectIds = subjectLines
                .Select(line => line.GetProperty("id").GetInt32())
                .ToHashSet();
            Assert.All(
                episodeLines,
                line => Assert.Contains(line.GetProperty("subject_id").GetInt32(), subjectIds));
            Assert.Equal(["2", "1.5", "1"], episodeLines.Select(line => line.GetProperty("episode").GetString()!).ToArray());
            Assert.Equal([2, 1, 1], episodeLines.Select(line => line.GetProperty("sort").GetInt32()).ToArray());
            Assert.Equal(JsonValueKind.Null, episodeLines[1].GetProperty("air_date").ValueKind);
            Assert.Equal(2, subjectLines[0].GetProperty("episode_count").GetInt32());
            Assert.Equal(0, subjectLines[2].GetProperty("episode_count").GetInt32());
            Assert.Equal([2, 3], relationLines.Select(line => line.GetProperty("subject_id").GetInt32()).ToArray());
            Assert.Equal([3, 2], relationLines.Select(line => line.GetProperty("related_subject_id").GetInt32()).ToArray());
            Assert.Equal([3, 2], relationLines.Select(line => line.GetProperty("relation_type").GetInt32()).ToArray());

            using (var package = ZipFile.OpenRead(result.OfflinePackagePath))
            {
                Assert.Equal(
                    ManifestEntryName.Concat(
                        result.Manifest.Assets.Select(asset => asset.FileName).Order(StringComparer.Ordinal)),
                    package.Entries.Select(entry => entry.FullName));
            }
            Assert.Equal("SHA256SUMS", Path.GetFileName(result.ReleaseChecksumsPath));
            var checksumLines = await File.ReadAllLinesAsync(result.ReleaseChecksumsPath);
            var expectedReleaseFiles = new[]
                {
                    Path.GetFileName(result.OfflinePackagePath),
                    Path.GetFileName(result.ManifestPath),
                }
                .Concat(result.Manifest.Assets.Select(asset => asset.FileName))
                .Order(StringComparer.Ordinal)
                .ToArray();
            Assert.Equal(expectedReleaseFiles.Length, checksumLines.Length);
            Assert.Equal(
                expectedReleaseFiles,
                checksumLines.Select(line => line[66..]).ToArray());
            foreach (var line in checksumLines)
            {
                Assert.Matches("^[0-9a-f]{64}  [^/\\\\]+$", line);
                var name = line[66..];
                Assert.Equal(
                    line[..64],
                    await Sha256Async(Path.Combine(result.OutputDirectory, name)));
            }

            await using var database = await SqliteDatabaseFixture.CreateAsync();
            using var store = new DataPackageStore(database.Database);
            var imported = await store.ImportAsync(new DataPackageImportRequest(
                result.Manifest,
                result.ManifestSha256,
                output,
                new Version(1, 0, 0),
                2,
                new DateTimeOffset(2026, 8, 8, 1, 0, 0, TimeSpan.Zero)));
            var snapshot = Assert.IsType<BangumiArchiveSnapshot>(
                await new BangumiArchiveStore(database.Database).GetAsync(2));

            Assert.Equal(3, imported.SubjectCount);
            Assert.Equal(3, imported.EpisodeCount);
            Assert.True(snapshot.HasCompleteEpisodeSet);
            Assert.Equal([1.5m, 2m], snapshot.Episodes.Select(episode => episode.EpisodeNumber).ToArray());
            var relations = Assert.IsAssignableFrom<IReadOnlyList<AnimeGoNet.Core.Metadata.BangumiSubjectRelation>>(
                await new BangumiArchiveStore(database.Database).GetRelatedSubjectsAsync(3));
            var prequel = Assert.Single(relations);
            Assert.Equal(2, prequel.Id);
            Assert.Equal("前传", prequel.Relation);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SameInputsProduceByteIdenticalReleaseArtifacts()
    {
        var root = CreateRoot();
        try
        {
            var archivePath = await WriteArchiveAsync(root);
            var hash = await Sha256Async(archivePath);
            var first = await BangumiArchivePackageBuilder.BuildAsync(
                Options(archivePath, Path.Combine(root, "first"), hash));
            var second = await BangumiArchivePackageBuilder.BuildAsync(
                Options(archivePath, Path.Combine(root, "second"), hash));

            var firstFiles = Directory.GetFiles(first.OutputDirectory)
                .ToDictionary(path => Path.GetFileName(path)!, Sha256Async, StringComparer.Ordinal);
            var secondFiles = Directory.GetFiles(second.OutputDirectory)
                .ToDictionary(path => Path.GetFileName(path)!, Sha256Async, StringComparer.Ordinal);
            Assert.Equal(firstFiles.Keys.Order(), secondFiles.Keys.Order());
            foreach (var name in firstFiles.Keys)
            {
                Assert.Equal(await firstFiles[name], await secondFiles[name]);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task HashMismatchLeavesNoOutputOrPartialDirectory()
    {
        var root = CreateRoot();
        try
        {
            var archivePath = await WriteArchiveAsync(root);
            var output = Path.Combine(root, "package");

            var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
                BangumiArchivePackageBuilder.BuildAsync(
                    Options(archivePath, output, new string('0', 64))));

            Assert.Contains("SHA-256", exception.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(output));
            Assert.Empty(Directory.GetDirectories(root, ".package.partial-*"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(4, 1, "anime Subjects")]
    [InlineData(1, 4, "normal anime Episodes")]
    public async Task ConfiguredProductionCountFloorRejectsTruncatedArchive(
        int minimumSubjects,
        int minimumEpisodes,
        string expectedKind)
    {
        var root = CreateRoot();
        try
        {
            var archivePath = await WriteArchiveAsync(root);
            var hash = await Sha256Async(archivePath);
            var output = Path.Combine(root, "package");

            var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
                BangumiArchivePackageBuilder.BuildAsync(
                    Options(
                        archivePath,
                        output,
                        hash,
                        minimumSubjects,
                        minimumEpisodes)));

            Assert.Contains(expectedKind, exception.Message, StringComparison.Ordinal);
            Assert.Contains("configured minimum", exception.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(output));
            Assert.Empty(Directory.GetDirectories(root, ".package.partial-*"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DuplicateAnimeSubjectIdIsRejectedBeforeOutputIsExposed()
    {
        var root = CreateRoot();
        try
        {
            var archivePath = await WriteArchiveAsync(
                root,
                """
                {"id":1,"type":2,"name":"One","name_cn":null,"date":null}
                {"id":1,"type":2,"name":"Duplicate","name_cn":null,"date":null}
                """,
                """
                {"id":10,"type":0,"subject_id":1,"sort":1,"airdate":null}
                """);
            var output = Path.Combine(root, "package");
            var hash = await Sha256Async(archivePath);

            var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
                BangumiArchivePackageBuilder.BuildAsync(
                    Options(archivePath, output, hash)));

            Assert.Contains("duplicate anime Subject ID", exception.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(output));
            Assert.Empty(Directory.GetDirectories(root, ".package.partial-*"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DuplicateNormalEpisodeIdIsRejectedBeforeOutputIsExposed()
    {
        var root = CreateRoot();
        try
        {
            var archivePath = await WriteArchiveAsync(
                root,
                """
                {"id":1,"type":2,"name":"One","name_cn":null,"date":null}
                """,
                """
                {"id":10,"type":0,"subject_id":1,"sort":1,"airdate":null}
                {"id":10,"type":0,"subject_id":1,"sort":2,"airdate":null}
                """);
            var output = Path.Combine(root, "package");
            var hash = await Sha256Async(archivePath);

            var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
                BangumiArchivePackageBuilder.BuildAsync(
                    Options(archivePath, output, hash)));

            Assert.Contains("duplicate normal Episode ID", exception.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(output));
            Assert.Empty(Directory.GetDirectories(root, ".package.partial-*"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task OversizedJsonLineIsRejectedBeforeOutputIsExposed()
    {
        var root = CreateRoot();
        try
        {
            var archivePath = Path.Combine(root, "dump-fixture.zip");
            await using (var file = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true))
            {
                await WriteEntryAsync(
                    archive,
                    "subject.jsonlines",
                    "{\"id\":1,\"type\":2,\"name\":\""
                    + new string('a', (8 * 1024 * 1024) + 1)
                    + "\",\"name_cn\":null,\"date\":null}");
                await WriteEntryAsync(
                    archive,
                    "episode.jsonlines",
                    "{\"id\":1,\"type\":0,\"subject_id\":1,\"sort\":1,\"airdate\":null}");
                await WriteEntryAsync(
                    archive,
                    "subject-relations.jsonlines",
                    "{\"subject_id\":1,\"related_subject_id\":1,\"relation_type\":2,\"order\":0}");
            }
            var output = Path.Combine(root, "package");
            var hash = await Sha256Async(archivePath);

            var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
                BangumiArchivePackageBuilder.BuildAsync(
                    Options(archivePath, output, hash)));

            Assert.Contains("too large", exception.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(output));
            Assert.Empty(Directory.GetDirectories(root, ".package.partial-*"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static BangumiArchiveBuildOptions Options(
        string archivePath,
        string output,
        string hash,
        int minimumSubjects = 1,
        int minimumEpisodes = 1) =>
        new(
            archivePath,
            output,
            "2026.08.04.1",
            new Uri("https://updates.example.invalid/2026.08.04.1/"),
            "https://github.com/bangumi/Archive",
            "archive",
            "dump-fixture.zip",
            hash,
            new DateTimeOffset(2026, 8, 4, 21, 5, 3, TimeSpan.Zero),
            "0.1.0",
            SubjectsPerShard: 1,
            MinimumSubjectCount: minimumSubjects,
            MinimumEpisodeCount: minimumEpisodes);

    private static async Task<string> WriteArchiveAsync(string root)
    {
        return await WriteArchiveAsync(
            root,
            """
            {"id":4,"type":2,"name":"No Episodes","name_cn":"","date":""}
            {"id":1,"type":1,"name":"Book","name_cn":"书","date":"2020-01-01"}
            {"id":3,"type":2,"name":"Third","name_cn":null,"date":"2024-02-30"}
            {"id":2,"type":2,"name":"Second","name_cn":" 第二部 ","date":"2024-01-01"}
            """,
            """
            {"id":21,"type":0,"subject_id":2,"sort":1.5,"airdate":"invalid"}
            {"id":99,"type":0,"subject_id":1,"sort":1,"airdate":"2020-01-01"}
            {"id":31,"type":1,"subject_id":3,"sort":1,"airdate":"2024-02-01"}
            {"id":20,"type":0,"subject_id":2,"sort":2,"airdate":"2024-01-08"}
            {"id":30,"type":0,"subject_id":3,"sort":1,"airdate":"2024-02-01"}
            """,
            """
            {"subject_id":3,"related_subject_id":2,"relation_type":2,"order":1}
            {"subject_id":2,"related_subject_id":3,"relation_type":3,"order":0}
            {"subject_id":1,"related_subject_id":2,"relation_type":2,"order":0}
            """);
    }

    private static async Task<string> WriteArchiveAsync(
        string root,
        string subjects,
        string episodes,
        string relations = "{\"subject_id\":1,\"related_subject_id\":1,\"relation_type\":2,\"order\":0}")
    {
        var path = Path.Combine(root, "dump-fixture.zip");
        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true);
        await WriteEntryAsync(archive, "subject.jsonlines", subjects);
        await WriteEntryAsync(archive, "episode.jsonlines", episodes);
        await WriteEntryAsync(archive, "subject-relations.jsonlines", relations);
        await WriteEntryAsync(archive, "person.jsonlines", "{\"id\":1}\n");
        return path;
    }

    private static async Task WriteEntryAsync(ZipArchive archive, string name, string value)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.SmallestSize);
        await using var stream = entry.Open();
        await stream.WriteAsync(Encoding.UTF8.GetBytes(value.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n"));
    }

    private static async Task<List<JsonElement>> ReadAssetsAsync(
        string root,
        IEnumerable<DataManifestAsset> assets)
    {
        var result = new List<JsonElement>();
        foreach (var asset in assets.OrderBy(asset => asset.FileName, StringComparer.Ordinal))
        {
            await using var file = File.OpenRead(Path.Combine(root, asset.FileName));
            await using var gzip = new GZipStream(file, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip, Encoding.UTF8);
            while (await reader.ReadLineAsync() is { } line)
            {
                using var document = JsonDocument.Parse(line);
                result.Add(document.RootElement.Clone());
            }
        }
        return result;
    }

    private static async Task<string> Sha256Async(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream));
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "animegonet-data-builder",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));
}
