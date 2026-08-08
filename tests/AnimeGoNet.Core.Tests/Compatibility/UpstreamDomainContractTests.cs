using System.Diagnostics;
using System.Text.RegularExpressions;

namespace AnimeGoNet.Core.Tests.Compatibility;

public sealed partial class UpstreamDomainContractTests
{
    private const string UpstreamCommit =
        "c7475dfc55a374cd0dd08821bf17125dab1e3145";

    private static readonly string[] AllowedDispositions =
        ["preserved", "replaced", "excluded"];

    private static readonly string[] UpstreamDirectories =
    [
        "internal/models",
        "internal/constant",
        "internal/exceptions",
        "pkg/exceptions",
    ];

    [Fact]
    public void ManifestHasValidDispositionAndExistingAotSafeTargets()
    {
        var root = RepositoryRoot();
        var manifestPath = Path.Combine(
            root,
            "docs",
            "baseline",
            "UPSTREAM_DOMAIN_CONTRACTS.psv");
        var text = File.ReadAllText(manifestPath);

        Assert.StartsWith($"# AnimeGo develop@{UpstreamCommit}", text, StringComparison.Ordinal);
        var entries = ParseManifest(text);
        Assert.NotEmpty(entries);
        Assert.Equal(entries.Count, entries.Select(entry => entry.UpstreamFile).Distinct(StringComparer.Ordinal).Count());

        foreach (var entry in entries)
        {
            Assert.Contains(entry.Disposition, AllowedDispositions);
            Assert.NotEmpty(entry.Reason);
            Assert.NotEmpty(entry.Targets);
            foreach (var target in entry.Targets)
            {
                Assert.StartsWith("src/", target, StringComparison.Ordinal);
                Assert.True(
                    File.Exists(Path.Combine(root, target.Replace('/', Path.DirectorySeparatorChar))),
                    $"Mapped target does not exist: {target}");
            }
        }
    }

    [Fact]
    public async Task PinnedUpstreamFilesAndExportedTypesAreExhaustivelyMapped()
    {
        var upstream = Environment.GetEnvironmentVariable("ANIMEGO_UPSTREAM_REPO");
        if (string.IsNullOrWhiteSpace(upstream))
        {
            return;
        }

        var upstreamRoot = Path.GetFullPath(upstream);
        Assert.Equal(UpstreamCommit, await ReadGitHeadAsync(upstreamRoot));

        var manifest = ParseManifest(File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "docs",
            "baseline",
            "UPSTREAM_DOMAIN_CONTRACTS.psv")));
        var mappedByFile = manifest.ToDictionary(entry => entry.UpstreamFile, StringComparer.Ordinal);
        var sourceFiles = UpstreamDirectories
            .SelectMany(directory => Directory.EnumerateFiles(
                Path.Combine(upstreamRoot, directory.Replace('/', Path.DirectorySeparatorChar)),
                "*.go",
                SearchOption.TopDirectoryOnly))
            .Select(path => Path.GetRelativePath(upstreamRoot, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            sourceFiles,
            mappedByFile.Keys.Order(StringComparer.Ordinal).ToArray());

        foreach (var sourceFile in sourceFiles)
        {
            var source = File.ReadAllText(Path.Combine(
                upstreamRoot,
                sourceFile.Replace('/', Path.DirectorySeparatorChar)));
            var exportedTypes = ExportedTypeRegex()
                .Matches(source)
                .Select(match => match.Groups[1].Value)
                .Order(StringComparer.Ordinal)
                .ToArray();
            var mappedTypes = mappedByFile[sourceFile].UpstreamTypes
                .Order(StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(exportedTypes, mappedTypes);
        }
    }

    private static List<ContractEntry> ParseManifest(string text)
    {
        var entries = new List<ContractEntry>();
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
            Assert.True(fields.Length == 5, $"Invalid manifest row {lineNumber}.");
            entries.Add(new ContractEntry(
                fields[0],
                fields[1] == "-"
                    ? []
                    : fields[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                fields[2],
                fields[3].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                fields[4]));
        }

        return entries;
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
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));

    [GeneratedRegex(@"(?m)^type\s+([A-Z][A-Za-z0-9_]*)\b", RegexOptions.CultureInvariant)]
    private static partial Regex ExportedTypeRegex();

    private sealed record ContractEntry(
        string UpstreamFile,
        IReadOnlyList<string> UpstreamTypes,
        string Disposition,
        IReadOnlyList<string> Targets,
        string Reason);
}
