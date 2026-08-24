using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace AnimeGoNet.Core.Tests.Compatibility;

public sealed partial class UpstreamPluginFixtureContractTests
{
    private const string UpstreamCommit =
        "c7475dfc55a374cd0dd08821bf17125dab1e3145";

    private static readonly string[] AllowedDispositions =
        ["ported", "replaced", "removed", "documentation"];

    private static readonly string[] UpstreamPathSpecs =
    [
        "assets/plugin/**",
        "test/testdata/feed/**",
        "test/testdata/filter/**",
        "test/testdata/parser/**",
        "test/testdata/python/**",
        "internal/animego/feed/*_test.go",
        "internal/animego/filter/*_test.go",
        "internal/animego/parser/*_test.go",
    ];

    [Fact]
    public void ManifestHasValidDispositionsAndExistingEvidenceTargets()
    {
        var root = RepositoryRoot();
        var entries = ReadManifest(root);

        Assert.NotEmpty(entries);
        Assert.Equal(
            entries.Count,
            entries.Select(entry => entry.UpstreamFile).Distinct(StringComparer.Ordinal).Count());
        foreach (var entry in entries)
        {
            Assert.Contains(entry.Disposition, AllowedDispositions);
            Assert.NotEmpty(entry.Reason);
            Assert.NotEmpty(entry.Targets);
            foreach (var target in entry.Targets)
            {
                Assert.True(
                    target.StartsWith("tests/", StringComparison.Ordinal)
                    || target.StartsWith("docs/", StringComparison.Ordinal),
                    $"Evidence target must be a test or document: {target}");
                Assert.True(
                    File.Exists(Path.Combine(root, target.Replace('/', Path.DirectorySeparatorChar))),
                    $"Mapped evidence target does not exist: {target}");
            }
        }
    }

    [Fact]
    public async Task PinnedPluginParserAndFilterSurfaceIsExhaustivelyMappedAndHashed()
    {
        var upstream = Environment.GetEnvironmentVariable("ANIMEGO_UPSTREAM_REPO");
        if (string.IsNullOrWhiteSpace(upstream))
        {
            return;
        }

        var upstreamRoot = Path.GetFullPath(upstream);
        Assert.Equal(UpstreamCommit, await ReadGitHeadAsync(upstreamRoot));

        var root = RepositoryRoot();
        var entries = ReadManifest(root);
        var trackedFiles = await ReadTrackedFilesAsync(upstreamRoot);
        Assert.Equal(
            trackedFiles,
            entries.Select(entry => entry.UpstreamFile).Order(StringComparer.Ordinal).ToArray());

        var fixtureHashes = ReadFixtureHashes(root);
        foreach (var entry in entries.Where(entry => !entry.UpstreamFile.StartsWith("internal/", StringComparison.Ordinal)))
        {
            Assert.True(
                fixtureHashes.TryGetValue(entry.UpstreamFile, out var expectedHash),
                $"Missing fixture SHA-256 baseline: {entry.UpstreamFile}");
            var path = Path.Combine(upstreamRoot, entry.UpstreamFile.Replace('/', Path.DirectorySeparatorChar));
            var actualHash = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(path)));
            Assert.Equal(expectedHash, actualHash);
        }
    }

    private static List<FixtureContract> ReadManifest(string root)
    {
        var path = Path.Combine(root, "docs", "baseline", "UPSTREAM_PLUGIN_FIXTURE_CONTRACTS.psv");
        var text = File.ReadAllText(path);
        Assert.StartsWith($"# AnimeGo develop@{UpstreamCommit}", text, StringComparison.Ordinal);

        var entries = new List<FixtureContract>();
        var lineNumber = 0;
        foreach (var line in text.Split('\n'))
        {
            lineNumber++;
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            var fields = trimmed.Split('|');
            Assert.True(fields.Length == 4, $"Invalid fixture contract row {lineNumber}.");
            entries.Add(new FixtureContract(
                fields[0],
                fields[1],
                fields[2].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                fields[3]));
        }

        return entries;
    }

    private static Dictionary<string, string> ReadFixtureHashes(string root)
    {
        var text = File.ReadAllText(Path.Combine(root, "docs", "baseline", "FIXTURES.sha256.md"));
        return HashLineRegex()
            .Matches(text.ReplaceLineEndings("\n"))
            .ToDictionary(
                match => match.Groups[2].Value,
                match => match.Groups[1].Value,
                StringComparer.Ordinal);
    }

    private static async Task<string[]> ReadTrackedFilesAsync(string repository)
    {
        using var process = CreateGitProcess(repository);
        process.StartInfo.ArgumentList.Add("ls-files");
        process.StartInfo.ArgumentList.Add("-z");
        foreach (var pathSpec in UpstreamPathSpecs)
        {
            process.StartInfo.ArgumentList.Add(pathSpec);
        }

        Assert.True(process.Start());
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, error);
        return output
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static async Task<string> ReadGitHeadAsync(string repository)
    {
        using var process = CreateGitProcess(repository);
        process.StartInfo.ArgumentList.Add("rev-parse");
        process.StartInfo.ArgumentList.Add("HEAD");
        Assert.True(process.Start());
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, error);
        return output.Trim();
    }

    private static Process CreateGitProcess(string repository) =>
        new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = repository,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                UseShellExecute = false,
            },
        };

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));

    [GeneratedRegex(@"(?m)^([0-9a-f]{64})  (.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex HashLineRegex();

    private sealed record FixtureContract(
        string UpstreamFile,
        string Disposition,
        IReadOnlyList<string> Targets,
        string Reason);
}
