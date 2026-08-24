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

    [Theory]
    [InlineData("https://mikanime.tv/Home/Episode/abc", "https://mikanime.tv/Home/Episode/abc")]
    [InlineData("https://mikanime.tv/Home/Episode/abc?token=secret", null)]
    [InlineData("https://mikanime.tv/Home/Bangumi/3951", null)]
    public void PersistsOnlyCredentialFreeMikanEpisodeSourcePage(
        string sourceUrl,
        string? expected)
    {
        var result = IngestCommandNormalizer.Normalize(
            "mikan",
            Item(title: "Episode 01", mikanUrl: sourceUrl, mikanId: 3951, bgmid: 547888));

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        Assert.Equal(expected, result.Item!.SourcePageUrl);
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
    public void RegisteredSourceImdbIdIsCanonicalLowercase()
    {
        var result = IngestCommandNormalizer.Normalize("u2", Item(title: "Show", imdbid: " TT1234567 "));

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

    [Fact]
    public void InternalMikanPublicationEvidenceIsPreserved()
    {
        var published = DateTimeOffset.Parse(
            "2026-07-22T12:34:56+08:00",
            System.Globalization.CultureInfo.InvariantCulture);
        var command = Item(
            title: "Episode 01",
            mikanId: 3951,
            bgmid: 547888) with
        {
            SourceEvidence = new IngestSourceEvidence(
                "2026-07-22T12:34:56",
                published),
        };

        var result = IngestCommandNormalizer.Normalize("mikan", command);

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        Assert.Equal("2026-07-22T12:34:56", result.Item!.PublishedAtRaw);
        Assert.Equal(published, result.Item.PublishedAt);
    }

    [Fact]
    public void NonMikanSourceCannotAttachMikanPublicationEvidence()
    {
        var command = Item(title: "Show", imdbid: "tt1234567") with
        {
            SourceEvidence = new IngestSourceEvidence(
                "2026-07-22T12:34:56Z",
                DateTimeOffset.Parse(
                    "2026-07-22T12:34:56Z",
                    System.Globalization.CultureInfo.InvariantCulture)),
        };

        var result = IngestCommandNormalizer.Normalize("u2", command);

        Assert.Contains(result.Errors, error =>
            error.Contains("publication evidence", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null, "tv")]
    [InlineData(" TV ", "tv")]
    [InlineData("MOVIE", "movie")]
    public void NormalizesSupportedMediaType(string? value, string expected)
    {
        var result = IngestCommandNormalizer.Normalize(
            "mikan",
            Item(title: "Feature", mikanId: 3951, bgmid: 547888, mediaType: value));

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        Assert.Equal(expected, result.Item!.MediaType);
    }

    [Fact]
    public void RejectsUnsupportedMediaType()
    {
        var result = IngestCommandNormalizer.Normalize(
            "mikan",
            Item(title: "Feature", mikanId: 3951, bgmid: 547888, mediaType: "ova"));

        Assert.Contains("info.media_type must be 'tv' or 'movie'", result.Errors);
    }

    private static IngestItemCommand Item(
        string? title,
        string? name = null,
        string torrent = "https://tracker.invalid/passkey/file.torrent",
        string? mikanUrl = null,
        int? mikanId = null,
        int? bgmid = null,
        string? imdbid = null,
        string? mediaType = null) =>
        new(
            torrent,
            new IngestItemInfo(
                title, name, null, null, mikanUrl, null, mikanId, bgmid, null, imdbid,
                MediaType: mediaType));
}
