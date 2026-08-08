using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace AnimeGoNet.App.Tests.Delivery;

public sealed class ReleaseMetadataContractTests
{
    [Fact]
    public async Task NativeAotMatrixGeneratesMetadataBeforeArtifactUpload()
    {
        var root = RepositoryRoot();
        var workflow = await File.ReadAllTextAsync(Path.Combine(
            root,
            ".github",
            "workflows",
            "animegonet-native-aot.yml"));
        var script = await File.ReadAllTextAsync(Path.Combine(
            root,
            "eng",
            "generate-release-metadata.ps1"));

        var generation = workflow.IndexOf(
            "./eng/generate-release-metadata.ps1",
            StringComparison.Ordinal);
        var upload = workflow.IndexOf("actions/upload-artifact@v7", StringComparison.Ordinal);
        Assert.True(generation >= 0);
        Assert.True(upload > generation);
        Assert.Contains("-PublishDirectory \"artifacts/publish/${{ matrix.rid }}\"", workflow, StringComparison.Ordinal);
        Assert.Contains("-AssetsFile \"src/AnimeGoNet.App/obj/project.assets.json\"", workflow, StringComparison.Ordinal);
        Assert.Contains("-RuntimeIdentifier \"${{ matrix.rid }}\"", workflow, StringComparison.Ordinal);
        Assert.Contains("SHA256SUMS", script, StringComparison.Ordinal);
        Assert.Contains("sbom.cdx.json", script, StringComparison.Ordinal);
        Assert.Contains("THIRD-PARTY-LICENSES.txt", script, StringComparison.Ordinal);
        Assert.Contains("[StringComparer]::Ordinal", script, StringComparison.Ordinal);
        Assert.DoesNotContain("TestSpace", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("192.168.", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("passkey", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ScriptProducesDeterministicVerifiableReleaseMetadata()
    {
        var repositoryRoot = RepositoryRoot();
        var root = Path.Combine(
            Path.GetTempPath(),
            "animegonet-release-metadata-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllBytesAsync(
                Path.Combine(root, "AnimeGoNet.App"),
                [0x41, 0x4e, 0x49, 0x4d, 0x45]);
            Directory.CreateDirectory(Path.Combine(root, "docs"));
            await File.WriteAllTextAsync(Path.Combine(root, "docs", "readme.txt"), "fixture\n");

            var script = Path.Combine(repositoryRoot, "eng", "generate-release-metadata.ps1");
            var assets = Path.Combine(
                repositoryRoot,
                "src",
                "AnimeGoNet.App",
                "obj",
                "project.assets.json");
            await RunScriptAsync(script, root, assets);
            var first = await ReadGeneratedFilesAsync(root);
            await RunScriptAsync(script, root, assets);
            var second = await ReadGeneratedFilesAsync(root);

            Assert.Equal(first.Sbom, second.Sbom);
            Assert.Equal(first.Licenses, second.Licenses);
            Assert.Equal(first.Checksums, second.Checksums);

            using var document = JsonDocument.Parse(first.Sbom);
            var json = document.RootElement;
            Assert.Equal("CycloneDX", json.GetProperty("bomFormat").GetString());
            Assert.Equal("1.5", json.GetProperty("specVersion").GetString());
            var application = json.GetProperty("metadata").GetProperty("component");
            Assert.Equal("AnimeGoNet", application.GetProperty("name").GetString());
            Assert.Equal("fixture-1", application.GetProperty("version").GetString());
            var components = json.GetProperty("components").EnumerateArray().ToArray();
            Assert.NotEmpty(components);
            var componentNames = components
                .Select(component => component.GetProperty("name").GetString()!)
                .ToArray();
            Assert.Equal(
                componentNames.Order(StringComparer.Ordinal),
                componentNames);
            Assert.All(components, component =>
            {
                Assert.Equal("library", component.GetProperty("type").GetString());
                Assert.StartsWith("pkg:nuget/", component.GetProperty("purl").GetString(), StringComparison.Ordinal);
                var hash = Assert.Single(component.GetProperty("hashes").EnumerateArray());
                Assert.Equal("SHA-512", hash.GetProperty("alg").GetString());
                Assert.Matches("^[0-9a-f]{128}$", hash.GetProperty("content").GetString()!);
                Assert.NotEmpty(component.GetProperty("licenses").EnumerateArray());
            });

            var licenses = System.Text.Encoding.UTF8.GetString(first.Licenses);
            Assert.Contains("License: MIT", licenses, StringComparison.Ordinal);
            Assert.Contains("License: Apache-2.0", licenses, StringComparison.Ordinal);
            Assert.DoesNotContain(repositoryRoot, licenses, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(root, licenses, StringComparison.OrdinalIgnoreCase);

            var checksumText = System.Text.Encoding.UTF8.GetString(first.Checksums);
            var checksumLines = checksumText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var checksumPaths = checksumLines.Select(line => line[66..]).ToArray();
            Assert.Equal(checksumPaths.Order(StringComparer.Ordinal), checksumPaths);
            Assert.Contains("AnimeGoNet.App", checksumPaths);
            Assert.Contains("docs/readme.txt", checksumPaths);
            Assert.Contains("sbom.cdx.json", checksumPaths);
            Assert.Contains("THIRD-PARTY-LICENSES.txt", checksumPaths);
            Assert.DoesNotContain("SHA256SUMS", checksumPaths);
            foreach (var line in checksumLines)
            {
                Assert.Equal("  ", line[64..66]);
                var expected = line[..64];
                var path = Path.Combine(root, line[66..].Replace('/', Path.DirectorySeparatorChar));
                var actual = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(path)));
                Assert.Equal(expected, actual);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task RunScriptAsync(
        string script,
        string publishDirectory,
        string assetsFile)
    {
        var start = new ProcessStartInfo
        {
            FileName = "pwsh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("-NoLogo");
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(script);
        start.ArgumentList.Add("-PublishDirectory");
        start.ArgumentList.Add(publishDirectory);
        start.ArgumentList.Add("-AssetsFile");
        start.ArgumentList.Add(assetsFile);
        start.ArgumentList.Add("-RuntimeIdentifier");
        start.ArgumentList.Add("win-x64");
        start.ArgumentList.Add("-Version");
        start.ArgumentList.Add("fixture-1");

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Failed to start pwsh.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(
            process.ExitCode == 0,
            $"Release metadata script failed. stdout: {await output} stderr: {await error}");
    }

    private static async Task<GeneratedFiles> ReadGeneratedFilesAsync(string root) =>
        new(
            await File.ReadAllBytesAsync(Path.Combine(root, "sbom.cdx.json")),
            await File.ReadAllBytesAsync(Path.Combine(root, "THIRD-PARTY-LICENSES.txt")),
            await File.ReadAllBytesAsync(Path.Combine(root, "SHA256SUMS")));

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."));

    private sealed record GeneratedFiles(byte[] Sbom, byte[] Licenses, byte[] Checksums);
}
