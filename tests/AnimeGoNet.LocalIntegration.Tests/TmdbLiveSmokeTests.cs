using AnimeGoNet.App.Metadata;
using AnimeGoNet.Core.Configuration;

namespace AnimeGoNet.LocalIntegration.Tests;

public sealed class TmdbLiveSmokeTests
{
    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task ConfiguredApiKeyBaseUrlAndOptionalProxyCanReadKnownSeries()
    {
        Assert.Equal("1", Required("ANIMEGONET_TMDB_INTEGRATION"));
        var apiKey = Required("ANIMEGONET_TMDB_API_KEY");
        var baseUrl = new Uri(
            Environment.GetEnvironmentVariable("ANIMEGONET_TMDB_BASE_URL")
            ?? "https://api.themoviedb.org/");
        var proxyValue = Environment.GetEnvironmentVariable("ANIMEGONET_OUTBOUND_PROXY_URL");
        var proxyUrl = string.IsNullOrWhiteSpace(proxyValue) ? null : new Uri(proxyValue);
        var outboundProxy = new OutboundProxyOptions
        {
            Url = proxyUrl,
            HostPatterns = proxyUrl is null ? [] : [baseUrl.IdnHost.ToLowerInvariant()],
        };
        var options = new TmdbClientOptions
        {
            BaseUrl = baseUrl,
            ApiKey = apiKey,
            Language = "zh-CN",
            HttpTimeout = TimeSpan.FromSeconds(30),
        };
        var errors = AnimeGoOptionsValidator.Validate(
            AnimeGoDefaults.CreateNative(Path.GetTempPath()) with
            {
                OutboundProxy = outboundProxy,
                Metadata = AnimeGoDefaults.CreateNative(Path.GetTempPath()).Metadata with
                {
                    Tmdb = options,
                },
            });
        Assert.Empty(errors);

        using var client = new TmdbClient(
            MetadataHttpClientFactory.Create(outboundProxy),
            options,
            ownsHttpClient: true);
        var details = await client.GetSeriesDetailsAsync(72517);

        Assert.NotNull(details);
        Assert.Equal(72517, details.Series.Id);
        Assert.False(string.IsNullOrWhiteSpace(details.Series.Name));
        Assert.Contains(details.Seasons, season => season.SeasonNumber == 1);
    }

    private static string Required(string name) =>
        Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException(
            $"Set {name} before running the explicit TMDB local integration test.");
}
