using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using AnimeGoNet.Core.DataUpdate;
using AnimeGoNet.Data.DataUpdate;

namespace AnimeGoNet.Data.Tests.DataUpdate;

internal sealed class DataPackageTestFixture : IAsyncDisposable
{
    private readonly SqliteDatabaseFixture _databaseFixture;

    private DataPackageTestFixture(SqliteDatabaseFixture databaseFixture)
    {
        _databaseFixture = databaseFixture;
        Store = new DataPackageStore(databaseFixture.Database);
    }

    public string RootPath => _databaseFixture.RootPath;

    public AnimeGoNet.Data.Sqlite.AnimeGoSqliteDatabase Database => _databaseFixture.Database;

    public DataPackageStore Store { get; }

    public static async Task<DataPackageTestFixture> CreateAsync() =>
        new(await SqliteDatabaseFixture.CreateAsync());

    public async Task<DataPackageImportRequest> CreateRequestAsync(
        string version,
        string subjects = """
            {"id":51,"name":"CLANNAD","name_cn":"CLANNAD","air_date":"2007-10-05","episode_count":2}
            {"id":52,"name":"AIR","name_cn":null,"air_date":null,"episode_count":1}

            """,
        string episodes = """
            {"id":1423,"subject_id":51,"sort":1,"episode":"1","air_date":"2007-10-05"}
            {"id":1424,"subject_id":51,"sort":2,"episode":"48.5","air_date":null}
            {"id":1425,"subject_id":52,"sort":1,"episode":"1","air_date":null}

            """,
        DateTimeOffset? utcNow = null,
        int keepVersions = 2,
        Version? clientVersion = null,
        string minimumClientVersion = "0.1.0",
        IReadOnlyList<AdditionalAsset>? additionalAssets = null,
        int schemaVersion = 1,
        string? relations = null)
    {
        var packagePath = Path.Combine(RootPath, version);
        Directory.CreateDirectory(packagePath);
        var assets = new List<DataManifestAsset>
        {
            await WriteAssetAsync(
                packagePath,
                DataAssetKind.Subjects,
                $"subjects-{version}.jsonl.gz",
                subjects,
                1,
                100),
            await WriteAssetAsync(
                packagePath,
                DataAssetKind.Episodes,
                $"episodes-{version}.jsonl.gz",
                episodes,
                1,
                100),
        };
        if (additionalAssets is not null)
        {
            foreach (var additional in additionalAssets)
            {
                assets.Add(await WriteAssetAsync(
                    packagePath,
                    additional.Kind,
                    additional.FileName,
                    additional.JsonLines,
                    additional.SubjectIdMin,
                    additional.SubjectIdMax));
            }
        }

        var subjectCount = assets
            .Where(asset => asset.Kind == DataAssetKind.Subjects)
            .Sum(asset => asset.RecordCount);
        var episodeCount = assets
            .Where(asset => asset.Kind == DataAssetKind.Episodes)
            .Sum(asset => asset.RecordCount);
        if (schemaVersion >= 2)
        {
            relations ??= """
                {"subject_id":52,"related_subject_id":51,"relation_type":2,"order":0}

                """;
            assets.Add(await WriteAssetAsync(
                packagePath,
                DataAssetKind.Relations,
                $"relations-{version}.jsonl.gz",
                relations,
                1,
                100));
        }
        var relationCount = assets
            .Where(asset => asset.Kind == DataAssetKind.Relations)
            .Sum(asset => asset.RecordCount);
        var manifest = new DataManifest(
            schemaVersion,
            version,
            new DateTimeOffset(2026, 7, 29, 12, 0, 0, TimeSpan.Zero),
            minimumClientVersion,
            new DataManifestUpstream(
                "https://github.com/bangumi/Archive",
                "archive-2026-07-29",
                "bangumi-json.zip",
                new string('a', 64)),
            assets,
            subjectCount,
            episodeCount)
        {
            RelationCount = relationCount,
        };
        return new DataPackageImportRequest(
            manifest,
            Sha256($"manifest:{version}"),
            packagePath,
            clientVersion ?? new Version(1, 0, 0),
            keepVersions,
            utcNow ?? new DateTimeOffset(2026, 7, 29, 13, 0, 0, TimeSpan.Zero));
    }

    public async Task<DataPackageImportRequest> CreateRawAssetRequestAsync(
        string version,
        DataAssetKind rawKind,
        byte[] rawBytes)
    {
        var request = await CreateRequestAsync(version);
        var target = request.Manifest.Assets.Single(asset => asset.Kind == rawKind);
        var path = Path.Combine(request.AssetDirectory, target.FileName);
        await File.WriteAllBytesAsync(path, rawBytes);
        var replacement = target with
        {
            SizeBytes = rawBytes.LongLength,
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(rawBytes)),
        };
        return request with
        {
            Manifest = request.Manifest with
            {
                Assets = request.Manifest.Assets
                    .Select(asset => asset == target ? replacement : asset)
                    .ToArray(),
            },
        };
    }

    public async ValueTask DisposeAsync()
    {
        Store.Dispose();
        await _databaseFixture.DisposeAsync();
    }

    private static async Task<DataManifestAsset> WriteAssetAsync(
        string packagePath,
        DataAssetKind kind,
        string fileName,
        string jsonLines,
        int subjectIdMin,
        int subjectIdMax)
    {
        var path = Path.Combine(packagePath, fileName);
        await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
        await using (var gzip = new GZipStream(file, CompressionLevel.SmallestSize))
        {
            var bytes = Encoding.UTF8.GetBytes(jsonLines);
            await gzip.WriteAsync(bytes);
        }

        var bytesOnDisk = await File.ReadAllBytesAsync(path);
        return new DataManifestAsset(
            kind,
            fileName,
            new Uri($"https://example.test/{fileName}"),
            bytesOnDisk.LongLength,
            Convert.ToHexStringLower(SHA256.HashData(bytesOnDisk)),
            jsonLines.Count(character => character == '\n'),
            subjectIdMin,
            subjectIdMax);
    }

    private static string Sha256(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

internal sealed record AdditionalAsset(
    DataAssetKind Kind,
    string FileName,
    string JsonLines,
    int SubjectIdMin,
    int SubjectIdMax);
