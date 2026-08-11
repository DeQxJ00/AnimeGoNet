using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AnimeGoNet.App.Tests.Delivery;

public sealed class NativeRunnerAssertionTests
{
    [Fact]
    public async Task ScriptAcceptsTheActualPlatformAndRejectsAnotherRid()
    {
        var actualRid = CurrentRid();
        var mismatchedRid = actualRid == "linux-x64" ? "win-x64" : "linux-x64";

        var accepted = await RunAsync(actualRid);
        Assert.Equal(0, accepted.ExitCode);
        Assert.Contains($"Native runner verified: {actualRid}", accepted.StandardOutput, StringComparison.Ordinal);

        var rejected = await RunAsync(mismatchedRid);
        Assert.NotEqual(0, rejected.ExitCode);
        Assert.Contains("Native runner mismatch", rejected.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkflowAssertsEveryMatrixEntryBeforeSetup()
    {
        var root = RepositoryRoot();
        var workflow = await File.ReadAllTextAsync(Path.Combine(
            root,
            ".github",
            "workflows",
            "animegonet-native-aot.yml"));

        Assert.Contains("runner: windows-11-arm", workflow, StringComparison.Ordinal);
        Assert.Contains("runner: ubuntu-24.04-arm", workflow, StringComparison.Ordinal);
        Assert.Contains("runner: macos-15", workflow, StringComparison.Ordinal);
        var assertion = workflow.IndexOf(
            "./eng/assert-native-runner.ps1 -RuntimeIdentifier \"${{ matrix.rid }}\"",
            StringComparison.Ordinal);
        var setup = workflow.IndexOf("actions/setup-dotnet@v5", StringComparison.Ordinal);
        Assert.True(assertion >= 0 && assertion < setup);
    }

    private static string CurrentRid()
    {
        var os = OperatingSystem.IsWindows()
            ? "win"
            : OperatingSystem.IsLinux()
                ? "linux"
                : OperatingSystem.IsMacOS()
                    ? "osx"
                    : throw new PlatformNotSupportedException();
        var architecture = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => throw new PlatformNotSupportedException(),
        };
        var rid = $"{os}-{architecture}";
        return rid == "osx-x64"
            ? throw new PlatformNotSupportedException("osx-x64 is outside the release matrix.")
            : rid;
    }

    private static async Task<ProcessResult> RunAsync(string rid)
    {
        var root = RepositoryRoot();
        var start = new ProcessStartInfo
        {
            FileName = "pwsh",
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("-NoLogo");
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(Path.Combine(root, "eng", "assert-native-runner.ps1"));
        start.ArgumentList.Add("-RuntimeIdentifier");
        start.ArgumentList.Add(rid);

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Failed to start pwsh.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResult(process.ExitCode, await output, await error);
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
