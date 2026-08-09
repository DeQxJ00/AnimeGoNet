using System.Diagnostics;
using System.Text.Json;
using AnimeGoNet.Core.Feeds;

namespace AnimeGoNet.Core.Tests.Feeds;

public sealed class RssFeedUpstreamParityTests
{
    private const string UpstreamCommit =
        "c7475dfc55a374cd0dd08821bf17125dab1e3145";

    [Fact]
    public async Task MikanFixtureMatchesEveryUpstreamGoldenField()
    {
        var upstreamRoot = await TryGetPinnedUpstreamRootAsync();
        if (upstreamRoot is null)
        {
            return;
        }

        var fixtureRoot = Path.Combine(upstreamRoot, "test", "testdata", "feed");
        var feed = RssFeedParser.Parse(await File.ReadAllBytesAsync(Path.Combine(fixtureRoot, "Mikan.xml")));
        using var expectedDocument = JsonDocument.Parse(
            await File.ReadAllBytesAsync(Path.Combine(fixtureRoot, "Mikan.json")));
        var expectedItems = expectedDocument.RootElement.EnumerateArray().ToArray();

        Assert.Equal(expectedItems.Length, feed.Items.Count);
        for (var index = 0; index < expectedItems.Length; index++)
        {
            var expected = expectedItems[index];
            var actual = feed.Items[index];
            Assert.Equal(expected.GetProperty("name").GetString(), actual.Title);
            Assert.Equal(expected.GetProperty("mikan_url").GetString(), actual.MikanUrl);
            Assert.Equal(expected.GetProperty("torrent_url").GetString(), actual.TorrentUrl);
            Assert.Equal(expected.GetProperty("type").GetString(), actual.ContentType);
            Assert.Equal(expected.GetProperty("length").GetInt64(), actual.Length);
            Assert.StartsWith(
                expected.GetProperty("date").GetString()!,
                Assert.IsType<string>(actual.PublishedDate),
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task InvalidLengthFixtureSkipsMissingEnclosureAndDefaultsLengthToZero()
    {
        var upstreamRoot = await TryGetPinnedUpstreamRootAsync();
        if (upstreamRoot is null)
        {
            return;
        }

        var raw = await File.ReadAllBytesAsync(Path.Combine(
            upstreamRoot,
            "test",
            "testdata",
            "feed",
            "skip_and_err_length.xml"));
        var feed = RssFeedParser.Parse(raw);

        var item = Assert.Single(feed.Items);
        Assert.Equal("万事屋斋藤先生转生异世界", item.Title);
        Assert.Equal(
            "https://mikanani.me/Home/Episode/2076477d6a119fae9ad882ecc5fd697c1afaee75",
            item.MikanUrl);
        Assert.Equal(
            "https://mikanani.me/Download/20230123/2076477d6a119fae9ad882ecc5fd697c1afaee75.torrent",
            item.TorrentUrl);
        Assert.Equal("application/x-bittorrent", item.ContentType);
        Assert.Equal(0, item.Length);
        Assert.StartsWith("2023-01-23", Assert.IsType<string>(item.PublishedDate), StringComparison.Ordinal);
    }

    [Fact]
    public async Task MalformedFixtureUsesStableParseFailureCode()
    {
        var upstreamRoot = await TryGetPinnedUpstreamRootAsync();
        if (upstreamRoot is null)
        {
            return;
        }

        var raw = await File.ReadAllBytesAsync(Path.Combine(
            upstreamRoot,
            "test",
            "testdata",
            "feed",
            "err_parse_feed.xml"));

        var exception = Assert.Throws<RssFeedException>(() => RssFeedParser.Parse(raw));
        Assert.Equal("rss_parse_failed", exception.Code);
    }

    [Fact]
    public async Task BangumiFixtureReadsSourceIdentityAndAllEnclosures()
    {
        var upstreamRoot = await TryGetPinnedUpstreamRootAsync();
        if (upstreamRoot is null)
        {
            return;
        }

        var raw = await File.ReadAllBytesAsync(Path.Combine(
            upstreamRoot,
            "test",
            "testdata",
            "feed",
            "2822_370.xml"));
        var feed = RssFeedParser.Parse(
            raw,
            "https://mikanani.me/RSS/Bangumi?bangumiId=2822&subgroupid=370");

        Assert.Equal(2822, feed.MikanId);
        Assert.NotEmpty(feed.Items);
        Assert.All(feed.Items, item =>
        {
            Assert.NotEmpty(item.Title);
            Assert.StartsWith("https://mikanani.me/Home/Episode/", item.MikanUrl, StringComparison.Ordinal);
            Assert.StartsWith("https://mikanani.me/Download/", item.TorrentUrl, StringComparison.Ordinal);
            Assert.Equal("application/x-bittorrent", item.ContentType);
            Assert.True(item.Length >= 0);
        });
    }

    private static async Task<string?> TryGetPinnedUpstreamRootAsync()
    {
        var upstream = Environment.GetEnvironmentVariable("ANIMEGO_UPSTREAM_REPO");
        if (string.IsNullOrWhiteSpace(upstream))
        {
            return null;
        }

        var upstreamRoot = Path.GetFullPath(upstream);
        Assert.True(Directory.Exists(upstreamRoot), $"Upstream repository does not exist: {upstreamRoot}");
        Assert.Equal(UpstreamCommit, await ReadGitHeadAsync(upstreamRoot));
        return upstreamRoot;
    }

    private static async Task<string> ReadGitHeadAsync(string repository)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = repository,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };
        process.StartInfo.ArgumentList.Add("rev-parse");
        process.StartInfo.ArgumentList.Add("HEAD");
        Assert.True(process.Start());
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, error);
        return output.Trim();
    }
}
