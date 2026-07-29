using AnimeGo.Plugin.Abstractions;
using AnimeGoNet.Core.Ingest;
using AnimeGoNet.Core.Plugins;

namespace AnimeGoNet.Core.Tests.Plugins;

public sealed class BuiltInPluginCatalogTests
{
    [Fact]
    public void RegistersEverySupportedSourceExplicitlyAndInStableOrder()
    {
        var catalog = BuiltInPluginCatalog.Create();

        Assert.Equal(
            ["mikan", "u2", "ttg"],
            catalog.GetAll<IInputSourceAdapter>().Select(plugin => plugin.Descriptor.Id));
        Assert.All(catalog.All, plugin => Assert.True(plugin.Descriptor.IsBuiltIn));
    }

    [Fact]
    public async Task RealNormalizationResolvesTheRegisteredAdapter()
    {
        var result = await IngestCommandNormalizer.NormalizeAsync(
            BuiltInPluginCatalog.Create(),
            " MIKAN ",
            new IngestItemCommand(
                "https://tracker.invalid/passkey/test.torrent",
                new IngestItemInfo(
                    "Episode 01",
                    null,
                    "item-1",
                    null,
                    "https://mikanani.me/Home/Bangumi/3951",
                    null,
                    null,
                    547888,
                    null,
                    null)));

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        Assert.Equal("mikan", result.Item!.Source);
        Assert.Equal(3951, result.Item.MikanId);
    }

    [Fact]
    public async Task RejectsUnknownAdapterBeforeExecutingBusinessRules()
    {
        var result = await IngestCommandNormalizer.NormalizeAsync(
            BuiltInPluginCatalog.Create(),
            "python/custom.py",
            new IngestItemCommand(
                "https://tracker.invalid/test.torrent",
                new IngestItemInfo("Episode 01", null, null, null, null, null, null, null, null, null)));

        Assert.False(result.IsValid);
        Assert.Equal(
            "source adapter 'python/custom.py' is not registered",
            Assert.Single(result.Errors));
    }

    [Fact]
    public async Task HostRejectsInvalidOutputFromRegisteredAdapter()
    {
        var result = await IngestCommandNormalizer.NormalizeAsync(
            new PluginCatalog([new InvalidOutputSourceAdapter()]),
            "invalid",
            new IngestItemCommand(
                "https://tracker.invalid/test.torrent",
                new IngestItemInfo("Episode 01", null, null, null, null, null, null, null, null, null)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("different source", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("invalid torrent URL", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("empty title", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("fingerprint", StringComparison.Ordinal));
    }

    private sealed class InvalidOutputSourceAdapter : IInputSourceAdapter
    {
        public PluginDescriptor Descriptor { get; } =
            new("invalid", "Invalid test adapter", "1.0.0", PluginCategory.Source);

        public ValueTask<SourceIngestResult> NormalizeAsync(
            SourceIngestContext context,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new SourceIngestResult(
                new SourceNormalizedItem(
                    "other",
                    "file:///unsafe.torrent",
                    "BAD",
                    " ",
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null),
                []));
    }
}
