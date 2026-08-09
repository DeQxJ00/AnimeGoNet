using System.Text;
using AnimeGoNet.Core.DataUpdate;

namespace AnimeGoNet.Core.Tests.DataUpdate;

public sealed class DataManifestParserTests
{
    [Fact]
    public void ParsesVersionOneManifestWithDeterministicAssets()
    {
        var manifest = DataManifestParser.Parse(Encoding.UTF8.GetBytes(ValidManifest));

        Assert.Equal(1, manifest.SchemaVersion);
        Assert.Equal("2026.07.29.1", manifest.DataVersion);
        Assert.Equal(new Version(0, 1, 0), Version.Parse(manifest.MinimumClientVersion));
        Assert.Equal(2, manifest.Assets.Count);
        Assert.Equal(100, manifest.SubjectCount);
        Assert.Equal(1200, manifest.EpisodeCount);
        Assert.Equal(0, manifest.RelationCount);
        Assert.Equal(DataAssetKind.Subjects, manifest.Assets[0].Kind);
        Assert.Equal("bangumi-subjects-v1-000001-000100.jsonl.gz", manifest.Assets[0].FileName);
        Assert.Equal(1, manifest.Assets[0].SubjectIdMin);
        Assert.Equal(100, manifest.Assets[0].SubjectIdMax);
    }

    [Theory]
    [InlineData("\"schema_version\":1", "\"schema_version\":3", "data_manifest_schema_unsupported")]
    [InlineData("\"data_version\":\"2026.07.29.1\"", "\"data_version\":\"../latest\"", "data_manifest_version_invalid")]
    [InlineData("\"sha256\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"", "\"sha256\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\"", "data_manifest_sha256_invalid")]
    [InlineData("\"file_name\":\"bangumi-subjects-v1-000001-000100.jsonl.gz\"", "\"file_name\":\"../subjects.jsonl.gz\"", "data_manifest_asset_name_invalid")]
    [InlineData("\"totals\":{\"subjects\":100,\"episodes\":1200}", "\"totals\":{\"subjects\":99,\"episodes\":1200}", "data_manifest_totals_mismatch")]
    public void RejectsInvalidManifestWithStableCode(
        string original,
        string replacement,
        string expectedCode)
    {
        var exception = Assert.Throws<DataManifestException>(() =>
            DataManifestParser.Parse(Encoding.UTF8.GetBytes(
                ValidManifest.Replace(original, replacement, StringComparison.Ordinal))));

        Assert.Equal(expectedCode, exception.Code);
    }

    [Fact]
    public void RejectsDuplicateAssetNames()
    {
        var json = ValidManifest.Replace(
            "bangumi-episodes-v1-000001-000100.jsonl.gz",
            "bangumi-subjects-v1-000001-000100.jsonl.gz",
            StringComparison.Ordinal);

        Assert.Equal(
            "data_manifest_asset_name_invalid",
            Assert.Throws<DataManifestException>(() =>
                DataManifestParser.Parse(Encoding.UTF8.GetBytes(json))).Code);
    }

    [Fact]
    public void RejectsOversizedManifestBeforeJsonParsing()
    {
        var bytes = new byte[DataManifestParser.MaximumManifestBytes + 1];

        Assert.Equal(
            "data_manifest_size_invalid",
            Assert.Throws<DataManifestException>(() => DataManifestParser.Parse(bytes)).Code);
    }

    [Fact]
    public void ParsesVersionTwoManifestWithRequiredRelations()
    {
        var json = ValidManifest
            .Replace("\"schema_version\":1", "\"schema_version\":2", StringComparison.Ordinal)
            .Replace(
                "\n  ],\n  \"totals\"",
                """
                ,
                    {
                      "kind":"relations",
                      "file_name":"bangumi-relations-v2-000001-000100.jsonl.gz",
                      "url":"https://github.com/example/AnimeGoNetData/releases/download/2026.07.29.1/bangumi-relations-v2-000001-000100.jsonl.gz",
                      "size_bytes":512,
                      "sha256":"dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd",
                      "record_count":12,
                      "subject_id_min":1,
                      "subject_id_max":100
                    }
                  ],
                  "totals"
                """,
                StringComparison.Ordinal)
            .Replace(
                "\"subjects\":100,\"episodes\":1200}",
                "\"subjects\":100,\"episodes\":1200,\"relations\":12}",
                StringComparison.Ordinal);

        var manifest = DataManifestParser.Parse(Encoding.UTF8.GetBytes(json));

        Assert.Equal(2, manifest.SchemaVersion);
        Assert.Equal(12, manifest.RelationCount);
        Assert.Equal(DataAssetKind.Relations, manifest.Assets[^1].Kind);
    }

    [Fact]
    public void VersionTwoWithoutRelationsIsRejected()
    {
        var json = ValidManifest.Replace(
            "\"schema_version\":1",
            "\"schema_version\":2",
            StringComparison.Ordinal);

        Assert.Equal(
            "data_manifest_relation_asset_missing",
            Assert.Throws<DataManifestException>(() =>
                DataManifestParser.Parse(Encoding.UTF8.GetBytes(json))).Code);
    }

    private const string ValidManifest = """
        {
          "schema_version":1,
          "data_version":"2026.07.29.1",
          "generated_at_utc":"2026-07-29T12:00:00.0000000+00:00",
          "minimum_client_version":"0.1.0",
          "upstream":{
            "repository":"https://github.com/bangumi/Archive",
            "release":"archive-2026-07-29",
            "asset":"bangumi-json-20260729.zip",
            "sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
          },
          "assets":[
            {
              "kind":"subjects",
              "file_name":"bangumi-subjects-v1-000001-000100.jsonl.gz",
              "url":"https://github.com/example/AnimeGoNetData/releases/download/2026.07.29.1/bangumi-subjects-v1-000001-000100.jsonl.gz",
              "size_bytes":1024,
              "sha256":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
              "record_count":100,
              "subject_id_min":1,
              "subject_id_max":100
            },
            {
              "kind":"episodes",
              "file_name":"bangumi-episodes-v1-000001-000100.jsonl.gz",
              "url":"https://github.com/example/AnimeGoNetData/releases/download/2026.07.29.1/bangumi-episodes-v1-000001-000100.jsonl.gz",
              "size_bytes":4096,
              "sha256":"cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc",
              "record_count":1200,
              "subject_id_min":1,
              "subject_id_max":100
            }
          ],
          "totals":{"subjects":100,"episodes":1200}
        }
        """;
}
