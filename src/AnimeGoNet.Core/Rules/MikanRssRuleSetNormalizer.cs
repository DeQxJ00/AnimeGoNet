namespace AnimeGoNet.Core.Rules;

public static class MikanRssRuleSetNormalizer
{
    public static MikanRssRuleSet Normalize(MikanRssRuleSet rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var groupIds = new HashSet<string>(StringComparer.Ordinal);
        var whitelist = NormalizeArrays(rules.Whitelist, ids, "whitelist");
        var blacklist = NormalizeArrays(rules.Blacklist, ids, "blacklist");
        var groups = new List<PriorityGroup>(rules.PriorityGroups.Count);
        foreach (var group in rules.PriorityGroups)
        {
            var id = NormalizeId(group.Id, "priority group");
            if (!groupIds.Add(id))
            {
                throw new ArgumentException($"Duplicate priority group id '{id}'.", nameof(rules));
            }

            groups.Add(new PriorityGroup(
                id,
                NormalizeName(group.Name, "priority group"),
                NormalizeArrays(group.Arrays, ids, $"priority group '{id}'")));
        }

        return new MikanRssRuleSet(whitelist, blacklist, groups);
    }

    private static List<NamedMatchArray> NormalizeArrays(
        IReadOnlyList<NamedMatchArray> arrays,
        HashSet<string> ids,
        string scope)
    {
        var normalized = new List<NamedMatchArray>(arrays.Count);
        foreach (var array in arrays)
        {
            var id = NormalizeId(array.Id, "match array");
            if (!ids.Add(id))
            {
                throw new ArgumentException($"Duplicate match array id '{id}'.", nameof(arrays));
            }

            var values = array.Values
                .Select(value => value?.Trim().ToLowerInvariant() ?? string.Empty)
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            normalized.Add(new NamedMatchArray(
                id, NormalizeName(array.Name, $"{scope} match array"), array.Enabled, values));
        }

        return normalized;
    }

    private static string NormalizeId(string value, string kind)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalized.Length is 0 or > 80
            || normalized.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
        {
            throw new ArgumentException($"{kind} id must be 1-80 lowercase ASCII letters, numbers, '-' or '_'.");
        }

        return normalized;
    }

    private static string NormalizeName(string value, string kind)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is 0 or > 120)
        {
            throw new ArgumentException($"{kind} name must be 1-120 characters.");
        }

        return normalized;
    }
}
