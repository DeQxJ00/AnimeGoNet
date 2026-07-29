using AnimeGoNet.Core.Torrents;

namespace AnimeGoNet.Core.Tests.Torrents;

public sealed class TorrentUpstreamParityTests
{
    private const string UpstreamCommit =
        "c7475dfc55a374cd0dd08821bf17125dab1e3145";

    private static readonly IReadOnlyDictionary<string, ExpectedTorrent> Expected =
        new Dictionary<string, ExpectedTorrent>(StringComparer.Ordinal)
        {
            ["40b003ab90b1f7145abeec15e636b901e317a572"] = new(
                "[ANi] 吸血鬼馬上死 第二季 - 12 [1080P][Baha][WEB-DL][AAC AVC][CHT].mp4",
                453969717,
                [
                    new(
                        "[ANi] 吸血鬼馬上死 第二季 - 12 [1080P][Baha][WEB-DL][AAC AVC][CHT].mp4",
                        453969717),
                ]),
            ["b7f570888e8967744b399361429ede46d1c0e484"] = new(
                "[orion origin] Kyuuketsuki Sugu Shinu S2 [01-12] [WebRip] [1080p] [H265 AAC] [CHS]",
                5207380312,
                EpisodeFiles(
                    [
                        ("01", "", 414025392L),
                        ("02", "", 426084213L),
                        ("03", "", 438115611L),
                        ("04", "", 444575740L),
                        ("05", "", 432105628L),
                        ("06", "", 452563208L),
                        ("07", "", 429627160L),
                        ("08", "", 457227861L),
                        ("09", "", 442559292L),
                        ("10", "", 417893300L),
                        ("11", "", 424746135L),
                        ("12", " [END]", 421679448L),
                    ])),
            ["b3b30371841956fb94388b6075ca43d83f80c66c"] = new(
                "[ANi] 勇者死了！ - 02 [1080P][Baha][WEB-DL][AAC AVC][CHT].mp4",
                520012578,
                [
                    new(
                        "[ANi] 勇者死了！ - 02 [1080P][Baha][WEB-DL][AAC AVC][CHT].mp4",
                        520012578),
                ]),
            ["a1cc591ca5970be0ea032bfd3281f71645302dc2"] = new(
                "[UHA-WINGS] [NIJIYON ANIMATION] [13 - 15] [x264 1080p] [CHS]",
                161507192,
                [
                    new(
                        "[UHA-WINGS] [NIJIYON ANIMATION] [13] [x264 1080p] [CHS].mp4",
                        57099614),
                    new(
                        "[UHA-WINGS] [NIJIYON ANIMATION] [14] [x264 1080p] [CHS].mp4",
                        49395424),
                    new(
                        "[UHA-WINGS] [NIJIYON ANIMATION] [15] [x264 1080p] [CHS].mp4",
                        55012154),
                ]),
        };

    [Fact]
    public void PinnedUpstreamFixturesMatchInfoHashNameSizeAndEveryFile()
    {
        var upstream = Environment.GetEnvironmentVariable("ANIMEGO_UPSTREAM_REPO");
        if (string.IsNullOrWhiteSpace(upstream))
        {
            return;
        }

        var fixtureDirectory = Path.Combine(
            Path.GetFullPath(upstream),
            "internal",
            "pkg",
            "torrent",
            "testdata");
        Assert.True(
            Directory.Exists(fixtureDirectory),
            $"Pinned AnimeGo fixture directory for {UpstreamCommit} is missing.");

        foreach (var (hash, expected) in Expected)
        {
            var bytes = File.ReadAllBytes(
                Path.Combine(fixtureDirectory, $"{hash}.torrent"));
            var actual = TorrentMetainfoParser.Parse(bytes);

            Assert.Equal(hash, actual.InfoHash);
            Assert.Equal(expected.Name, actual.Name);
            Assert.Equal(expected.TotalSize, actual.TotalSize);
            var files = actual.Files.Where(file => !file.IsPadding).ToArray();
            Assert.Equal(expected.Files.Count, files.Length);
            for (var index = 0; index < files.Length; index++)
            {
                var expectedFile = expected.Files[index];
                Assert.Equal(
                    expected.Files.Count == 1
                        ? expectedFile.Name
                        : $"{expected.Name}/{expectedFile.Name}",
                    files[index].RelativePath);
                Assert.Equal(expectedFile.Size, files[index].Size);
            }

            Assert.DoesNotContain(
                "announce",
                actual.ToString(),
                StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task CiPinsPublicUpstreamWithoutCopyingTorrentAnnounces()
    {
        var workflow = await File.ReadAllTextAsync(
            Path.Combine(
                RepositoryRoot(),
                ".github",
                "workflows",
                "dotnet-ci.yml"));

        Assert.Contains("repository: wetor/AnimeGo", workflow, StringComparison.Ordinal);
        Assert.Contains($"ref: {UpstreamCommit}", workflow, StringComparison.Ordinal);
        Assert.Contains("ANIMEGO_UPSTREAM_REPO:", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain(".torrent.b64", workflow, StringComparison.Ordinal);
    }

    private static ExpectedFile[] EpisodeFiles(
        IReadOnlyList<(string Episode, string Extra, long Size)> episodes) =>
        episodes
            .Select(item => new ExpectedFile(
                $"[orion origin] Kyuuketsuki Sugu Shinu S2 [{item.Episode}]" +
                $"{item.Extra} [1080p] [H265 AAC] [CHS].mp4",
                item.Size))
            .ToArray();

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));

    private sealed record ExpectedTorrent(
        string Name,
        long TotalSize,
        IReadOnlyList<ExpectedFile> Files);

    private sealed record ExpectedFile(string Name, long Size);
}
