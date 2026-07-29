using System.Text;
using AnimeGoNet.Core.DataUpdate;
using AnimeGoNet.Data.DataUpdate;

namespace AnimeGoNet.Data.Tests.DataUpdate;

public sealed class DataPackageStoreTests
{
    [Fact]
    public async Task ImportsValidatedPackageAndActivatesItAtomically()
    {
        await using var fixture = await DataPackageTestFixture.CreateAsync();
        var request = await fixture.CreateRequestAsync("2026.07.29.1");

        var result = await fixture.Store.ImportAsync(request);
        var status = await fixture.Store.GetStatusAsync();

        Assert.False(result.AlreadyActive);
        Assert.Equal(2, result.SubjectCount);
        Assert.Equal(3, result.EpisodeCount);
        Assert.Equal("2026.07.29.1", status.ActiveVersion);
        Assert.Null(status.PreviousVersion);
        var version = Assert.Single(status.Versions);
        Assert.Equal("active", version.State);
        Assert.Equal("completed", status.LastRun!.Status);

        await using var connection = await OpenAsync(fixture);
        await using var query = connection.CreateCommand();
        query.CommandText = """
            SELECT
                (SELECT episode_number
                 FROM bangumi_archive_episodes
                 WHERE data_version = '2026.07.29.1' AND episode_id = 1424),
                (SELECT COUNT(*) FROM data_update_staging_subjects),
                (SELECT COUNT(*) FROM data_update_staging_episodes);
            """;
        await using var reader = await query.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("48.5", reader.GetString(0));
        Assert.Equal(0, reader.GetInt32(1));
        Assert.Equal(0, reader.GetInt32(2));
    }

    [Fact]
    public async Task ActivatesSecondVersionAndRollbackSwapsThePointers()
    {
        await using var fixture = await DataPackageTestFixture.CreateAsync();
        await fixture.Store.ImportAsync(await fixture.CreateRequestAsync("2026.07.29.1"));
        await fixture.Store.ImportAsync(await fixture.CreateRequestAsync(
            "2026.07.30.1",
            utcNow: new DateTimeOffset(2026, 7, 30, 13, 0, 0, TimeSpan.Zero)));

        var before = await fixture.Store.GetStatusAsync();
        var rollback = await fixture.Store.RollbackAsync(
            new DateTimeOffset(2026, 7, 30, 14, 0, 0, TimeSpan.Zero));
        var after = await fixture.Store.GetStatusAsync();

        Assert.Equal("2026.07.30.1", before.ActiveVersion);
        Assert.Equal("2026.07.29.1", before.PreviousVersion);
        Assert.Equal("2026.07.29.1", rollback.ActiveVersion);
        Assert.Equal("2026.07.30.1", rollback.PreviousVersion);
        Assert.Equal(rollback.ActiveVersion, after.ActiveVersion);
        Assert.Equal(rollback.PreviousVersion, after.PreviousVersion);
        Assert.Equal("rollback", after.LastRun!.Operation);
        Assert.Equal("completed", after.LastRun.Status);
    }

    [Fact]
    public async Task RetentionAlwaysPreservesActiveAndPreviousVersions()
    {
        await using var fixture = await DataPackageTestFixture.CreateAsync();
        await fixture.Store.ImportAsync(await fixture.CreateRequestAsync("2026.07.29.1"));
        await fixture.Store.ImportAsync(await fixture.CreateRequestAsync(
            "2026.07.30.1",
            utcNow: new DateTimeOffset(2026, 7, 30, 13, 0, 0, TimeSpan.Zero)));
        var third = await fixture.Store.ImportAsync(await fixture.CreateRequestAsync(
            "2026.07.31.1",
            utcNow: new DateTimeOffset(2026, 7, 31, 13, 0, 0, TimeSpan.Zero)));

        var status = await fixture.Store.GetStatusAsync();

        Assert.Equal(["2026.07.29.1"], third.PrunedVersions);
        Assert.Equal("2026.07.31.1", status.ActiveVersion);
        Assert.Equal("2026.07.30.1", status.PreviousVersion);
        Assert.Equal(
            ["2026.07.31.1", "2026.07.30.1"],
            status.Versions.Select(version => version.DataVersion).ToArray());
    }

    [Fact]
    public async Task ReimportingActiveVersionIsIdempotent()
    {
        await using var fixture = await DataPackageTestFixture.CreateAsync();
        var request = await fixture.CreateRequestAsync("2026.07.29.1");
        var first = await fixture.Store.ImportAsync(request);

        var second = await fixture.Store.ImportAsync(request);
        var status = await fixture.Store.GetStatusAsync();

        Assert.False(first.AlreadyActive);
        Assert.True(second.AlreadyActive);
        Assert.Empty(second.RunId);
        Assert.Single(status.Versions);
        Assert.Equal(first.RunId, status.LastRun!.RunId);
    }

    [Fact]
    public async Task SameVersionWithDifferentManifestIsRejected()
    {
        await using var fixture = await DataPackageTestFixture.CreateAsync();
        var request = await fixture.CreateRequestAsync("2026.07.29.1");
        await fixture.Store.ImportAsync(request);

        var exception = await Assert.ThrowsAsync<DataPackageException>(() =>
            fixture.Store.ImportAsync(request with { ManifestSha256 = new string('f', 64) }));

        Assert.Equal("data_version_immutable_conflict", exception.Code);
        Assert.Equal("2026.07.29.1", (await fixture.Store.GetStatusAsync()).ActiveVersion);
    }

    [Fact]
    public async Task MissingSubjectReferenceFailsWithoutChangingActiveVersion()
    {
        await using var fixture = await DataPackageTestFixture.CreateAsync();
        await fixture.Store.ImportAsync(await fixture.CreateRequestAsync("2026.07.29.1"));
        var bad = await fixture.CreateRequestAsync(
            "2026.07.30.1",
            episodes: """
                {"id":1423,"subject_id":99,"sort":1,"episode":"1","air_date":null}

                """,
            utcNow: new DateTimeOffset(2026, 7, 30, 13, 0, 0, TimeSpan.Zero));

        var exception = await Assert.ThrowsAsync<DataPackageException>(() =>
            fixture.Store.ImportAsync(bad));
        var status = await fixture.Store.GetStatusAsync();

        Assert.Equal("data_package_subject_reference_missing", exception.Code);
        Assert.Equal("2026.07.29.1", status.ActiveVersion);
        Assert.Single(status.Versions);
        Assert.Equal("failed", status.LastRun!.Status);
        Assert.Equal(exception.Code, status.LastRun.FailureCode);
        await AssertStagingEmptyAsync(fixture);
    }

    [Fact]
    public async Task ChecksumMismatchFailsBeforeDecompression()
    {
        await using var fixture = await DataPackageTestFixture.CreateAsync();
        var request = await fixture.CreateRequestAsync("2026.07.29.1");
        var asset = request.Manifest.Assets[0];
        var modified = asset with { Sha256 = new string('f', 64) };
        request = request with
        {
            Manifest = request.Manifest with
            {
                Assets = [modified, .. request.Manifest.Assets.Skip(1)],
            },
        };

        var exception = await Assert.ThrowsAsync<DataPackageException>(() =>
            fixture.Store.ImportAsync(request));

        Assert.Equal("data_asset_sha256_mismatch", exception.Code);
        Assert.Null((await fixture.Store.GetStatusAsync()).ActiveVersion);
        await AssertStagingEmptyAsync(fixture);
    }

    [Fact]
    public async Task InvalidGzipWithMatchingChecksumIsRejected()
    {
        await using var fixture = await DataPackageTestFixture.CreateAsync();
        var request = await fixture.CreateRawAssetRequestAsync(
            "2026.07.29.1",
            DataAssetKind.Subjects,
            Encoding.UTF8.GetBytes("not gzip"));

        var exception = await Assert.ThrowsAsync<DataPackageException>(() =>
            fixture.Store.ImportAsync(request));

        Assert.Equal("data_asset_gzip_invalid", exception.Code);
    }

    [Theory]
    [InlineData(
        """
        {"id":52,"name":"AIR","name_cn":null,"air_date":null,"episode_count":1}
        {"id":51,"name":"CLANNAD","name_cn":null,"air_date":null,"episode_count":1}

        """,
        "data_asset_order_invalid")]
    [InlineData(
        """
        {"id":51,"name":"CLANNAD","name_cn":null,"air_date":null,"episode_count":1}
        not-json

        """,
        "data_asset_json_invalid")]
    [InlineData(
        """
        {"id":51,"name":"CLANNAD","name_cn":null,"air_date":"29-07-2026","episode_count":1}

        """,
        "data_asset_record_date_invalid")]
    [InlineData(
        """
        {"id":51,"name":"CLANNAD","air_date":null,"episode_count":1}

        """,
        "data_asset_record_value_invalid")]
    public async Task MalformedSubjectAssetsAreRejected(string subjects, string expectedCode)
    {
        await using var fixture = await DataPackageTestFixture.CreateAsync();
        var request = await fixture.CreateRequestAsync("2026.07.29.1", subjects: subjects);

        var exception = await Assert.ThrowsAsync<DataPackageException>(() =>
            fixture.Store.ImportAsync(request));

        Assert.Equal(expectedCode, exception.Code);
        Assert.Null((await fixture.Store.GetStatusAsync()).ActiveVersion);
    }

    [Theory]
    [InlineData(
        "{\"id\":51,\"name\":\"CLANNAD\",\"name_cn\":null,\"air_date\":null,\"episode_count\":1}\r\n",
        "data_asset_line_ending_invalid")]
    [InlineData(
        "{\"id\":51,\"name\":\"CLANNAD\",\"name_cn\":null,\"air_date\":null,\"episode_count\":1}",
        "data_asset_line_ending_invalid")]
    public async Task NonCanonicalLineEndingsAreRejected(string subjects, string expectedCode)
    {
        await using var fixture = await DataPackageTestFixture.CreateAsync();
        var request = await fixture.CreateRequestAsync("2026.07.29.1", subjects: subjects);

        var exception = await Assert.ThrowsAsync<DataPackageException>(() =>
            fixture.Store.ImportAsync(request));

        Assert.Equal(expectedCode, exception.Code);
    }

    [Fact]
    public async Task OversizedJsonLineIsRejectedBeforeJsonAllocation()
    {
        await using var fixture = await DataPackageTestFixture.CreateAsync();
        var subjects = string.Concat(
            "{\"id\":51,\"name\":\"",
            new string('a', DataPackageStore.MaximumJsonLineBytes),
            "\",\"name_cn\":null,\"air_date\":null,\"episode_count\":1}\n");
        var request = await fixture.CreateRequestAsync("2026.07.29.1", subjects: subjects);

        var exception = await Assert.ThrowsAsync<DataPackageException>(() =>
            fixture.Store.ImportAsync(request));

        Assert.Equal("data_asset_line_too_long", exception.Code);
    }

    [Fact]
    public async Task AssetDirectoryMayHaveATrailingSeparator()
    {
        await using var fixture = await DataPackageTestFixture.CreateAsync();
        var request = await fixture.CreateRequestAsync("2026.07.29.1");
        request = request with
        {
            AssetDirectory = request.AssetDirectory + Path.DirectorySeparatorChar,
        };

        var result = await fixture.Store.ImportAsync(request);

        Assert.Equal("2026.07.29.1", result.DataVersion);
    }

    [Fact]
    public async Task DuplicateIdsAcrossShardsAreRejected()
    {
        await using var fixture = await DataPackageTestFixture.CreateAsync();
        var request = await fixture.CreateRequestAsync(
            "2026.07.29.1",
            additionalAssets:
            [
                new AdditionalAsset(
                    DataAssetKind.Subjects,
                    "subjects-second.jsonl.gz",
                    """
                    {"id":51,"name":"Duplicate","name_cn":null,"air_date":null,"episode_count":1}

                    """,
                    1,
                    100),
            ]);

        var exception = await Assert.ThrowsAsync<DataPackageException>(() =>
            fixture.Store.ImportAsync(request));

        Assert.Equal("data_asset_duplicate_id", exception.Code);
        await AssertStagingEmptyAsync(fixture);
    }

    [Fact]
    public async Task DeclaredRecordCountMismatchIsRejected()
    {
        await using var fixture = await DataPackageTestFixture.CreateAsync();
        var request = await fixture.CreateRequestAsync("2026.07.29.1");
        var asset = request.Manifest.Assets[0];
        request = request with
        {
            Manifest = request.Manifest with
            {
                Assets =
                [
                    asset with { RecordCount = asset.RecordCount + 1 },
                    .. request.Manifest.Assets.Skip(1),
                ],
                SubjectCount = request.Manifest.SubjectCount + 1,
            },
        };

        var exception = await Assert.ThrowsAsync<DataPackageException>(() =>
            fixture.Store.ImportAsync(request));

        Assert.Equal("data_asset_record_count_mismatch", exception.Code);
    }

    [Fact]
    public async Task ClientVersionGateRunsBeforeCreatingAnImportRun()
    {
        await using var fixture = await DataPackageTestFixture.CreateAsync();
        var request = await fixture.CreateRequestAsync(
            "2026.07.29.1",
            clientVersion: new Version(1, 0, 0),
            minimumClientVersion: "2.0.0");

        var exception = await Assert.ThrowsAsync<DataPackageException>(() =>
            fixture.Store.ImportAsync(request));
        var status = await fixture.Store.GetStatusAsync();

        Assert.Equal("data_client_version_too_old", exception.Code);
        Assert.Null(status.LastRun);
    }

    [Fact]
    public async Task RollbackWithoutPreviousVersionIsAuditedAsFailure()
    {
        await using var fixture = await DataPackageTestFixture.CreateAsync();
        await fixture.Store.ImportAsync(await fixture.CreateRequestAsync("2026.07.29.1"));

        var exception = await Assert.ThrowsAsync<DataPackageException>(() =>
            fixture.Store.RollbackAsync(
                new DateTimeOffset(2026, 7, 29, 14, 0, 0, TimeSpan.Zero)));
        var status = await fixture.Store.GetStatusAsync();

        Assert.Equal("data_rollback_version_unavailable", exception.Code);
        Assert.Equal("2026.07.29.1", status.ActiveVersion);
        Assert.Equal("failed", status.LastRun!.Status);
        Assert.Equal("rollback", status.LastRun.Operation);
    }

    private static async Task<Microsoft.Data.Sqlite.SqliteConnection> OpenAsync(
        DataPackageTestFixture fixture) =>
        await fixture.Database.OpenConnectionAsync();

    private static async Task AssertStagingEmptyAsync(DataPackageTestFixture fixture)
    {
        await using var connection = await OpenAsync(fixture);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM data_update_staging_subjects),
                (SELECT COUNT(*) FROM data_update_staging_episodes);
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(0, reader.GetInt32(0));
        Assert.Equal(0, reader.GetInt32(1));
    }
}
