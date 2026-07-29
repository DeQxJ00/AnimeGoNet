namespace AnimeGoNet.Core.Rules;

public sealed record LegacyMikanFilterRule(
    bool IsEnableWhitelist,
    bool IsEnableBlacklist,
    IReadOnlyList<string> Whitelist,
    IReadOnlyList<string> Blacklist);

public sealed record LegacyMikanFilterConfig(
    IReadOnlyList<KeyValuePair<string, LegacyMikanFilterRule>> Filiter0,
    IReadOnlyDictionary<string, LegacyMikanFilterRule> Filiter1,
    IReadOnlyDictionary<string, LegacyMikanFilterRule> Filiter2,
    IReadOnlyDictionary<string, LegacyMikanFilterRule> Filiter3,
    IReadOnlyDictionary<string, LegacyMikanFilterRule> Filiter4);

public sealed record LegacyMikanFilterCandidate(
    string Title,
    int? MikanId,
    int? GroupId,
    string GroupName);

public sealed record LegacyMikanFilterResult(
    bool Accepted,
    string Reason,
    string? MatchedScope,
    string? MatchedKey);

public sealed record LegacyMikanFilterTraceStep(
    string Tier,
    string? Key,
    bool Applicable,
    bool? Accepted,
    IReadOnlyList<string> WhitelistMatches,
    IReadOnlyList<string> BlacklistMatches,
    string Reason);

public sealed record LegacyMikanFilterPreview(
    LegacyMikanFilterResult Result,
    IReadOnlyList<LegacyMikanFilterTraceStep> Steps);

public static class LegacyMikanFilterEngine
{
    public static LegacyMikanFilterResult Evaluate(
        LegacyMikanFilterCandidate candidate,
        LegacyMikanFilterConfig config) =>
        Preview(candidate, config).Result;

    public static LegacyMikanFilterPreview Preview(
        LegacyMikanFilterCandidate candidate,
        LegacyMikanFilterConfig config)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(config);
        var steps = new List<LegacyMikanFilterTraceStep>();
        var accepted0 = true;
        string? scope = null;
        string? key = null;
        foreach (var pair in config.Filiter0)
        {
            var evaluation = EvaluateRule(pair.Value, candidate.Title);
            accepted0 = evaluation.Accepted;
            scope = "Filiter0";
            key = pair.Key;
            steps.Add(ToStep("Filiter0", pair.Key, evaluation));
        }

        var accepted123 = true;
        if (config.Filiter1.Count > 0 || config.Filiter2.Count > 0 || config.Filiter3.Count > 0)
        {
            if (candidate.MikanId is not > 0 || candidate.GroupId is not > 0)
            {
                steps.Add(new LegacyMikanFilterTraceStep(
                    "Filiter1-3", null, false, false, [], [],
                    "MikanIdentityRequired"));
                return new LegacyMikanFilterPreview(
                    new LegacyMikanFilterResult(false, "MikanIdentityRequired", null, null),
                    steps);
            }

            var combined = $"key_{candidate.MikanId}_{candidate.GroupId}";
            var work = candidate.MikanId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var group = candidate.GroupId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (config.Filiter1.TryGetValue(combined, out var rule1))
            {
                var evaluation = EvaluateRule(rule1, candidate.Title);
                accepted123 = evaluation.Accepted;
                scope = "Filiter1";
                key = combined;
                steps.Add(ToStep("Filiter1", combined, evaluation));
                steps.Add(NotApplicable("Filiter2", work, "HigherTierMatched"));
                steps.Add(NotApplicable("Filiter3", group, "HigherTierMatched"));
            }
            else if (config.Filiter2.TryGetValue(work, out var rule2))
            {
                steps.Add(NotApplicable("Filiter1", combined, "NoMatchingRule"));
                var evaluation = EvaluateRule(rule2, candidate.Title);
                accepted123 = evaluation.Accepted;
                scope = "Filiter2";
                key = work;
                steps.Add(ToStep("Filiter2", work, evaluation));
                steps.Add(NotApplicable("Filiter3", group, "HigherTierMatched"));
            }
            else if (config.Filiter3.TryGetValue(group, out var rule3))
            {
                steps.Add(NotApplicable("Filiter1", combined, "NoMatchingRule"));
                steps.Add(NotApplicable("Filiter2", work, "NoMatchingRule"));
                var evaluation = EvaluateRule(rule3, candidate.Title);
                accepted123 = evaluation.Accepted;
                scope = "Filiter3";
                key = group;
                steps.Add(ToStep("Filiter3", group, evaluation));
            }
            else
            {
                steps.Add(NotApplicable("Filiter1", combined, "NoMatchingRule"));
                steps.Add(NotApplicable("Filiter2", work, "NoMatchingRule"));
                steps.Add(NotApplicable("Filiter3", group, "NoMatchingRule"));
            }
        }
        else
        {
            steps.Add(NotApplicable("Filiter1-3", null, "NoConfiguredRules"));
        }

        var accepted4 = true;
        if (config.Filiter4.TryGetValue(candidate.GroupName, out var rule4))
        {
            var evaluation = EvaluateRule(rule4, candidate.Title);
            accepted4 = evaluation.Accepted;
            scope = "Filiter4";
            key = candidate.GroupName;
            steps.Add(ToStep("Filiter4", candidate.GroupName, evaluation));
        }
        else
        {
            steps.Add(NotApplicable("Filiter4", candidate.GroupName, "NoMatchingRule"));
        }

        var accepted = accepted0 && accepted123 && accepted4;
        return new LegacyMikanFilterPreview(
            new LegacyMikanFilterResult(
                accepted,
                accepted ? "Accepted" : "RejectedByLegacyMikanTool",
                scope,
                key),
            steps);
    }

    public static string ParseGroupName(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        var normalized = title.Replace('【', '[').Replace('】', ']');
        var start = normalized.IndexOf('[', StringComparison.Ordinal);
        if (start < 0) return string.Empty;
        var end = normalized.IndexOf(']', start + 1);
        return end > start ? normalized[(start + 1)..end] : string.Empty;
    }

    private static RuleEvaluation EvaluateRule(LegacyMikanFilterRule rule, string title)
    {
        var whitelistMatches = rule.IsEnableWhitelist
            ? rule.Whitelist.Where(value => title.Contains(value, StringComparison.Ordinal)).ToArray()
            : [];
        var blacklistMatches = rule.IsEnableBlacklist
            ? rule.Blacklist.Where(value => title.Contains(value, StringComparison.Ordinal)).ToArray()
            : [];
        var accepted = (rule.IsEnableWhitelist, rule.IsEnableBlacklist) switch
        {
            (true, false) => whitelistMatches.Length > 0,
            (false, true) => blacklistMatches.Length == 0,
            (true, true) => whitelistMatches.Length > 0 && blacklistMatches.Length == 0,
            _ => true,
        };
        return new RuleEvaluation(accepted, whitelistMatches, blacklistMatches);
    }

    private static LegacyMikanFilterTraceStep ToStep(
        string tier,
        string key,
        RuleEvaluation evaluation) =>
        new(
            tier,
            key,
            true,
            evaluation.Accepted,
            evaluation.WhitelistMatches,
            evaluation.BlacklistMatches,
            evaluation.Accepted ? "Accepted" : "RejectedByRule");

    private static LegacyMikanFilterTraceStep NotApplicable(
        string tier,
        string? key,
        string reason) =>
        new(tier, key, false, null, [], [], reason);

    private sealed record RuleEvaluation(
        bool Accepted,
        IReadOnlyList<string> WhitelistMatches,
        IReadOnlyList<string> BlacklistMatches);
}
