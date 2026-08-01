using AnimeGo.Plugin.Abstractions;

namespace AnimeGo.Plugin.Abstractions.Tests;

public sealed class PluginManagerTests
{
    [Fact]
    public async Task ParserUsesFirstConfiguredByCatalogOrderWithoutFallback()
    {
        var first = new ParserPlugin("first", 10, matched: false);
        var second = new ParserPlugin("second", 20, matched: true);
        var manager = new TitleParserManager(new PluginCatalog([second, first]));

        var result = await manager.ParseAsync(ParseContext());

        Assert.False(result.Matched);
        Assert.Equal(1, first.CallCount);
        Assert.Equal(0, second.CallCount);
    }

    [Fact]
    public async Task ParserCanSelectAnExplicitRegisteredId()
    {
        var first = new ParserPlugin("first", 10, matched: false);
        var second = new ParserPlugin("second", 20, matched: true);
        var manager = new TitleParserManager(new PluginCatalog([first, second]));

        var result = await manager.ParseAsync(ParseContext(), " SECOND ");

        Assert.True(result.Matched);
        Assert.Equal(0, first.CallCount);
        Assert.Equal(1, second.CallCount);
        Assert.Equal(
            "parser_not_registered",
            Assert.Throws<PluginPipelineException>(() => manager.Resolve("missing")).Code);
    }

    [Fact]
    public async Task FiltersRunInOrderAndOnlyPassAcceptedItemsForward()
    {
        var first = new FilterPlugin("first", 10, rejectedIndexes: [1]);
        var second = new FilterPlugin("second", 20, rejectedIndexes: [2]);
        var manager = new OrderedFeedFilterManager(new PluginCatalog([second, first]));

        var result = await manager.ExecuteAsync(FilterContext());

        Assert.True(result.Succeeded);
        Assert.Equal(["first", "second"], result.Runs.Select(run => run.PluginId));
        Assert.Equal([0, 1, 2], first.ObservedIndexes);
        Assert.Equal([0, 2], second.ObservedIndexes);
        Assert.Equal([0], result.AcceptedItems.Select(item => item.Index));
    }

    [Fact]
    public async Task FilterErrorStopsTheChainAndPreservesPreFailureItems()
    {
        var first = new FilterPlugin("first", 10, rejectedIndexes: [1]);
        var failing = new FilterPlugin("failing", 20, errorCode: "filter_failed");
        var never = new FilterPlugin("never", 30);
        var manager = new OrderedFeedFilterManager(new PluginCatalog([never, failing, first]));

        var result = await manager.ExecuteAsync(FilterContext());

        Assert.False(result.Succeeded);
        Assert.Equal("filter_failed", Assert.Single(result.Errors).Code);
        Assert.Equal(["first", "failing"], result.Runs.Select(run => run.PluginId));
        Assert.Equal([0, 2], result.AcceptedItems.Select(item => item.Index));
        Assert.Equal(0, never.CallCount);
    }

    [Fact]
    public async Task UnexpectedFilterExceptionPropagatesAndStopsTheChain()
    {
        var throwing = new FilterPlugin("throwing", 10, throwException: true);
        var never = new FilterPlugin("never", 20);
        var manager = new OrderedFeedFilterManager(new PluginCatalog([never, throwing]));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await manager.ExecuteAsync(FilterContext()));

        Assert.Equal("filter_exception", exception.Message);
        Assert.Equal(1, throwing.CallCount);
        Assert.Equal(0, never.CallCount);
    }

    [Fact]
    public async Task InvalidFilterResultStopsBeforeTheNextPlugin()
    {
        var invalid = new FilterPlugin("invalid", 10, returnDuplicateIndex: true);
        var never = new FilterPlugin("never", 20);
        var manager = new OrderedFeedFilterManager(new PluginCatalog([never, invalid]));

        var result = await manager.ExecuteAsync(FilterContext());

        Assert.False(result.Succeeded);
        Assert.Equal("filter_result_invalid", Assert.Single(result.Errors).Code);
        Assert.Equal([0, 1, 2], result.AcceptedItems.Select(item => item.Index));
        Assert.Equal(0, never.CallCount);
    }

    [Fact]
    public async Task InvalidOrDuplicateFilterConfigurationIsRejected()
    {
        var plugin = new FilterPlugin("filter", 10);
        var manager = new OrderedFeedFilterManager(new PluginCatalog([plugin]));

        var invalidResult = await manager.ExecuteAsync(
            FilterContext(),
            configuredPluginIds: ["filter"]);
        Assert.True(invalidResult.Succeeded);

        Assert.Equal(
            "filter_id_duplicate",
            (await Assert.ThrowsAsync<PluginPipelineException>(async () =>
                await manager.ExecuteAsync(FilterContext(), ["filter", "FILTER"]))).Code);
        Assert.Equal(
            "filter_not_registered",
            (await Assert.ThrowsAsync<PluginPipelineException>(async () =>
                await manager.ExecuteAsync(FilterContext(), ["missing"]))).Code);
    }

    [Fact]
    public async Task EmptyConfiguredFilterChainLeavesInputUntouched()
    {
        var plugin = new FilterPlugin("filter", 10);
        var manager = new OrderedFeedFilterManager(new PluginCatalog([plugin]));

        var result = await manager.ExecuteAsync(FilterContext(), []);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Runs);
        Assert.Equal([0, 1, 2], result.AcceptedItems.Select(item => item.Index));
        Assert.Equal(0, plugin.CallCount);
    }

    [Fact]
    public async Task ExternalFilterRequiresExplicitOrderedConfiguration()
    {
        var builtIn = new FilterPlugin("builtin", 10);
        var external = new FilterPlugin("external", 20, isBuiltIn: false);
        var manager = new OrderedFeedFilterManager(
            new PluginCatalog([external, builtIn]));

        var defaults = await manager.ExecuteAsync(FilterContext());
        var configured = await manager.ExecuteAsync(FilterContext(), ["external"]);

        Assert.Equal(["builtin"], defaults.Runs.Select(run => run.PluginId));
        Assert.Equal(["external"], configured.Runs.Select(run => run.PluginId));
        Assert.Equal(1, builtIn.CallCount);
        Assert.Equal(1, external.CallCount);
    }

    private static TitleParseContext ParseContext() =>
        new(
            "Show [01]",
            null,
            "mikan",
            new Dictionary<string, string>(StringComparer.Ordinal));

    private static FilterContext FilterContext() =>
        new(
            "mikan",
            [
                Item(0),
                Item(1),
                Item(2),
            ],
            new Dictionary<string, string>(StringComparer.Ordinal));

    private static FilterItem Item(int index) =>
        new(
            index,
            $"Show [{index + 1:00}]",
            $"https://tracker.invalid/{index}.torrent",
            null,
            null,
            "3951",
            "application/x-bittorrent",
            42,
            null);

    private sealed class ParserPlugin(
        string id,
        int order,
        bool matched) : ITitleParserPlugin
    {
        public PluginDescriptor Descriptor { get; } =
            new(id, id, "1.0.0", PluginCategory.Parser, order);

        public int CallCount { get; private set; }

        public ValueTask<TitleParseResult> ParseAsync(
            TitleParseContext context,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return ValueTask.FromResult(new TitleParseResult(
                matched,
                null,
                null,
                matched ? 1 : null,
                matched ? "normal" : "unknown",
                matched ? "1" : null,
                null,
                null,
                matched ? [] : [new PluginOperationError("no_match", "No match.")]));
        }
    }

    private sealed class FilterPlugin(
        string id,
        int order,
        IReadOnlyList<int>? rejectedIndexes = null,
        string? errorCode = null,
        bool returnDuplicateIndex = false,
        bool isBuiltIn = true,
        bool throwException = false) : IFeedFilterPlugin
    {
        private readonly HashSet<int> rejected = (rejectedIndexes ?? []).ToHashSet();

        public PluginDescriptor Descriptor { get; } =
            new(id, id, "1.0.0", PluginCategory.Filter, order, isBuiltIn);

        public int CallCount { get; private set; }

        public IReadOnlyList<int> ObservedIndexes { get; private set; } = [];

        public ValueTask<FilterResult> FilterAsync(
            FilterContext context,
            CancellationToken cancellationToken)
        {
            CallCount++;
            ObservedIndexes = context.Items.Select(item => item.Index).ToArray();
            if (throwException)
            {
                throw new InvalidOperationException("filter_exception");
            }

            if (errorCode is not null)
            {
                return ValueTask.FromResult(new FilterResult(
                    [],
                    [new PluginOperationError(errorCode, errorCode)],
                    new Dictionary<string, string>(StringComparer.Ordinal)));
            }

            var decisions = context.Items.Select(item =>
                {
                    var accepted = !rejected.Contains(item.Index);
                    return new FilterDecision(
                        returnDuplicateIndex ? context.Items[0].Index : item.Index,
                        accepted ? "Accepted" : "Rejected",
                        accepted,
                        accepted ? "accepted" : "rejected",
                        0,
                        new Dictionary<string, string?>(StringComparer.Ordinal));
                }).ToArray();
            return ValueTask.FromResult(new FilterResult(
                decisions,
                [],
                new Dictionary<string, string>(StringComparer.Ordinal)));
        }
    }
}
