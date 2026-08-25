namespace AnimeGoNet.App.Tests.Delivery;

public sealed class UpstreamGoBaselineDeliveryContractTests
{
    private const string UpstreamCommit =
        "c7475dfc55a374cd0dd08821bf17125dab1e3145";

    [Fact]
    public async Task CaptureScriptFailsClosedAndWritesStableHashedJsonReport()
    {
        var root = RepositoryRoot();
        var script = await File.ReadAllTextAsync(Path.Combine(
            root,
            "eng",
            "capture-upstream-go-baseline.sh"));

        Assert.StartsWith("#!/usr/bin/env bash\nset -euo pipefail", script, StringComparison.Ordinal);
        Assert.Contains($"expected_commit=\"{UpstreamCommit}\"", script, StringComparison.Ordinal);
        Assert.Contains("CGO_ENABLED=0 GOOS=linux GOARCH=amd64", script, StringComparison.Ordinal);
        Assert.Contains("go test -p 1 -count=1 -json ./...", script, StringComparison.Ordinal);
        Assert.Contains("events.jsonl", script, StringComparison.Ordinal);
        Assert.Contains("stderr.log", script, StringComparison.Ordinal);
        Assert.Contains("summary.json", script, StringComparison.Ordinal);
        Assert.Contains("SHA256SUMS", script, StringComparison.Ordinal);
        Assert.Contains("wrong_commit", script, StringComparison.Ordinal);
        Assert.Contains("exit \"$test_exit_code\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("curl ", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TestSpace", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("passkey", script, StringComparison.OrdinalIgnoreCase);
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
