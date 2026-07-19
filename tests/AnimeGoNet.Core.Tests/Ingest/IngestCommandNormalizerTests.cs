using AnimeGoNet.Core.Ingest;

namespace AnimeGoNet.Core.Tests.Ingest;

public sealed class IngestCommandNormalizerTests
{
    [Fact]
    public void NormalizesMikanAliasesAndFingerprint()
    {
        var result = IngestCommandNormalizer.Normalize(
            " MIKAN ",
            Item(
                title: null,
                name: "Episode 01",
                mikanUrl: "https://mikanani.me/Home/Bangumi/3951",
                bgmid: 547888));

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        Assert.Equal("mikan", result.Item!.Source);
        Assert.Equal("Episode 01", result.Item.Title);
        Assert.Equal(3951, result.Item.MikanId);
        Assert.Equal("3951", result.Item.SourceWorkId);
        Assert.Equal(64, result.Item.TorrentUrlFingerprint.Length);
    }

    [Fact]
    public void ModernMikanRequiresBangumiIdButLegacyAdapterDoesNot()
    {
        var command = Item(title: "Episode 01", mikanUrl: "https://mikanani.me/Home/Bangumi/3951");

        Assert.Contains("positive bgmid", Assert.Single(IngestCommandNormalizer.Normalize("mikan", command).Errors));
        Assert.True(IngestCommandNormalizer.Normalize("mikan", command, requireModernMetadata: false).IsValid);
    }

    [Fact]
    public void RejectsConflictingTitleAliasesAndNonHttpTorrent()
    {
        var result = IngestCommandNormalizer.Normalize(
            "mikan",
            Item(title: "A", name: "B", torrent: "file:///secret.torrent", mikanId: 1, bgmid: 2));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("must match", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("HTTP(S)", StringComparison.Ordinal));
    }

    [Fact]
    public void TtgImdbIdIsCanonicalLowercase()
    {
        var result = IngestCommandNormalizer.Normalize("ttg", Item(title: "Show", imdbid: " TT1234567 "));

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        Assert.Equal("tt1234567", result.Item!.ImdbId);
    }

    [Fact]
    public void RejectsConflictingMikanWorkIdentifiers()
    {
        var result = IngestCommandNormalizer.Normalize(
            "mikan",
            Item(
                title: "Episode 01",
                mikanUrl: "https://mikanani.me/Home/Bangumi/3952",
                mikanId: 3951,
                bgmid: 547888));

        Assert.Contains(result.Errors, error => error.Contains("same work", StringComparison.Ordinal));
    }

    private static IngestItemCommand Item(
        string? title,
        string? name = null,
        string torrent = "https://tracker.invalid/passkey/file.torrent",
        string? mikanUrl = null,
        int? mikanId = null,
        int? bgmid = null,
        string? imdbid = null) =>
        new(
            torrent,
            new IngestItemInfo(title, name, null, null, mikanUrl, null, mikanId, bgmid, null, imdbid));
}
