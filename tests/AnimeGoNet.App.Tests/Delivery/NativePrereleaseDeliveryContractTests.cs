using System.Xml.Linq;
using YamlDotNet.RepresentationModel;

namespace AnimeGoNet.App.Tests.Delivery;

public sealed class NativeReleaseDeliveryContractTests
{
    [Fact]
    public void TagReleaseWaitsForEveryRidAndPublishesOnlyVerifiedPackages()
    {
        var root = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));
        var workflow = File.ReadAllText(Path.Combine(
            root,
            ".github",
            "workflows",
            "animegonet-native-aot.yml"));
        var parsed = new YamlStream();
        parsed.Load(new StringReader(workflow));
        Assert.Single(parsed.Documents);

        Assert.Contains("release:", workflow, StringComparison.Ordinal);
        Assert.Contains("if: startsWith(github.ref, 'refs/tags/v')", workflow, StringComparison.Ordinal);
        Assert.Contains("needs: publish", workflow, StringComparison.Ordinal);
        Assert.Contains("contents: write", workflow, StringComparison.Ordinal);
        Assert.Contains("actions/download-artifact@v8", workflow, StringComparison.Ordinal);
        Assert.Contains("pattern: animegonet-*", workflow, StringComparison.Ordinal);
        Assert.Contains("vMAJOR.MINOR.PATCH or vMAJOR.MINOR.PATCH-SUFFIX", workflow, StringComparison.Ordinal);
        Assert.Contains("Release tag version must match Directory.Build.props", workflow, StringComparison.Ordinal);
        Assert.Contains("@('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-arm64')", workflow, StringComparison.Ordinal);
        Assert.Contains("./eng/package-native-release.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("$archives.Count -ne $rids.Count", workflow, StringComparison.Ordinal);
        Assert.Contains("gh release create", workflow, StringComparison.Ordinal);
        Assert.Contains("--verify-tag", workflow, StringComparison.Ordinal);
        Assert.Contains("if [[ \"${GITHUB_REF_NAME}\" == *-* ]]", workflow, StringComparison.Ordinal);
        Assert.Contains("release_args+=(--prerelease --latest=false)", workflow, StringComparison.Ordinal);

        var packageIndex = workflow.IndexOf("./eng/package-native-release.ps1", StringComparison.Ordinal);
        var releaseIndex = workflow.IndexOf("gh release create", StringComparison.Ordinal);
        Assert.True(packageIndex >= 0 && releaseIndex > packageIndex);
        Assert.DoesNotContain("--clobber", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectVersionIsAStableSemanticVersion()
    {
        var root = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));
        var props = XDocument.Load(Path.Combine(root, "Directory.Build.props"));
        var version = Assert.Single(props.Descendants("Version")).Value;

        Assert.Matches(@"^[0-9]+\.[0-9]+\.[0-9]+$", version);
        Assert.True(Version.TryParse(version, out var parsed));
        Assert.NotNull(parsed);
        Assert.Equal(version, parsed.ToString(3));
    }
}
