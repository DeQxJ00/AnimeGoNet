using System.Globalization;

namespace AnimeGoNet.Core.Rules;

public sealed record MikanRssVerifiedCoverage(
    string CandidateId,
    int TmdbSeriesId,
    int TmdbSeasonNumber,
    IReadOnlyList<int> EpisodeNumbers);

public static class MikanRssVerifiedCoverageSelector
{
    public static IReadOnlyList<MikanRssDecision> Evaluate(
        IReadOnlyList<MikanRssCandidate> candidates,
        IReadOnlyList<MikanRssVerifiedCoverage> coverages,
        MikanRssRuleSet rules)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(coverages);
        ArgumentNullException.ThrowIfNull(rules);
        var candidateById = candidates.ToDictionary(value => value.Id, StringComparer.Ordinal);
        if (candidateById.Count != candidates.Count)
        {
            throw new ArgumentException("Candidate ids must be unique.", nameof(candidates));
        }

        var coverageById = coverages.ToDictionary(value => value.CandidateId, StringComparer.Ordinal);
        if (coverageById.Count != coverages.Count)
        {
            throw new ArgumentException("Coverage candidate ids must be unique.", nameof(coverages));
        }

        foreach (var coverage in coverages)
        {
            if (!candidateById.ContainsKey(coverage.CandidateId)
                || coverage.TmdbSeriesId <= 0
                || coverage.TmdbSeasonNumber <= 0
                || coverage.EpisodeNumbers.Count == 0
                || coverage.EpisodeNumbers.Any(value => value <= 0)
                || coverage.EpisodeNumbers.Distinct().Count() != coverage.EpisodeNumbers.Count)
            {
                throw new ArgumentException("Verified coverage is invalid.", nameof(coverages));
            }
        }

        var active = coverageById.Keys.ToHashSet(StringComparer.Ordinal);
        var partialConflicts = new HashSet<string>(StringComparer.Ordinal);
        var partialConflictGroups = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        Dictionary<EpisodeIdentity, EpisodeEvaluation> evaluations;
        while (true)
        {
            evaluations = EvaluateEpisodes(active, candidateById, coverageById, rules);
            var remove = active
                .Where(id => coverageById[id].EpisodeNumbers.Count > 1)
                .Where(id =>
                {
                    var identities = Identities(coverageById[id]);
                    var won = identities.Count(identity =>
                        evaluations.TryGetValue(identity, out var evaluation)
                        && string.Equals(evaluation.WinnerId, id, StringComparison.Ordinal));
                    return won > 0 && won < identities.Length;
                })
                .ToArray();
            if (remove.Length == 0)
            {
                break;
            }

            foreach (var id in remove)
            {
                partialConflictGroups[id] = Identities(coverageById[id])
                    .Where(evaluations.ContainsKey)
                    .SelectMany(identity => evaluations[identity].Decisions[id].EvaluatedPriorityGroups)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                active.Remove(id);
                partialConflicts.Add(id);
            }
        }

        var decisions = new List<MikanRssDecision>(coverages.Count);
        foreach (var candidate in candidates.Where(value => coverageById.ContainsKey(value.Id)))
        {
            var coverage = coverageById[candidate.Id];
            var identities = Identities(coverage);
            if (partialConflicts.Contains(candidate.Id))
            {
                decisions.Add(new MikanRssDecision(
                    candidate.Id,
                    MikanRssDecisionKind.SuppressedByHigherPriority,
                    "PartialCoverageConflict",
                    null,
                    partialConflictGroups[candidate.Id]));
                continue;
            }

            var episodeDecisions = identities
                .Where(evaluations.ContainsKey)
                .Select(identity => evaluations[identity].Decisions[candidate.Id])
                .ToArray();
            var groups = episodeDecisions
                .SelectMany(value => value.EvaluatedPriorityGroups)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var wonAll = identities.All(identity =>
                evaluations.TryGetValue(identity, out var evaluation)
                && string.Equals(evaluation.WinnerId, candidate.Id, StringComparison.Ordinal));
            if (wonAll)
            {
                decisions.Add(new MikanRssDecision(
                    candidate.Id,
                    MikanRssDecisionKind.Winner,
                    coverage.EpisodeNumbers.Count > 1
                        ? "VerifiedMultiEpisodePriorityWinner"
                        : "VerifiedEpisodePriorityWinner",
                    candidate.Id,
                    groups));
                continue;
            }

            var rejection = episodeDecisions.FirstOrDefault(value => value.Kind is
                MikanRssDecisionKind.RejectedByBlacklist or MikanRssDecisionKind.RejectedByWhitelist);
            if (rejection is not null)
            {
                decisions.Add(rejection with { EvaluatedPriorityGroups = groups });
                continue;
            }

            var winner = identities
                .Select(identity => evaluations.TryGetValue(identity, out var evaluation)
                    ? evaluation.WinnerId
                    : null)
                .FirstOrDefault(value => value is not null);
            var winnerIsMulti = winner is not null
                && coverageById.TryGetValue(winner, out var winningCoverage)
                && winningCoverage.EpisodeNumbers.Count > 1;
            decisions.Add(new MikanRssDecision(
                candidate.Id,
                MikanRssDecisionKind.SuppressedByHigherPriority,
                winnerIsMulti ? "SuppressedByMultiEpisodeWinner" : "SuppressedByHigherPriority",
                winner,
                groups));
        }

        return decisions;
    }

    private static Dictionary<EpisodeIdentity, EpisodeEvaluation> EvaluateEpisodes(
        HashSet<string> active,
        Dictionary<string, MikanRssCandidate> candidates,
        Dictionary<string, MikanRssVerifiedCoverage> coverages,
        MikanRssRuleSet rules)
    {
        var identities = active
            .SelectMany(id => Identities(coverages[id]))
            .Distinct()
            .OrderBy(value => value.SeriesId)
            .ThenBy(value => value.SeasonNumber)
            .ThenBy(value => value.EpisodeNumber)
            .ToArray();
        var result = new Dictionary<EpisodeIdentity, EpisodeEvaluation>();
        foreach (var identity in identities)
        {
            var episodeCandidates = active
                .Where(id => Identities(coverages[id]).Contains(identity))
                .Select(id => candidates[id] with
                {
                    SourceEpisodeKind = "verified",
                    SourceEpisode = identity.Key,
                })
                .ToArray();
            var decisions = MikanRssRuleEngine.Evaluate(episodeCandidates, rules)
                .ToDictionary(value => value.CandidateId, StringComparer.Ordinal);
            result.Add(
                identity,
                new EpisodeEvaluation(
                    decisions.Values.SingleOrDefault(value => value.Kind == MikanRssDecisionKind.Winner)?.CandidateId,
                    decisions));
        }

        return result;
    }

    private static EpisodeIdentity[] Identities(MikanRssVerifiedCoverage coverage) =>
        coverage.EpisodeNumbers
            .Order()
            .Select(value => new EpisodeIdentity(
                coverage.TmdbSeriesId,
                coverage.TmdbSeasonNumber,
                value))
            .ToArray();

    private sealed record EpisodeEvaluation(
        string? WinnerId,
        IReadOnlyDictionary<string, MikanRssDecision> Decisions);

    private readonly record struct EpisodeIdentity(
        int SeriesId,
        int SeasonNumber,
        int EpisodeNumber)
    {
        public string Key => string.Join(
            '/',
            SeriesId.ToString(CultureInfo.InvariantCulture),
            SeasonNumber.ToString(CultureInfo.InvariantCulture),
            EpisodeNumber.ToString(CultureInfo.InvariantCulture));
    }
}
