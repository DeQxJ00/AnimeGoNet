using System.Diagnostics;
using System.Text.RegularExpressions;
using AnimeGoNet.Core.Feeds;
using AnimeGoNet.Core.Metadata;
using AnimeGoNet.Core.Rules;

namespace AnimeGoNet.Core.Tests.Rules;

public sealed partial class UpstreamFilterFixtureParityTests
{
    private const string UpstreamCommit =
        "c7475dfc55a374cd0dd08821bf17125dab1e3145";

    [Fact]
    public async Task PinnedMikanFeedMatchesAllUpstreamFilterResultCounts()
    {
        var upstreamRoot = await TryGetPinnedUpstreamRootAsync();
        if (upstreamRoot is null)
        {
            return;
        }

        var feed = RssFeedParser.Parse(await File.ReadAllBytesAsync(Path.Combine(
            upstreamRoot,
            "test",
            "testdata",
            "filter",
            "Mikan.xml")));
        Assert.Equal(13, feed.Items.Count);

        var parsed = feed.Items
            .Select(item => AutoBangumiRawParser.Parse(item.Title))
            .ToArray();
        Assert.Equal(4, parsed.Count(item => item.Group == "NC-Raws"));
        Assert.Equal(
            9,
            parsed.Count(item =>
                item.Episode != 0
                && Resolution1080Regex().IsMatch(item.Resolution)));

        var emptyConfig = new LegacyMikanFilterConfig(
            [],
            EmptyRules(),
            EmptyRules(),
            EmptyRules(),
            EmptyRules());
        Assert.Equal(
            13,
            feed.Items.Count(item => LegacyMikanFilterEngine.Evaluate(
                new LegacyMikanFilterCandidate(
                    item.Title,
                    null,
                    null,
                    LegacyMikanFilterEngine.ParseGroupName(item.Title)),
                emptyConfig).Accepted));
    }

    [Fact]
    public async Task RegexFixtureKeepsOnlyTheSameInlineCandidate()
    {
        var upstreamRoot = await TryGetPinnedUpstreamRootAsync();
        if (upstreamRoot is null)
        {
            return;
        }

        var script = await File.ReadAllTextAsync(Path.Combine(
            upstreamRoot,
            "test",
            "testdata",
            "filter",
            "test_re.py"));
        Assert.Contains("re.search('1080'", script, StringComparison.Ordinal);

        var names = new[] { "0000", "1108011", "2222", "3333" };
        Assert.Equal(["1108011"], names.Where(name => name.Contains("1080", StringComparison.Ordinal)));
    }

    private static Dictionary<string, LegacyMikanFilterRule> EmptyRules() =>
        new(StringComparer.Ordinal);

    private static async Task<string?> TryGetPinnedUpstreamRootAsync()
    {
        var upstream = Environment.GetEnvironmentVariable("ANIMEGO_UPSTREAM_REPO");
        if (string.IsNullOrWhiteSpace(upstream))
        {
            return null;
        }

        var upstreamRoot = Path.GetFullPath(upstream);
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

    [GeneratedRegex("1080", RegexOptions.CultureInvariant)]
    private static partial Regex Resolution1080Regex();
}
