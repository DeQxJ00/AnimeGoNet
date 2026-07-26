using AnimeGoNet.Core.Feeds;
using AnimeGoNet.Core.Rules;

namespace AnimeGoNet.Core.Tests.Feeds;

public sealed class MikanRssBatchPlannerTests
{
    [Fact]
    public void SameWorkAndEpisodeUsesRulesAndStopsAtOneWinner()
    {
        var plan = MikanRssBatchPlanner.Create(
            Feed(3951,
                Item("[繁] Show [03] [1080p]", "a"),
                Item("[简] Show [03] [1080p]", "b")),
            Rules(priorityGroups:
            [
                new PriorityGroup("language", "Language",
                [
                    new NamedMatchArray("simplified", "Simplified", true, ["简"]),
                    new NamedMatchArray("traditional", "Traditional", true, ["繁"]),
                ]),
            ]));

        Assert.Equal(2, plan.Items.Count);
        Assert.Single(plan.Winners);
        Assert.Equal("[简] Show [03] [1080p]", plan.Winners[0].FeedItem.Title);
        Assert.Equal("PriorityWinner", plan.Winners[0].Decision.Reason);
        Assert.Equal(
            MikanRssDecisionKind.SuppressedByHigherPriority,
            plan.Items.Single(item => item.FeedItem.Title.StartsWith("[繁]", StringComparison.Ordinal)).Decision.Kind);
    }

    [Fact]
    public void BlacklistRunsBeforeSingleCandidateBypass()
    {
        var plan = MikanRssBatchPlanner.Create(
            Feed(3951, Item("[Group] Show [03] [720p]", "a")),
            Rules(blacklist:
            [
                new NamedMatchArray("reject-720p", "Reject 720p", true, ["720p"]),
            ]));

        var item = Assert.Single(plan.Items);
        Assert.Equal(MikanRssDecisionKind.RejectedByBlacklist, item.Decision.Kind);
        Assert.Empty(plan.Winners);
    }

    [Fact]
    public void DifferentEpisodesAndUnknownEpisodeNeverCompete()
    {
        var plan = MikanRssBatchPlanner.Create(
            Feed(3951,
                Item("Show [03]", "a"),
                Item("Show [04]", "b"),
                Item("Show [1080p]", "c")),
            Rules(priorityGroups:
            [
                new PriorityGroup("only-a", "Only A",
                [
                    new NamedMatchArray("a", "A", true, ["show"]),
                ]),
            ]));

        Assert.Equal(3, plan.Winners.Count);
        Assert.All(plan.Items, item =>
            Assert.True(item.Decision.Reason is "SingleCandidateBypass" or "UngroupedBypass"));
        Assert.Null(plan.Items[2].Candidate.SourceEpisodeKind);
        Assert.Null(plan.Items[2].Candidate.SourceEpisode);
    }

    [Fact]
    public void FractionalEpisodeHasItsOwnNonIntegerGroup()
    {
        var plan = MikanRssBatchPlanner.Create(
            Feed(3951,
                Item("Show [48.5] x264", "a"),
                Item("Show [48.5] x265", "b"),
                Item("Show [48] x264", "c")),
            Rules(priorityGroups:
            [
                new PriorityGroup("codec", "Codec",
                [
                    new NamedMatchArray("x265", "x265", true, ["x265"]),
                    new NamedMatchArray("x264", "x264", true, ["x264"]),
                ]),
            ]));

        Assert.Equal("fractional", plan.Items[0].Candidate.SourceEpisodeKind);
        Assert.Equal("48.5", plan.Items[0].Candidate.SourceEpisode);
        Assert.Null(MikanRssEpisodeParser.Parse(plan.Items[0].FeedItem.Title).NormalEpisode);
        Assert.Equal(2, plan.Winners.Count);
        Assert.Contains(plan.Winners, item => item.FeedItem.Title.Contains("[48.5] x265", StringComparison.Ordinal));
        Assert.Contains(plan.Winners, item => item.FeedItem.Title.Contains("[48] x264", StringComparison.Ordinal));
    }

    [Fact]
    public void DisabledPriorityReturnsEveryItemWithoutEvaluatingRules()
    {
        var plan = MikanRssBatchPlanner.Create(
            Feed(3951,
                Item("Show [03] [720p]", "a"),
                Item("Show [03] [1080p]", "b")),
            Rules(blacklist:
            [
                new NamedMatchArray("all", "All", true, ["show"]),
            ]),
            priorityEnabled: false);

        Assert.Equal(2, plan.Winners.Count);
        Assert.All(plan.Items, item => Assert.Equal("SkippedByConfiguration", item.Decision.Reason));
    }

    [Fact]
    public void StableIdPrefersMikanUrlAndDuplicateEntriesRemainUnique()
    {
        var first = MikanRssBatchPlanner.Create(
            Feed(3951, Item("Show [03]", "same", "https://mikanani.me/download/credential-one")),
            Rules());
        var second = MikanRssBatchPlanner.Create(
            Feed(3951, Item("Show [03]", "same", "https://mikanani.me/download/credential-two")),
            Rules());
        var duplicate = MikanRssBatchPlanner.Create(
            Feed(3951, Item("Show [03]", "same"), Item("Show [03]", "same")),
            Rules());

        Assert.Equal(first.Items[0].Candidate.Id, second.Items[0].Candidate.Id);
        Assert.DoesNotContain("credential", first.Items[0].Candidate.Id, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(duplicate.Items[0].Candidate.Id, duplicate.Items[1].Candidate.Id);
    }

    [Fact]
    public void LegacyRejectedAndFailedItemsNeverEnterPriorityCompetition()
    {
        var audits = new[]
        {
            new MikanLegacyFilterAudit(
                MikanLegacyFilterState.Rejected, "RejectedByLegacyMikanTool", "Filiter1", "key_3951_370", 3951, 370),
            new MikanLegacyFilterAudit(
                MikanLegacyFilterState.Accepted, "Accepted", "Filiter2", "3951", 3951, 370),
            new MikanLegacyFilterAudit(
                MikanLegacyFilterState.FilterEvaluationFailed, "mikan_identity_link_missing"),
        };
        var plan = MikanRssBatchPlanner.Create(
            Feed(3951,
                Item("Show [03] x265", "a"),
                Item("Show [03] x264", "b"),
                Item("Show [03] x265", "c")),
            Rules(priorityGroups:
            [
                new PriorityGroup("codec", "Codec",
                [
                    new NamedMatchArray("x265", "x265", true, ["x265"]),
                    new NamedMatchArray("x264", "x264", true, ["x264"]),
                ]),
            ]),
            legacyFilterAudits: audits,
            legacyFilterRevision: 7,
            legacyFilterEnabled: true);

        Assert.Equal(7, plan.LegacyFilterRevision);
        Assert.True(plan.LegacyFilterEnabled);
        Assert.Equal(MikanRssDecisionKind.RejectedByLegacyFilter, plan.Items[0].Decision.Kind);
        Assert.Equal(MikanRssDecisionKind.Winner, plan.Items[1].Decision.Kind);
        Assert.Equal("SingleCandidateBypass", plan.Items[1].Decision.Reason);
        Assert.Equal(MikanRssDecisionKind.FilterEvaluationFailed, plan.Items[2].Decision.Kind);
        Assert.Single(plan.Winners);
    }

    [Fact]
    public void LegacyAuditCountMustMatchOriginalFeed()
    {
        var exception = Assert.Throws<ArgumentException>(() => MikanRssBatchPlanner.Create(
            Feed(3951, Item("Show [03]", "a")),
            Rules(),
            legacyFilterAudits: []));

        Assert.Equal("legacyFilterAudits", exception.ParamName);
    }

    private static RssFeedDocument Feed(int? mikanId, params RssFeedItem[] items) => new(items, mikanId);

    private static RssFeedItem Item(string title, string identity, string? torrentUrl = null) => new(
        title,
        $"https://mikanani.me/Home/Episode/{identity}",
        torrentUrl ?? $"https://mikanani.me/Download/{identity}.torrent",
        "application/x-bittorrent",
        42,
        "2026-07-22");

    private static MikanRssRuleSet Rules(
        IReadOnlyList<NamedMatchArray>? whitelist = null,
        IReadOnlyList<NamedMatchArray>? blacklist = null,
        IReadOnlyList<PriorityGroup>? priorityGroups = null) =>
        new(whitelist ?? [], blacklist ?? [], priorityGroups ?? []);
}
