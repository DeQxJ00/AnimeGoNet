using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AnimeGoNet.App.Feeds;
using AnimeGoNet.Core.Feeds;
using AnimeGoNet.Core.Rules;

namespace AnimeGoNet.LocalIntegration.Tests;

public sealed class MikanRssLiveAuditTests
{
    private static readonly JsonSerializerOptions ReportJsonOptions = new() { WriteIndented = true };

    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task PrivateFeedCanBeFetchedParsedAndFilteredWithoutStagingDownloads()
    {
        Assert.Equal("1", Required("ANIMEGONET_MIKAN_RSS_INTEGRATION"));
        var rssUrl = Required("ANIMEGONET_MIKAN_RSS_URL");
        var outputRoot = Required("ANIMEGONET_MIKAN_RSS_AUDIT_OUTPUT");

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        var reader = new RssFeedReader(new RssFeedHttpClient(httpClient));
        var feed = await reader.ParseUrlAsync(rssUrl);
        Assert.NotEmpty(feed.Items);

        var plan = MikanRssBatchPlanner.Create(feed, MikanRssRuleDefaults.Create());
        Assert.Equal(feed.Items.Count, plan.Items.Count);
        Assert.All(plan.Items, item => Assert.DoesNotContain("http", item.Decision.Reason, StringComparison.OrdinalIgnoreCase));

        Directory.CreateDirectory(outputRoot);
        var reportPath = Path.Combine(
            outputRoot,
            $"mikan-rss-audit-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.json");
        var report = new
        {
            schema_version = 1,
            executed_at_utc = DateTimeOffset.UtcNow,
            source_host = new Uri(rssUrl).IdnHost.ToLowerInvariant(),
            source_fingerprint = Fingerprint(rssUrl),
            feed_mikanid = feed.MikanId,
            item_count = feed.Items.Count,
            winner_count = plan.Winners.Count,
            effects = "none",
            ai_used = false,
            items = plan.Items.Select((item, index) => new
            {
                index = index + 1,
                title = item.FeedItem.Title,
                published_date = item.FeedItem.PublishedDate,
                content_type = item.FeedItem.ContentType,
                declared_length = item.FeedItem.Length,
                item_fingerprint = Fingerprint(string.Concat(item.FeedItem.MikanUrl, "\n", item.FeedItem.TorrentUrl)),
                parsed_episode_kind = item.Candidate.SourceEpisodeKind,
                parsed_episode = item.Candidate.SourceEpisode,
                decision = item.Decision.Kind.ToString(),
                reason = item.Decision.Reason,
                evaluated_priority_groups = item.Decision.EvaluatedPriorityGroups,
            }),
        };
        await File.WriteAllTextAsync(
            reportPath,
            JsonSerializer.Serialize(report, ReportJsonOptions),
            Encoding.UTF8);

        var serialized = await File.ReadAllTextAsync(reportPath);
        Assert.DoesNotContain(rssUrl, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("token=", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("torrent_url", serialized, StringComparison.OrdinalIgnoreCase);
    }

    private static string Fingerprint(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string Required(string name) =>
        Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"Set {name} before running this explicit local integration test.");
}
