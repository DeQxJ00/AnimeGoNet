namespace AnimeGoNet.Core.Rules;

public static class MikanRssRuleEngine
{
    public static IReadOnlyList<MikanRssDecision> Evaluate(
        IReadOnlyList<MikanRssCandidate> candidates,
        MikanRssRuleSet rules)
    {
        var decisions = new List<MikanRssDecision>(candidates.Count);
        var eligible = new List<(MikanRssCandidate Candidate, int Index, string LowerTitle)>();
        var activeWhitelist = rules.Whitelist.Where(IsActive).ToArray();

        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            var lowerTitle = candidate.Title.ToLowerInvariant();
            var blacklist = rules.Blacklist.FirstOrDefault(rule => IsActive(rule) && Matches(lowerTitle, rule));
            if (blacklist is not null)
            {
                decisions.Add(new MikanRssDecision(
                    candidate.Id,
                    MikanRssDecisionKind.RejectedByBlacklist,
                    $"blacklist:{blacklist.Id}",
                    null,
                    []));
                continue;
            }

            if (activeWhitelist.Length > 0 && !activeWhitelist.Any(rule => Matches(lowerTitle, rule)))
            {
                decisions.Add(new MikanRssDecision(
                    candidate.Id,
                    MikanRssDecisionKind.RejectedByWhitelist,
                    "whitelist:no_match",
                    null,
                    []));
                continue;
            }

            eligible.Add((candidate, index, lowerTitle));
        }

        foreach (var grouping in eligible.GroupBy(item => GetGroupKey(item.Candidate)))
        {
            var group = grouping.OrderBy(item => item.Index).ToList();
            if (grouping.Key.StartsWith("ungrouped:", StringComparison.Ordinal))
            {
                AddWinner(decisions, group[0], "UngroupedBypass", []);
                continue;
            }

            if (group.Count == 1)
            {
                AddWinner(decisions, group[0], "SingleCandidateBypass", []);
                continue;
            }

            var remaining = group;
            var evaluatedGroups = new List<string>();
            foreach (var priorityGroup in rules.PriorityGroups)
            {
                evaluatedGroups.Add(priorityGroup.Id);
                var firstMatchingArray = priorityGroup.Arrays.FirstOrDefault(array =>
                    IsActive(array) && remaining.Any(item => Matches(item.LowerTitle, array)));
                if (firstMatchingArray is null)
                {
                    continue;
                }

                remaining = remaining.Where(item => Matches(item.LowerTitle, firstMatchingArray)).ToList();
                if (remaining.Count == 1)
                {
                    break;
                }
            }

            var winner = remaining[0];
            AddWinner(decisions, winner, remaining.Count == 1 ? "PriorityWinner" : "StableRssOrder", evaluatedGroups);
            foreach (var loser in group.Where(item => item.Index != winner.Index))
            {
                decisions.Add(new MikanRssDecision(
                    loser.Candidate.Id,
                    MikanRssDecisionKind.SuppressedByHigherPriority,
                    "SuppressedByHigherPriority",
                    winner.Candidate.Id,
                    evaluatedGroups));
            }
        }

        return decisions.OrderBy(decision => IndexOf(candidates, decision.CandidateId)).ToArray();
    }

    private static void AddWinner(
        List<MikanRssDecision> decisions,
        (MikanRssCandidate Candidate, int Index, string LowerTitle) winner,
        string reason,
        IReadOnlyList<string> groups) =>
        decisions.Add(new MikanRssDecision(
            winner.Candidate.Id,
            MikanRssDecisionKind.Winner,
            reason,
            winner.Candidate.Id,
            groups.ToArray()));

    private static string GetGroupKey(MikanRssCandidate candidate)
    {
        if (candidate.MikanId is not > 0
            || string.IsNullOrWhiteSpace(candidate.SourceEpisodeKind)
            || string.IsNullOrWhiteSpace(candidate.SourceEpisode))
        {
            return "ungrouped:" + candidate.Id;
        }

        return string.Concat(
            candidate.MikanId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "|",
            candidate.SourceEpisodeKind.Trim().ToLowerInvariant(),
            "|",
            candidate.SourceEpisode.Trim().ToLowerInvariant());
    }

    private static bool IsActive(NamedMatchArray array) =>
        array.Enabled && array.Values.Any(value => !string.IsNullOrEmpty(value));

    private static bool Matches(string lowerTitle, NamedMatchArray array) =>
        array.Values.Any(value => !string.IsNullOrEmpty(value)
            && lowerTitle.Contains(value.ToLowerInvariant(), StringComparison.Ordinal));

    private static int IndexOf(IReadOnlyList<MikanRssCandidate> candidates, string id)
    {
        for (var index = 0; index < candidates.Count; index++)
        {
            if (string.Equals(candidates[index].Id, id, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return int.MaxValue;
    }
}
