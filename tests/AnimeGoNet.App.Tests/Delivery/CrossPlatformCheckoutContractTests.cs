namespace AnimeGoNet.App.Tests.Delivery;

public sealed class CrossPlatformCheckoutContractTests
{
    [Fact]
    public async Task RepositoryTextFilesAreCheckedOutWithLfLineEndings()
    {
        var attributes = await File.ReadAllTextAsync(Path.Combine(
            RepositoryRoot(),
            ".gitattributes"));

        Assert.Contains(
            "* text=auto eol=lf",
            attributes.ReplaceLineEndings("\n"),
            StringComparison.Ordinal);
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));
}
