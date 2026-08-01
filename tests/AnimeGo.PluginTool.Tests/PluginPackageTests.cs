using System.IO.Compression;
using System.Security.Cryptography;

namespace AnimeGo.PluginTool.Tests;

public sealed class PluginPackageTests
{
    [Fact]
    public async Task PackProducesByteIdenticalSortedArchivesWithStableMetadata()
    {
        using var package = new PluginToolTestPackage();
        var nested = Path.Combine(package.PackagePath, "assets");
        Directory.CreateDirectory(nested);
        await File.WriteAllTextAsync(Path.Combine(nested, "z.txt"), "fixture");
        var first = Path.Combine(package.RootPath, "first.zip");
        var second = Path.Combine(package.RootPath, "second.zip");

        var firstResult = await PluginToolTestDriver.InvokeAsync(
            ["pack", package.PackagePath, "--output", first]);
        var secondResult = await PluginToolTestDriver.InvokeAsync(
            ["pack", package.PackagePath, "--output", second]);

        Assert.Equal(0, firstResult.ExitCode);
        Assert.Equal(0, secondResult.ExitCode);
        Assert.Equal(await File.ReadAllBytesAsync(first), await File.ReadAllBytesAsync(second));
        Assert.Equal(
            firstResult.OutputJson.GetProperty("archiveSha256").GetString(),
            secondResult.OutputJson.GetProperty("archiveSha256").GetString());
        using var archive = ZipFile.OpenRead(first);
        var names = archive.Entries.Select(entry => entry.FullName).ToArray();
        Assert.Equal(names.Order(StringComparer.Ordinal), names);
        Assert.All(archive.Entries, entry =>
        {
            Assert.Equal(1980, entry.LastWriteTime.Year);
            Assert.Equal(0, entry.ExternalAttributes);
        });
    }

    [Fact]
    public async Task PackProtectsOutputAndForceOnlyReplacesTheExactArchive()
    {
        using var package = new PluginToolTestPackage();
        var output = Path.Combine(package.RootPath, "plugin.zip");
        await File.WriteAllTextAsync(output, "existing");
        var inside = Path.Combine(package.PackagePath, "inside.zip");

        var existing = await PluginToolTestDriver.InvokeAsync(
            ["pack", package.PackagePath, "--output", output]);
        var insideResult = await PluginToolTestDriver.InvokeAsync(
            ["pack", package.PackagePath, "--output", inside]);
        var forced = await PluginToolTestDriver.InvokeAsync(
            ["pack", package.PackagePath, "--output", output, "--force"]);

        Assert.Equal(6, existing.ExitCode);
        Assert.Equal("plugin_pack_output_exists", existing.ErrorJson.GetProperty("code").GetString());
        Assert.Equal(6, insideResult.ExitCode);
        Assert.Equal(
            "plugin_pack_output_inside_package",
            insideResult.ErrorJson.GetProperty("code").GetString());
        Assert.Equal(0, forced.ExitCode);
        Assert.Equal([0x50, 0x4b], (await File.ReadAllBytesAsync(output))[..2]);
    }

    [Fact]
    public async Task PackerDetectsPackageMutationAfterAuditAndDoesNotCommitOutput()
    {
        using var package = new PluginToolTestPackage();
        var loaded = await package.CreateLoader().LoadPackageAsync(package.PackagePath);
        var audited = await new PluginPackageAuditor().AuditAsync(
            loaded,
            CancellationToken.None);
        await File.AppendAllTextAsync(Path.Combine(package.PackagePath, "plugin.json"), " ");
        var output = Path.Combine(package.RootPath, "changed.zip");

        var exception = await Assert.ThrowsAsync<PluginToolException>(() =>
            new PluginPackagePacker().PackAsync(
                audited,
                output,
                force: false,
                CancellationToken.None));

        Assert.Equal("plugin_package_changed", exception.Code);
        Assert.False(File.Exists(output));
        Assert.Empty(Directory.EnumerateFiles(
            package.RootPath,
            ".animego-plugin-pack-*.tmp"));
    }

    [Fact]
    public async Task PackerCancellationDoesNotCommitAnOutputOrTemporaryArchive()
    {
        using var package = new PluginToolTestPackage();
        var loaded = await package.CreateLoader().LoadPackageAsync(package.PackagePath);
        var audited = await new PluginPackageAuditor().AuditAsync(
            loaded,
            CancellationToken.None);
        var output = Path.Combine(package.RootPath, "canceled.zip");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new PluginPackagePacker().PackAsync(
                audited,
                output,
                force: false,
                cancellation.Token));

        Assert.False(File.Exists(output));
        Assert.Empty(Directory.EnumerateFiles(
            package.RootPath,
            ".animego-plugin-pack-*.tmp"));
    }

    [Fact]
    public async Task AuditDigestChangesWhenContentChangesWithoutChangingLength()
    {
        using var package = new PluginToolTestPackage();
        var loader = package.CreateLoader();
        var loaded = await loader.LoadPackageAsync(package.PackagePath);
        var auditor = new PluginPackageAuditor();
        var first = await auditor.AuditAsync(loaded, CancellationToken.None);
        var entryPath = Path.Combine(package.PackagePath, package.EntryPoint);
        await File.WriteAllBytesAsync(entryPath, [0x03, 0x02, 0x01]);
        var second = await auditor.AuditAsync(loaded, CancellationToken.None);

        Assert.NotEqual(first.ContentSha256, second.ContentSha256);
        Assert.Equal(first.TotalBytes, second.TotalBytes);
        Assert.Equal(
            64,
            Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(entryPath))).Length);
    }

    [Fact]
    public async Task AuditorRejectsDirectoryLinksWithoutFollowingTheirTargets()
    {
        using var package = new PluginToolTestPackage();
        var loaded = await package.CreateLoader().LoadPackageAsync(package.PackagePath);
        var outside = Path.Combine(package.RootPath, "outside");
        Directory.CreateDirectory(outside);
        await File.WriteAllTextAsync(Path.Combine(outside, "private.txt"), "outside");
        var link = Path.Combine(package.PackagePath, "linked");
        try
        {
            Directory.CreateSymbolicLink(link, outside);
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or PlatformNotSupportedException
            || exception is IOException && OperatingSystem.IsWindows())
        {
            return;
        }

        var error = await Assert.ThrowsAsync<PluginToolException>(() =>
            new PluginPackageAuditor().AuditAsync(loaded, CancellationToken.None));

        Assert.Equal("plugin_package_link_disallowed", error.Code);
    }
}
