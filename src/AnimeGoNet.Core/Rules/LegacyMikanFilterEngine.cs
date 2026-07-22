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

public static class LegacyMikanFilterEngine
{
    public static LegacyMikanFilterResult Evaluate(
        LegacyMikanFilterCandidate candidate,
        LegacyMikanFilterConfig config)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(config);
        var accepted0 = true;
        string? scope = null;
        string? key = null;
        foreach (var pair in config.Filiter0)
        {
            accepted0 = IsAccepted(pair.Value, candidate.Title);
            scope = "Filiter0";
            key = pair.Key;
        }

        var accepted123 = true;
        if (config.Filiter1.Count > 0 || config.Filiter2.Count > 0 || config.Filiter3.Count > 0)
        {
            if (candidate.MikanId is not > 0 || candidate.GroupId is not > 0)
            {
                return new LegacyMikanFilterResult(false, "MikanIdentityRequired", null, null);
            }

            var combined = $"key_{candidate.MikanId}_{candidate.GroupId}";
            var work = candidate.MikanId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var group = candidate.GroupId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (config.Filiter1.TryGetValue(combined, out var rule1))
            {
                accepted123 = IsAccepted(rule1, candidate.Title);
                scope = "Filiter1";
                key = combined;
            }
            else if (config.Filiter2.TryGetValue(work, out var rule2))
            {
                accepted123 = IsAccepted(rule2, candidate.Title);
                scope = "Filiter2";
                key = work;
            }
            else if (config.Filiter3.TryGetValue(group, out var rule3))
            {
                accepted123 = IsAccepted(rule3, candidate.Title);
                scope = "Filiter3";
                key = group;
            }
        }

        var accepted4 = true;
        if (config.Filiter4.TryGetValue(candidate.GroupName, out var rule4))
        {
            accepted4 = IsAccepted(rule4, candidate.Title);
            scope = "Filiter4";
            key = candidate.GroupName;
        }

        var accepted = accepted0 && accepted123 && accepted4;
        return new LegacyMikanFilterResult(
            accepted,
            accepted ? "Accepted" : "RejectedByLegacyMikanTool",
            scope,
            key);
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

    private static bool IsAccepted(LegacyMikanFilterRule rule, string title)
    {
        var whitelistMatch = rule.IsEnableWhitelist
            && rule.Whitelist.Any(value => title.Contains(value, StringComparison.Ordinal));
        var blacklistMatch = rule.IsEnableBlacklist
            && rule.Blacklist.Any(value => title.Contains(value, StringComparison.Ordinal));
        return (rule.IsEnableWhitelist, rule.IsEnableBlacklist) switch
        {
            (true, false) => whitelistMatch,
            (false, true) => !blacklistMatch,
            (true, true) => whitelistMatch && !blacklistMatch,
            _ => true,
        };
    }
}
