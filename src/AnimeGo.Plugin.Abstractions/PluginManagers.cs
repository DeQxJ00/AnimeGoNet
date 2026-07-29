namespace AnimeGo.Plugin.Abstractions;

public sealed class PluginPipelineException(string code, string message)
    : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class TitleParserManager(PluginCatalog catalog)
{
    public ITitleParserPlugin Resolve(string? configuredPluginId = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredPluginId))
        {
            return catalog.Find<ITitleParserPlugin>(configuredPluginId)
                ?? throw new PluginPipelineException(
                    "parser_not_registered",
                    $"Parser plugin '{configuredPluginId.Trim().ToLowerInvariant()}' is not registered.");
        }

        var parsers = catalog.GetAll<ITitleParserPlugin>();
        return parsers.Count > 0
            ? parsers[0]
            : throw new PluginPipelineException(
                "parser_not_registered",
                "No parser plugin is registered.");
    }

    public ValueTask<TitleParseResult> ParseAsync(
        TitleParseContext context,
        string? configuredPluginId = null,
        CancellationToken cancellationToken = default) =>
        Resolve(configuredPluginId).ParseAsync(context, cancellationToken);
}

public sealed record OrderedFilterRun(
    string PluginId,
    FilterResult Result);

public sealed record OrderedFilterExecutionResult(
    IReadOnlyList<FilterItem> AcceptedItems,
    IReadOnlyList<OrderedFilterRun> Runs,
    IReadOnlyList<PluginOperationError> Errors)
{
    public bool Succeeded => Errors.Count == 0;
}

public sealed class OrderedFeedFilterManager(PluginCatalog catalog)
{
    public async ValueTask<OrderedFilterExecutionResult> ExecuteAsync(
        FilterContext context,
        IReadOnlyList<string>? configuredPluginIds = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var filters = Resolve(configuredPluginIds);
        IReadOnlyList<FilterItem> current = context.Items.ToArray();
        var runs = new List<OrderedFilterRun>(filters.Count);

        foreach (var filter in filters)
        {
            var result = await filter.FilterAsync(
                context with { Items = current },
                cancellationToken).ConfigureAwait(false);
            runs.Add(new OrderedFilterRun(filter.Descriptor.Id, result));
            if (result.Errors.Count > 0)
            {
                return new OrderedFilterExecutionResult(
                    current,
                    runs,
                    result.Errors.ToArray());
            }

            var validationError = ValidateResult(current, result);
            if (validationError is not null)
            {
                return new OrderedFilterExecutionResult(
                    current,
                    runs,
                    [validationError]);
            }

            var decisionsByIndex = result.Decisions.ToDictionary(
                decision => decision.Index);
            current = current
                .Where(item => decisionsByIndex[item.Index].Accepted)
                .ToArray();
        }

        return new OrderedFilterExecutionResult(current, runs, []);
    }

    private IReadOnlyList<IFeedFilterPlugin> Resolve(
        IReadOnlyList<string>? configuredPluginIds)
    {
        if (configuredPluginIds is null)
        {
            return catalog.GetAll<IFeedFilterPlugin>();
        }

        var filters = new List<IFeedFilterPlugin>(configuredPluginIds.Count);
        var usedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var configuredId in configuredPluginIds)
        {
            if (string.IsNullOrWhiteSpace(configuredId))
            {
                throw new PluginPipelineException(
                    "filter_id_invalid",
                    "Configured filter plugin id cannot be empty.");
            }

            var normalized = configuredId.Trim().ToLowerInvariant();
            if (!usedIds.Add(normalized))
            {
                throw new PluginPipelineException(
                    "filter_id_duplicate",
                    $"Filter plugin '{normalized}' is configured more than once.");
            }

            filters.Add(catalog.Find<IFeedFilterPlugin>(normalized)
                ?? throw new PluginPipelineException(
                    "filter_not_registered",
                    $"Filter plugin '{normalized}' is not registered."));
        }

        return filters;
    }

    private static PluginOperationError? ValidateResult(
        IReadOnlyList<FilterItem> input,
        FilterResult result)
    {
        if (result.Decisions.Count != input.Count)
        {
            return InvalidResult("Filter decision count does not match its input count.");
        }

        var expectedIndexes = input.Select(item => item.Index).ToHashSet();
        var resultIndexes = new HashSet<int>();
        foreach (var decision in result.Decisions)
        {
            if (!expectedIndexes.Contains(decision.Index)
                || !resultIndexes.Add(decision.Index)
                || string.IsNullOrWhiteSpace(decision.Outcome)
                || string.IsNullOrWhiteSpace(decision.Reason)
                || decision.Metadata is null)
            {
                return InvalidResult("Filter decisions contain an invalid or duplicate item index/state.");
            }
        }

        return resultIndexes.SetEquals(expectedIndexes)
            ? null
            : InvalidResult("Filter decisions do not cover the complete input.");
    }

    private static PluginOperationError InvalidResult(string message) =>
        new("filter_result_invalid", message);
}
