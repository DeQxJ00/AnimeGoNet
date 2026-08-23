using AnimeGoNet.Core.Rules;

namespace AnimeGoNet.Core.Tests.Rules;

public sealed class MikanRssVerifiedCoverageSelectorTests
{
    [Fact]
    public void MultiFileCandidateMustWinEveryCoveredEpisodeAndIsReturnedOnce()
    {
        var candidates = new[]
        {
            Candidate("single-2", "[LoliHouse] Show - 02 [简繁内封]", "2"),
            Candidate("single-3", "[LoliHouse] Show - 03 [简繁内封]", "3"),
            Candidate("pack-simple", "[澄空] Show [02-03] [简体]", null),
            Candidate("pack-traditional", "[澄空] Show [02-03] [繁体]", null),
        };
        var coverages = new[]
        {
            Coverage("single-2", 100, 1, 2),
            Coverage("single-3", 100, 1, 3),
            Coverage("pack-simple", 100, 1, 2, 3),
            Coverage("pack-traditional", 100, 1, 2, 3),
        };

        var decisions = MikanRssVerifiedCoverageSelector.Evaluate(
            candidates,
            coverages,
            Rules());

        var winner = Assert.Single(
            decisions,
            value => value.Kind == MikanRssDecisionKind.Winner);
        Assert.Equal("pack-simple", winner.CandidateId);
        Assert.Equal("VerifiedMultiEpisodePriorityWinner", winner.Reason);
        Assert.All(
            decisions.Where(value => value.CandidateId is "single-2" or "single-3"),
            value =>
            {
                Assert.Equal(MikanRssDecisionKind.SuppressedByHigherPriority, value.Kind);
                Assert.Equal("SuppressedByMultiEpisodeWinner", value.Reason);
                Assert.Equal("pack-simple", value.WinnerId);
            });
    }

    [Fact]
    public void PartiallyWinningPackIsRemovedBeforeCoveredEpisodesAreReevaluated()
    {
        var candidates = new[]
        {
            Candidate("single-2", "[Other] Show - 02", "2"),
            Candidate("single-3", "[澄空] Show - 03 [简体]", "3"),
            Candidate("pack", "[Preferred] Show [02-03]", null),
        };
        var coverages = new[]
        {
            Coverage("single-2", 100, 1, 2),
            Coverage("single-3", 100, 1, 3),
            Coverage("pack", 100, 1, 2, 3),
        };
        var rules = new MikanRssRuleSet(
            [],
            [],
            [
                new PriorityGroup("episode-3-special", "Episode 3 special", [
                    new NamedMatchArray("sumi", "Sumi", true, ["澄空"]),
                    new NamedMatchArray("preferred", "Preferred", true, ["preferred"]),
                ]),
                new PriorityGroup("pack-next", "Pack next", [
                    new NamedMatchArray("preferred", "Preferred", true, ["preferred"]),
                ]),
            ]);

        var decisions = MikanRssVerifiedCoverageSelector.Evaluate(candidates, coverages, rules);

        Assert.Equal(
            "PartialCoverageConflict",
            decisions.Single(value => value.CandidateId == "pack").Reason);
        Assert.Equal(
            ["single-2", "single-3"],
            decisions.Where(value => value.Kind == MikanRssDecisionKind.Winner)
                .Select(value => value.CandidateId)
                .Order()
                .ToArray());
    }

    private static MikanRssCandidate Candidate(string id, string title, string? episode) =>
        new(id, title, 3981, episode is null ? null : "normal", episode);

    private static MikanRssVerifiedCoverage Coverage(
        string candidateId,
        int series,
        int season,
        params int[] episodes) =>
        new(candidateId, series, season, episodes);

    private static MikanRssRuleSet Rules() => new(
        [],
        [],
        [
            new PriorityGroup("subtitle-group", "Subtitle group", [
                new NamedMatchArray("sumi", "Sumi", true, ["澄空"]),
            ]),
            new PriorityGroup("language", "Language", [
                new NamedMatchArray("simple", "Simplified", true, ["简体"]),
                new NamedMatchArray("traditional", "Traditional", true, ["繁体"]),
            ]),
        ]);
}
