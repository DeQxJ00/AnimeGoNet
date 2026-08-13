using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AnimeGoNet.App.Feeds;
using AnimeGoNet.App.Metadata;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Feeds;
using AnimeGoNet.Core.Metadata;
using AnimeGoNet.Core.Rules;

namespace AnimeGoNet.LocalIntegration.Tests;

public sealed class MikanRssLiveAuditTests
{
    private static readonly JsonSerializerOptions ReportJsonOptions = new() { WriteIndented = true };

    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task MyBangumiAggregateResolvesPerItemWorkIdentityWithoutDownloading()
    {
        Assert.Equal("1", Required("ANIMEGONET_MIKAN_RSS_INTEGRATION"));
        var rssUrl = Required("ANIMEGONET_MIKAN_RSS_URL");

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        var rssHttp = new RssFeedHttpClient(httpClient);
        var feed = await new RssFeedReader(rssHttp).ParseUrlAsync(rssUrl);
        Assert.NotEmpty(feed.Items);
        Assert.Null(feed.MikanId);

        var identities = await new MikanFeedIdentityResolver(rssHttp)
            .ResolveAsync(feed, "mikan");

        Assert.Equal(feed.Items.Count, identities.Count);
        Assert.All(identities, item =>
        {
            Assert.NotNull(item.Identity);
            Assert.Null(item.FailureCode);
        });
        Assert.True(identities.Select(item => item.Identity!.MikanId).Distinct().Count() > 1);
    }

    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task PrivateFeedCanBeFetchedParsedAndFilteredWithoutStagingDownloads()
    {
        Assert.Equal("1", Required("ANIMEGONET_MIKAN_RSS_INTEGRATION"));
        var rssUrl = Required("ANIMEGONET_MIKAN_RSS_URL");
        var outputRoot = Required("ANIMEGONET_MIKAN_RSS_AUDIT_OUTPUT");
        var metadataEnabled = string.Equals(
            Environment.GetEnvironmentVariable("ANIMEGONET_MIKAN_RSS_METADATA"),
            "1",
            StringComparison.Ordinal);

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        var rssHttp = new RssFeedHttpClient(httpClient);
        var reader = new RssFeedReader(rssHttp);
        var feed = await reader.ParseUrlAsync(rssUrl);
        Assert.NotEmpty(feed.Items);

        var plan = MikanRssBatchPlanner.Create(feed, MikanRssRuleDefaults.Create());
        Assert.Equal(feed.Items.Count, plan.Items.Count);
        Assert.All(plan.Items, item => Assert.DoesNotContain("http", item.Decision.Reason, StringComparison.OrdinalIgnoreCase));

        Directory.CreateDirectory(outputRoot);
        var reportPath = Path.Combine(
            outputRoot,
            $"mikan-rss-audit-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.json");
        var metadata = metadataEnabled
            ? await ResolveMetadataAsync(plan, rssHttp)
            : Enumerable.Repeat(MetadataAudit.NotRun, plan.Items.Count).ToArray();
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
            metadata_enabled = metadataEnabled,
            canonical_season_count = metadata.Count(value => value.CanonicalTmdbSeason is not null),
            items = plan.Items.Select((item, index) => ToReportItem(item, index, metadata[index])),
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

    private static object ToReportItem(MikanRssPlannedItem item, int index, MetadataAudit metadata)
    {
        var raw = AutoBangumiRawParser.Parse(item.FeedItem.Title);
        return new
        {
            index = index + 1,
            title = item.FeedItem.Title,
            published_date = item.FeedItem.PublishedDate,
            content_type = item.FeedItem.ContentType,
            declared_length = item.FeedItem.Length,
            item_fingerprint = Fingerprint(string.Concat(item.FeedItem.MikanUrl, "\n", item.FeedItem.TorrentUrl)),
            title_season_hint = raw.Season > 0 ? raw.Season : (int?)null,
            title_season_raw = string.IsNullOrWhiteSpace(raw.SeasonRaw) ? null : raw.SeasonRaw,
            identity_mikanid = metadata.MikanId,
            identity_groupid = metadata.GroupId,
            bgmid = metadata.BangumiId,
            bangumi_name = metadata.BangumiName,
            bangumi_air_date = metadata.BangumiAirDate,
            tmdbid = metadata.TmdbId,
            tmdb_name = metadata.TmdbName,
            canonical_tmdb_season = metadata.CanonicalTmdbSeason,
            canonical_tmdb_episode = metadata.CanonicalTmdbEpisode,
            season_state = metadata.State,
            metadata_failure_code = metadata.FailureCode,
            tmdb_attempted_titles = metadata.AttemptedTitles,
            parsed_episode_kind = item.Candidate.SourceEpisodeKind,
            parsed_episode = item.Candidate.SourceEpisode,
            decision = item.Decision.Kind.ToString(),
            reason = item.Decision.Reason,
            evaluated_priority_groups = item.Decision.EvaluatedPriorityGroups,
        };
    }

    private static async Task<MetadataAudit[]> ResolveMetadataAsync(
        MikanRssBatchPlan plan,
        RssFeedHttpClient rssHttp)
    {
        var tmdbOptions = new TmdbClientOptions
        {
            BaseUrl = new Uri(Environment.GetEnvironmentVariable("ANIMEGONET_TMDB_BASE_URL") ?? "http://api.tmdb.local/"),
            ApiKey = Required("ANIMEGONET_TMDB_API_KEY"),
            RetryCount = 0,
            HttpTimeout = TimeSpan.FromSeconds(60),
        };
        var bangumiOptions = new BangumiClientOptions
        {
            BaseUrl = new Uri(Environment.GetEnvironmentVariable("ANIMEGONET_BANGUMI_BASE_URL") ?? "http://api.bgm.local/"),
            RetryCount = 0,
            HttpTimeout = TimeSpan.FromSeconds(60),
        };
        using var tmdbHttp = new HttpClient();
        using var bangumiHttp = new HttpClient();
        using var tmdb = new TmdbClient(tmdbHttp, tmdbOptions);
        using var bangumi = new BangumiSubjectClient(bangumiHttp, bangumiOptions);
        var seriesSeason = new TmdbSeriesSeasonResolver(new TmdbSeriesResolver(tmdb), tmdb);
        var bangumiResolver = new MikanBangumiSubjectResolver(rssHttp);
        var cache = new Dictionary<int, WorkMetadata>();
        var results = new MetadataAudit[plan.Items.Count];

        for (var index = 0; index < plan.Items.Count; index++)
        {
            var item = plan.Items[index];
            try
            {
                var page = await rssHttp.GetAsync(new Uri(item.FeedItem.MikanUrl));
                var identity = MikanEpisodeIdentityParser.Parse(page);
                if (!cache.TryGetValue(identity.MikanId, out var work))
                {
                    work = await ResolveWorkAsync(
                        item.FeedItem,
                        identity.MikanId,
                        bangumiResolver,
                        bangumi,
                        tmdb,
                        seriesSeason);
                    cache.Add(identity.MikanId, work);
                }

                results[index] = await ResolveEpisodeAsync(item, identity, work, bangumi, tmdb);
            }
            catch (MikanEpisodeIdentityException exception)
            {
                results[index] = MetadataAudit.Failed(exception.Code);
            }
            catch (BangumiClientException exception)
            {
                results[index] = MetadataAudit.Failed(exception.SafeCode);
            }
            catch (TmdbClientException exception)
            {
                results[index] = MetadataAudit.Failed(exception.SafeCode);
            }
            catch (HttpRequestException)
            {
                results[index] = MetadataAudit.Failed("metadata_network_error");
            }
        }

        return results;
    }

    private static async Task<WorkMetadata> ResolveWorkAsync(
        RssFeedItem item,
        int mikanId,
        MikanBangumiSubjectResolver bangumiResolver,
        BangumiSubjectClient bangumi,
        TmdbClient tmdb,
        TmdbSeriesSeasonResolver seriesSeason)
    {
        var discovery = await bangumiResolver.ResolveAsync(
            new RssFeedDocument([item], mikanId));
        if (!discovery.IsResolved)
        {
            return WorkMetadata.Failed(mikanId, discovery.FailureCode ?? "mikan_bgmid_discovery_failed");
        }

        var subject = await bangumi.GetSubjectAsync(discovery.BangumiSubjectId!.Value);
        if (subject is null)
        {
            return WorkMetadata.Failed(mikanId, "bangumi_subject_not_found", discovery.BangumiSubjectId);
        }

        var resolution = await seriesSeason.ResolveAsync(
            TmdbSeriesSeasonResolver.BangumiTitles(subject),
            subject.AirDate);
        if (!resolution.IsSuccess)
        {
            return new WorkMetadata(
                mikanId,
                subject,
                resolution.Details,
                null,
                resolution.Failure?.Code ?? "tmdb_season_not_resolved",
                resolution.AttemptedTitles);
        }

        return new WorkMetadata(
            mikanId,
            subject,
            resolution.Details,
            resolution.Season,
            null,
            resolution.AttemptedTitles);
    }

    private static async Task<MetadataAudit> ResolveEpisodeAsync(
        MikanRssPlannedItem item,
        MikanEpisodeIdentity identity,
        WorkMetadata work,
        BangumiSubjectClient bangumi,
        TmdbClient tmdb)
    {
        if (work.Subject is null || work.Details is null || work.Season is null)
        {
            return MetadataAudit.FromWork(identity, work);
        }

        int? canonicalEpisode = null;
        var state = "season_resolved_episode_not_applicable";
        var failure = (string?)null;
        if (string.Equals(item.Candidate.SourceEpisodeKind, "normal", StringComparison.Ordinal)
            && int.TryParse(item.Candidate.SourceEpisode, out var sourceEpisode)
            && sourceEpisode > 0)
        {
            var bangumiEpisodes = await bangumi.GetEpisodesAsync(work.Subject.Id);
            var tmdbEpisodes = work.Season.Episodes ?? [];
            var dateMatch = BangumiTmdbEpisodeDateResolver.Resolve(
                bangumiEpisodes,
                tmdbEpisodes,
                sourceEpisode);
            var targetEpisode = dateMatch.IsSuccess
                ? dateMatch.Episode!.EpisodeNumber
                : sourceEpisode;
            if (dateMatch.IsApplicable && !dateMatch.IsSuccess)
            {
                state = "season_resolved_episode_other";
                failure = dateMatch.FailureCode;
            }
            else
            {
                var verified = await tmdb.GetEpisodeAsync(
                    work.Details.Series.Id,
                    work.Season.SeasonNumber,
                    targetEpisode);
                if (verified is not null
                    && verified.SeriesId == work.Details.Series.Id
                    && verified.SeasonNumber == work.Season.SeasonNumber
                    && verified.EpisodeNumber == targetEpisode)
                {
                    canonicalEpisode = targetEpisode;
                    state = dateMatch.IsSuccess
                        ? "season_episode_resolved_by_bangumi_date"
                        : "season_episode_resolved_by_number";
                }
                else
                {
                    state = "season_resolved_episode_other";
                    failure = "tmdb_episode_not_found";
                }
            }
        }

        return MetadataAudit.FromWork(identity, work) with
        {
            CanonicalTmdbEpisode = canonicalEpisode,
            State = state,
            FailureCode = failure,
        };
    }

    private sealed record WorkMetadata(
        int MikanId,
        BangumiSubject? Subject,
        TmdbSeriesDetails? Details,
        TmdbSeason? Season,
        string? FailureCode,
        IReadOnlyList<string> AttemptedTitles)
    {
        public static WorkMetadata Failed(int mikanId, string code, int? bangumiId = null) =>
            new(mikanId,
                bangumiId is null ? null : new BangumiSubject(bangumiId.Value, string.Empty, string.Empty, null, 0),
                null,
                null,
                code,
                []);
    }

    private sealed record MetadataAudit(
        int? MikanId,
        int? GroupId,
        int? BangumiId,
        string? BangumiName,
        DateOnly? BangumiAirDate,
        int? TmdbId,
        string? TmdbName,
        int? CanonicalTmdbSeason,
        int? CanonicalTmdbEpisode,
        string State,
        string? FailureCode,
        IReadOnlyList<string> AttemptedTitles)
    {
        public static MetadataAudit NotRun { get; } =
            new(null, null, null, null, null, null, null, null, null, "not_run", null, []);

        public static MetadataAudit Failed(string code) =>
            NotRun with { State = "failed", FailureCode = code };

        public static MetadataAudit FromWork(MikanEpisodeIdentity identity, WorkMetadata work) =>
            new(
                identity.MikanId,
                identity.SubGroupId,
                work.Subject?.Id,
                string.IsNullOrWhiteSpace(work.Subject?.ChineseName) ? work.Subject?.Name : work.Subject.ChineseName,
                work.Subject?.AirDate,
                work.Details?.Series.Id,
                work.Details?.Series.Name,
                work.Season?.SeasonNumber,
                null,
                work.Season is null ? "season_failed" : "season_resolved",
                work.FailureCode,
                work.AttemptedTitles);
    }

    private static string Required(string name) =>
        Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"Set {name} before running this explicit local integration test.");
}
