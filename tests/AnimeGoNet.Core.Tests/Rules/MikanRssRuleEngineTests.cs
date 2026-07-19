using AnimeGoNet.Core.Rules;

namespace AnimeGoNet.Core.Tests.Rules;

public sealed class MikanRssRuleEngineTests
{
    [Fact]
    public void DefaultRulesReject720pBeforePriorityEvaluation()
    {
        var decision = Assert.Single(MikanRssRuleEngine.Evaluate(
            [Candidate("only", "简体 HEVC 720P")],
            MikanRssRuleDefaults.Create()));

        Assert.Equal(MikanRssDecisionKind.RejectedByBlacklist, decision.Kind);
        Assert.Equal("blacklist:resolution-720p", decision.Reason);
    }

    [Fact]
    public void BlacklistRunsBeforeWhitelistEvenForSingleCandidate()
    {
        var decisions = MikanRssRuleEngine.Evaluate(
            [Candidate("one", "CHS 720P")],
            new MikanRssRuleSet(
                [Array("language", "chs")],
                [Array("720", "720p")],
                []));

        var decision = Assert.Single(decisions);
        Assert.Equal(MikanRssDecisionKind.RejectedByBlacklist, decision.Kind);
        Assert.Empty(decision.EvaluatedPriorityGroups);
    }

    [Fact]
    public void LowercasePriorityOrderShortCircuitsAfterOneWinner()
    {
        var decisions = MikanRssRuleEngine.Evaluate(
            [
                Candidate("traditional", "繁體 HEVC"),
                Candidate("simplified", "簡體 H264"),
            ],
            new MikanRssRuleSet(
                [],
                [],
                [
                    new PriorityGroup("language", "language", [Array("simplified", "简体", "簡體"), Array("traditional", "繁體")]),
                    new PriorityGroup("codec", "codec", [Array("hevc", "hevc"), Array("h264", "h264")]),
                ]));

        var winner = Assert.Single(decisions, item => item.Kind == MikanRssDecisionKind.Winner);
        Assert.Equal("simplified", winner.CandidateId);
        Assert.Equal(["language"], winner.EvaluatedPriorityGroups);
        Assert.Equal("simplified", Assert.Single(decisions, item => item.CandidateId == "traditional").WinnerId);
    }

    [Fact]
    public void SingleEligibleCandidateBypassesAllPriorityGroups()
    {
        var decisions = MikanRssRuleEngine.Evaluate(
            [Candidate("blocked", "720p"), Candidate("winner", "1080P")],
            new MikanRssRuleSet(
                [],
                [Array("720", "720p")],
                [new PriorityGroup("resolution", "resolution", [Array("1080", "1080p")])]));

        var winner = Assert.Single(decisions, item => item.Kind == MikanRssDecisionKind.Winner);
        Assert.Equal("SingleCandidateBypass", winner.Reason);
        Assert.Empty(winner.EvaluatedPriorityGroups);
    }

    [Fact]
    public void TiesUseStableRssOrderAndDifferentEpisodesDoNotCompete()
    {
        var decisions = MikanRssRuleEngine.Evaluate(
            [Candidate("first", "A"), Candidate("second", "A"), Candidate("ep2", "A", episode: "2")],
            new MikanRssRuleSet([], [], []));

        Assert.Equal("StableRssOrder", Assert.Single(decisions, item => item.CandidateId == "first").Reason);
        Assert.Equal(MikanRssDecisionKind.SuppressedByHigherPriority, Assert.Single(decisions, item => item.CandidateId == "second").Kind);
        Assert.Equal("SingleCandidateBypass", Assert.Single(decisions, item => item.CandidateId == "ep2").Reason);
    }

    private static MikanRssCandidate Candidate(string id, string title, string episode = "1") =>
        new(id, title, 3951, "normal", episode);

    private static NamedMatchArray Array(string id, params string[] values) =>
        new(id, id, true, values);
}
