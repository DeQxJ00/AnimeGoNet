using YamlDotNet.RepresentationModel;

namespace AnimeGoNet.App.Tests.Delivery;

public sealed class UpstreamGoBaselineDeliveryContractTests
{
    private const string UpstreamCommit =
        "c7475dfc55a374cd0dd08821bf17125dab1e3145";

    [Fact]
    public async Task WorkflowPinsLinuxContainerCommitSerialExecutionAndAlwaysUploadsReport()
    {
        var root = RepositoryRoot();
        var workflow = await File.ReadAllTextAsync(Path.Combine(
            root,
            ".github",
            "workflows",
            "upstream-go-baseline.yml"));
        var parsed = new YamlStream();
        parsed.Load(new StringReader(workflow));

        Assert.Single(parsed.Documents);
        Assert.Contains("container:", workflow, StringComparison.Ordinal);
        Assert.Contains("image: golang:1.22.10-bookworm", workflow, StringComparison.Ordinal);
        Assert.Contains("repository: wetor/AnimeGo", workflow, StringComparison.Ordinal);
        Assert.Contains($"ref: {UpstreamCommit}", workflow, StringComparison.Ordinal);
        Assert.Contains("persist-credentials: false", workflow, StringComparison.Ordinal);
        Assert.Contains("bash ./eng/capture-upstream-go-baseline.sh", workflow, StringComparison.Ordinal);
        Assert.Contains("if: always()", workflow, StringComparison.Ordinal);
        Assert.Contains("actions/upload-artifact@v7", workflow, StringComparison.Ordinal);
        Assert.Contains("if-no-files-found: error", workflow, StringComparison.Ordinal);
        Assert.Contains("retention-days: 30", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("TestSpace", workflow, StringComparison.OrdinalIgnoreCase);
    }

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
