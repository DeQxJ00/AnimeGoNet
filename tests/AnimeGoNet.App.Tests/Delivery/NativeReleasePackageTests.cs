using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace AnimeGoNet.App.Tests.Delivery;

public sealed class NativeReleasePackageTests
{
    [Fact]
    public async Task CreatesDeterministicVerifiedArchiveWithUnixExecutableModes()
    {
        var root = TemporaryRoot();
        try
        {
            var publish = await CreatePublishFixtureAsync(root);
            var firstOutput = Path.Combine(root, "first");
            var secondOutput = Path.Combine(root, "second");
            var first = await RunAsync(publish, firstOutput, "linux-arm64", "V0.1.0-RC.1");
            var second = await RunAsync(publish, secondOutput, "linux-arm64", "V0.1.0-RC.1");
            Assert.Equal(0, first.ExitCode);
            Assert.Equal(0, second.ExitCode);

            var archiveName = "animegonet-v0.1.0-rc.1-linux-arm64.zip";
            var firstArchive = Path.Combine(firstOutput, archiveName);
            var secondArchive = Path.Combine(secondOutput, archiveName);
            Assert.Equal(
                await File.ReadAllBytesAsync(firstArchive),
                await File.ReadAllBytesAsync(secondArchive));
            Assert.Equal(
                await File.ReadAllBytesAsync(firstArchive + ".sha256"),
                await File.ReadAllBytesAsync(secondArchive + ".sha256"));

            var checksum = await File.ReadAllTextAsync(firstArchive + ".sha256");
            Assert.Equal(
                $"{Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(firstArchive)))}  {archiveName}\n",
                checksum.Replace("\r\n", "\n", StringComparison.Ordinal));

            using var zip = ZipFile.OpenRead(firstArchive);
            var entries = zip.Entries.ToDictionary(entry => entry.FullName, StringComparer.Ordinal);
            Assert.Equal(
                entries.Keys.Order(StringComparer.Ordinal),
                entries.Keys);
            Assert.Equal(0x81ed, UnixMode(entries["AnimeGoNet.App"]));
            Assert.Equal(0x81ed, UnixMode(entries["AnimeGoNet.LegacyCacheImporter"]));
            Assert.Equal(0x81a4, UnixMode(entries["sbom.cdx.json"]));
            Assert.Equal(0x81a4, UnixMode(entries["docs/readme.txt"]));
            Assert.Equal("application", await ReadEntryAsync(entries["AnimeGoNet.App"]));
            Assert.Equal("fixture", await ReadEntryAsync(entries["docs/readme.txt"]));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RejectsTamperedOrUnchecksummedPublishTreesWithoutLeavingArchive(bool tamper)
    {
        var root = TemporaryRoot();
        try
        {
            var publish = await CreatePublishFixtureAsync(root);
            if (tamper)
            {
                await File.AppendAllTextAsync(Path.Combine(publish, "AnimeGoNet.App"), "tampered");
            }
            else
            {
                await File.WriteAllTextAsync(Path.Combine(publish, "unexpected.txt"), "unexpected");
            }

            var output = Path.Combine(root, "output");
            var result = await RunAsync(publish, output, "linux-x64", "v0.1.0-beta.1");
            Assert.NotEqual(0, result.ExitCode);
            Assert.False(File.Exists(Path.Combine(
                output,
                "animegonet-v0.1.0-beta.1-linux-x64.zip")));
            Assert.False(File.Exists(Path.Combine(
                output,
                "animegonet-v0.1.0-beta.1-linux-x64.zip.sha256")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<string> CreatePublishFixtureAsync(string root)
    {
        var publish = Path.Combine(root, "publish");
        Directory.CreateDirectory(Path.Combine(publish, "docs"));
        await File.WriteAllTextAsync(Path.Combine(publish, "AnimeGoNet.App"), "application");
        await File.WriteAllTextAsync(
            Path.Combine(publish, "AnimeGoNet.LegacyCacheImporter"),
            "importer");
        await File.WriteAllTextAsync(Path.Combine(publish, "sbom.cdx.json"), "{}\n");
        await File.WriteAllTextAsync(
            Path.Combine(publish, "THIRD-PARTY-LICENSES.txt"),
            "licenses\n");
        await File.WriteAllTextAsync(Path.Combine(publish, "docs", "readme.txt"), "fixture");

        var files = Directory.EnumerateFiles(publish, "*", SearchOption.AllDirectories)
            .Select(path => new
            {
                Path = path,
                Relative = Path.GetRelativePath(publish, path)
                    .Replace(Path.DirectorySeparatorChar, '/'),
            })
            .OrderBy(value => value.Relative, StringComparer.Ordinal)
            .ToArray();
        var lines = new List<string>(files.Length);
        foreach (var file in files)
        {
            lines.Add(
                $"{Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(file.Path)))}  {file.Relative}");
        }
        await File.WriteAllTextAsync(
            Path.Combine(publish, "SHA256SUMS"),
            string.Join('\n', lines) + "\n",
            new UTF8Encoding(false));
        return publish;
    }

    private static async Task<ScriptResult> RunAsync(
        string publish,
        string output,
        string rid,
        string version)
    {
        var start = new ProcessStartInfo("pwsh")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("-NoLogo");
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(Path.Combine(RepositoryRoot(), "eng", "package-native-release.ps1"));
        start.ArgumentList.Add("-PublishDirectory");
        start.ArgumentList.Add(publish);
        start.ArgumentList.Add("-OutputDirectory");
        start.ArgumentList.Add(output);
        start.ArgumentList.Add("-RuntimeIdentifier");
        start.ArgumentList.Add(rid);
        start.ArgumentList.Add("-Version");
        start.ArgumentList.Add(version);

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Failed to start release packager.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ScriptResult(process.ExitCode, await outputTask, await errorTask);
    }

    private static int UnixMode(ZipArchiveEntry entry) =>
        (int)(unchecked((uint)entry.ExternalAttributes) >> 16);

    private static async Task<string> ReadEntryAsync(ZipArchiveEntry entry)
    {
        await using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    private static string TemporaryRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "animegonet-native-package-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));

    private sealed record ScriptResult(int ExitCode, string Output, string Error);
}
