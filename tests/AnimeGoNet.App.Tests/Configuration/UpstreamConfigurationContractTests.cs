using System.Diagnostics;
using System.Text.RegularExpressions;

namespace AnimeGoNet.App.Tests.Configuration;

public sealed partial class UpstreamConfigurationContractTests
{
    private const string UpstreamCommit =
        "c7475dfc55a374cd0dd08821bf17125dab1e3145";

    private static readonly string[] AllowedDispositions =
        ["preserved", "replaced", "excluded"];

    [Fact]
    public void ManifestsHaveValidDispositionsAndExistingTargets()
    {
        var root = RepositoryRoot();
        var contracts = ParseContracts(File.ReadAllText(Path.Combine(
            root,
            "docs",
            "baseline",
            "UPSTREAM_CONFIGURATION_CONTRACTS.psv")));
        Assert.NotEmpty(contracts);
        Assert.Equal(
            contracts.Count,
            contracts.Select(entry => entry.UpstreamFile)
                .Distinct(StringComparer.Ordinal).Count());
        foreach (var entry in contracts)
        {
            Assert.Contains(entry.Disposition, AllowedDispositions);
            Assert.NotEmpty(entry.Symbols);
            Assert.NotEmpty(entry.Reason);
            foreach (var target in entry.Targets)
            {
                Assert.True(
                    File.Exists(Path.Combine(root, ToPlatformPath(target))),
                    $"Mapped configuration target does not exist: {target}");
            }
        }

        var tests = ParseTestMappings(File.ReadAllText(Path.Combine(
            root,
            "docs",
            "baseline",
            "UPSTREAM_CONFIGURATION_TESTS.psv")));
        Assert.NotEmpty(tests);
        Assert.Equal(
            tests.Count,
            tests.Select(entry => entry.UpstreamSymbol)
                .Distinct(StringComparer.Ordinal).Count());
        foreach (var entry in tests)
        {
            Assert.Contains(entry.Disposition, AllowedDispositions);
            Assert.NotEmpty(entry.Reason);
            var targetPath = Path.Combine(root, ToPlatformPath(entry.TargetFile));
            Assert.True(File.Exists(targetPath), $"Mapped parity test does not exist: {entry.TargetFile}");
            Assert.Matches(
                $@"\b{Regex.Escape(entry.TargetMethod)}\s*\(",
                File.ReadAllText(targetPath));
        }
    }

    [Fact]
    public async Task PinnedUpstreamConfigurationFilesAndSymbolsAreExhaustivelyMapped()
    {
        var upstream = Environment.GetEnvironmentVariable("ANIMEGO_UPSTREAM_REPO");
        if (string.IsNullOrWhiteSpace(upstream))
        {
            return;
        }

        var upstreamRoot = Path.GetFullPath(upstream);
        Assert.Equal(UpstreamCommit, await ReadGitHeadAsync(upstreamRoot));
        var manifest = ParseContracts(File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "docs",
            "baseline",
            "UPSTREAM_CONFIGURATION_CONTRACTS.psv")))
            .ToDictionary(entry => entry.UpstreamFile, StringComparer.Ordinal);
        var configRoot = Path.Combine(upstreamRoot, "configs");
        var sourceFiles = Directory.EnumerateFiles(
                configRoot,
                "*.go",
                SearchOption.AllDirectories)
            .Where(path => !path.EndsWith("_test.go", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(upstreamRoot, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(sourceFiles, manifest.Keys.Order(StringComparer.Ordinal).ToArray());
        foreach (var sourceFile in sourceFiles)
        {
            var actual = ExportedSymbols(File.ReadAllText(Path.Combine(
                upstreamRoot,
                ToPlatformPath(sourceFile))));
            Assert.Equal(
                actual,
                manifest[sourceFile].Symbols.Order(StringComparer.Ordinal).ToArray());
        }
    }

    [Fact]
    public async Task EveryPinnedUpstreamConfigurationTestHasAnExplicitReplacement()
    {
        var upstream = Environment.GetEnvironmentVariable("ANIMEGO_UPSTREAM_REPO");
        if (string.IsNullOrWhiteSpace(upstream))
        {
            return;
        }

        var upstreamRoot = Path.GetFullPath(upstream);
        Assert.Equal(UpstreamCommit, await ReadGitHeadAsync(upstreamRoot));
        var actual = ExportedFunctionRegex()
            .Matches(File.ReadAllText(Path.Combine(upstreamRoot, "configs", "config_test.go")))
            .Select(match => match.Groups[1].Value)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var mapped = ParseTestMappings(File.ReadAllText(Path.Combine(
                RepositoryRoot(),
                "docs",
                "baseline",
                "UPSTREAM_CONFIGURATION_TESTS.psv")))
            .Select(entry => entry.UpstreamSymbol)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(actual, mapped);
    }

    private static string[] ExportedSymbols(string source)
    {
        var symbols = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in ExportedTypeRegex().Matches(source))
        {
            symbols.Add(match.Groups[1].Value);
        }
        foreach (Match match in ExportedFunctionRegex().Matches(source))
        {
            symbols.Add($"func:{match.Groups[1].Value}");
        }
        foreach (Match match in ExportedDirectValueRegex().Matches(source))
        {
            symbols.Add($"{match.Groups[1].Value}:{match.Groups[2].Value}");
        }
        foreach (Match block in ValueBlockRegex().Matches(source))
        {
            var kind = block.Groups[1].Value;
            var braceDepth = 0;
            foreach (var line in block.Groups[2].Value.Split('\n'))
            {
                if (braceDepth == 0)
                {
                    var value = ExportedBlockValueRegex().Match(line);
                    if (value.Success)
                    {
                        symbols.Add($"{kind}:{value.Groups[1].Value}");
                    }
                }

                braceDepth += line.Count(character => character == '{');
                braceDepth -= line.Count(character => character == '}');
            }
        }
        return symbols.Order(StringComparer.Ordinal).ToArray();
    }

    private static List<ContractEntry> ParseContracts(string text) =>
        ParseRows(text, 5).Select(fields => new ContractEntry(
            fields[0],
            fields[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            fields[2],
            fields[3].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            fields[4])).ToList();

    private static List<TestMapping> ParseTestMappings(string text) =>
        ParseRows(text, 5).Select(fields => new TestMapping(
            fields[0], fields[1], fields[2], fields[3], fields[4])).ToList();

    private static IEnumerable<string[]> ParseRows(string text, int fieldCount)
    {
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
            Assert.True(fields.Length == fieldCount, $"Invalid manifest row {lineNumber}.");
            yield return fields;
        }
    }

    private static async Task<string> ReadGitHeadAsync(string repository)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = repository,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };
        process.StartInfo.ArgumentList.Add("rev-parse");
        process.StartInfo.ArgumentList.Add("HEAD");
        Assert.True(process.Start());
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, error);
        return output.Trim();
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static string ToPlatformPath(string path) =>
        path.Replace('/', Path.DirectorySeparatorChar);

    [GeneratedRegex(@"(?m)^type\s+([A-Z][A-Za-z0-9_]*)\b", RegexOptions.CultureInvariant)]
    private static partial Regex ExportedTypeRegex();

    [GeneratedRegex(@"(?m)^func\s+(?:\([^\r\n)]*\)\s*)?([A-Z][A-Za-z0-9_]*)\s*\(", RegexOptions.CultureInvariant)]
    private static partial Regex ExportedFunctionRegex();

    [GeneratedRegex(@"(?m)^(var|const)\s+([A-Z][A-Za-z0-9_]*)\b", RegexOptions.CultureInvariant)]
    private static partial Regex ExportedDirectValueRegex();

    [GeneratedRegex(@"(?ms)^(var|const)\s*\(\s*(.*?)^\)", RegexOptions.CultureInvariant)]
    private static partial Regex ValueBlockRegex();

    [GeneratedRegex(@"(?m)^\s*([A-Z][A-Za-z0-9_]*)\b", RegexOptions.CultureInvariant)]
    private static partial Regex ExportedBlockValueRegex();

    private sealed record ContractEntry(
        string UpstreamFile,
        IReadOnlyList<string> Symbols,
        string Disposition,
        IReadOnlyList<string> Targets,
        string Reason);

    private sealed record TestMapping(
        string UpstreamSymbol,
        string Disposition,
        string TargetFile,
        string TargetMethod,
        string Reason);
}
