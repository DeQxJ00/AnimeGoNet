using AnimeGoNet.App.Configuration;
using AnimeGoNet.Core.Configuration;
using Microsoft.Extensions.Configuration;

namespace AnimeGoNet.App.Tests.Configuration;

public sealed class SourceProfileDeploymentLocksTests
{
    [Fact]
    public void LegacyAndCanonicalKeysLockOnlyTheirSourceFields()
    {
        var locks = SourceProfileDeploymentLocks.FromSources(
            ["ANIMEGO_TAG", "sources__mikan__category", "unrelated"],
            ["--ANIMEGO_MIKAN_COOKIE=private", "--sources:u2:category=U2"]);

        Assert.True(locks.IsLocked("mikan", "dynamic_tag_template"));
        Assert.True(locks.IsLocked("mikan", "category"));
        Assert.True(locks.IsLocked("mikan", "mikan_identity_cookie"));
        Assert.True(locks.IsLocked("u2", "category"));
        Assert.False(locks.IsLocked("u2", "dynamic_tag_template"));
        Assert.Equal(3, locks.ForSource("mikan").Count);
        Assert.Equal(
            ["ANIMEGO_TAG"],
            Assert.Single(
                locks.ForSource("mikan"),
                item => item.Field == "dynamic_tag_template")
                .ControllingKeys);
        Assert.DoesNotContain(
            locks.Items.SelectMany(item => item.ControllingKeys),
            key => key.Contains("private", StringComparison.Ordinal));
    }

    [Fact]
    public void LegacyTagOverridesCanonicalYamlAndExplicitEmptyClearsIt()
    {
        var configured = new ConfigurationManager();
        configured.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ANIMEGO_TAG"] = "{year}-environment",
            ["sources:mikan:downloader_id"] = "bt",
            ["sources:mikan:dynamic_tag_template"] = "{year}-yaml",
        });
        var cleared = new ConfigurationManager();
        cleared.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ANIMEGO_TAG"] = string.Empty,
            ["sources:mikan:downloader_id"] = "bt",
            ["sources:mikan:dynamic_tag_template"] = "{year}-yaml",
        });

        var configuredOptions = AnimeGoApplication.LoadOptions(configured, inContainer: false);
        var clearedOptions = AnimeGoApplication.LoadOptions(cleared, inContainer: false);

        Assert.Equal(
            "{year}-environment",
            Assert.Single(configuredOptions.InitialSourceProfiles).DynamicTagTemplate);
        Assert.Null(Assert.Single(clearedOptions.InitialSourceProfiles).DynamicTagTemplate);
    }

    [Fact]
    public void OverrideProjectionContainsValuesButNeverFormatsCookie()
    {
        var locks = SourceProfileDeploymentLocks.FromSources(
            ["ANIMEGO_CATEGORY", "ANIMEGO_TAG", "ANIMEGO_MIKAN_COOKIE"],
            []);
        var seed = AnimeGoDefaults.CreateDocker().InitialSourceProfiles[0] with
        {
            Category = "environment-category",
            DynamicTagTemplate = "{year}-environment",
            MikanIdentityCookie = "environment-private-cookie",
        };

        var value = Assert.IsType<AnimeGoNet.Data.Sources.SourceProfileDeploymentOverride>(
            locks.CreateOverride(seed));

        Assert.True(value.OverrideCategory);
        Assert.True(value.OverrideDynamicTagTemplate);
        Assert.True(value.OverrideMikanIdentityCookie);
        Assert.DoesNotContain("environment-private-cookie", value.ToString(), StringComparison.Ordinal);
    }
}
