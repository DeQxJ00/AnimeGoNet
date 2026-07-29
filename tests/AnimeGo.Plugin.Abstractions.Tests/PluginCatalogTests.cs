using AnimeGo.Plugin.Abstractions;

namespace AnimeGo.Plugin.Abstractions.Tests;

public sealed class PluginCatalogTests
{
    [Fact]
    public void OrdersRegistrationsByConfiguredOrderThenStableId()
    {
        var catalog = new PluginCatalog(
        [
            new SourcePlugin("ttg", 20),
            new SourcePlugin("u2", 20),
            new SourcePlugin("mikan", 10),
        ]);

        Assert.Equal(
            ["mikan", "ttg", "u2"],
            catalog.GetAll<IInputSourceAdapter>().Select(plugin => plugin.Descriptor.Id));
    }

    [Fact]
    public void RejectsDuplicateIdsAcrossCategories()
    {
        var exception = Assert.Throws<ArgumentException>(() => new PluginCatalog(
        [
            new SourcePlugin("builtin", 0),
            new FeedPlugin("builtin", 0),
        ]));

        Assert.Contains("Duplicate plugin id", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsInvalidOrMismatchedDescriptors()
    {
        Assert.Throws<ArgumentException>(() => new PluginCatalog([new SourcePlugin("Mikan", 0)]));
        Assert.Throws<ArgumentException>(() => new PluginCatalog([new SourcePlugin("bad..id", 0)]));
        Assert.Throws<ArgumentException>(() => new PluginCatalog([new MismatchedPlugin()]));
    }

    [Fact]
    public void RequireUsesCaseInsensitiveInputButExactCategory()
    {
        var source = new SourcePlugin("mikan", 0);
        var catalog = new PluginCatalog([source]);

        Assert.Same(source, catalog.Require<IInputSourceAdapter>(" MIKAN "));
        Assert.Null(catalog.Find<IFeedPlugin>("mikan"));
        Assert.Throws<KeyNotFoundException>(() => catalog.Require<IInputSourceAdapter>("unknown"));
        Assert.Throws<KeyNotFoundException>(() => catalog.Require<IFeedPlugin>("mikan"));
    }

    private sealed class SourcePlugin(string id, int order) : IInputSourceAdapter
    {
        public PluginDescriptor Descriptor { get; } =
            new(id, id, "1.0.0", PluginCategory.Source, order);

        public ValueTask<SourceIngestResult> NormalizeAsync(
            SourceIngestContext context,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new SourceIngestResult(null, []));
    }

    private sealed class FeedPlugin(string id, int order) : IFeedPlugin
    {
        public PluginDescriptor Descriptor { get; } =
            new(id, id, "1.0.0", PluginCategory.Feed, order);

        public ValueTask<FeedResult> FetchAsync(
            FeedContext context,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new FeedResult(
                [],
                [],
                new Dictionary<string, string>(StringComparer.Ordinal)));
    }

    private sealed class MismatchedPlugin : IInputSourceAdapter
    {
        public PluginDescriptor Descriptor { get; } =
            new("mismatch", "Mismatch", "1.0.0", PluginCategory.Feed);

        public ValueTask<SourceIngestResult> NormalizeAsync(
            SourceIngestContext context,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new SourceIngestResult(null, []));
    }
}
