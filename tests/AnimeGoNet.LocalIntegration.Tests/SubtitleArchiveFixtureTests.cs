using AnimeGoNet.App.Library;
using AnimeGoNet.Core.Configuration;
using Xunit.Sdk;

namespace AnimeGoNet.LocalIntegration.Tests;

/// <summary>
/// Explicitly opt-in coverage for real subtitle archive files kept outside Git.
/// The fixture directory is intentionally ignored so private media/subtitle data
/// cannot be staged accidentally.
/// </summary>
public sealed class SubtitleArchiveFixtureTests
{
    private static readonly string[] SupportedExtensions =
    [
        ".zip", ".rar", ".7z", ".tar",
        ".tar.gz", ".tgz", ".tar.bz2", ".tbz2", ".tar.xz", ".txz",
    ];

    [Fact]
    [Trait("Category", "LocalIntegration")]
    public async Task ImportsEveryLocalSubtitleArchiveFixture()
    {
        Assert.Equal(
            "1",
            Environment.GetEnvironmentVariable("ANIMEGONET_SUBTITLE_ARCHIVE_INTEGRATION"));

        var fixtureRoot = FindFixtureRoot();
        Assert.NotNull(fixtureRoot);

        var archives = Directory.EnumerateFiles(fixtureRoot!, "*", SearchOption.AllDirectories)
            .Where(IsSupportedArchive)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Assert.NotEmpty(archives);

        var results = new List<string>(archives.Length);
        foreach (var archivePath in archives)
        {
            var root = Path.Combine(
                Path.GetTempPath(), "animegonet-subtitle-fixture-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var paths = AnimeGoDefaults.CreateNative(root).Paths;
                var service = new SubtitleArchiveImportService(DirectoryLayout.From(paths));
                await using var archive = new FileStream(
                    archivePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    64 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);

                var session = await service.ImportAsync(
                    archive,
                    Path.GetFileName(archivePath),
                    tmdbSeriesId: 72517,
                    seasonNumber: 1,
                    seriesName: "Subtitle Fixture Show");

                Assert.NotEmpty(session.Candidates);
                results.Add($"{Path.GetFileName(archivePath)}: {session.Candidates.Count} subtitle candidates");
            }
            catch (Exception exception) when (exception is not XunitException)
            {
                throw new XunitException(
                    $"Real subtitle archive fixture '{Path.GetFileName(archivePath)}' failed: {exception.Message}",
                    exception);
            }
            finally
            {
                TryDelete(root);
            }
        }

        Assert.NotEmpty(results);
    }

    private static bool IsSupportedArchive(string path)
    {
        var name = Path.GetFileName(path);
        return SupportedExtensions.Any(extension =>
            name.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
    }

    private static string? FindFixtureRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "subtitles-test");
            if (Directory.Exists(candidate)) return candidate;
            current = current.Parent;
        }

        return null;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup; the fixture test never touches the source archive.
        }
    }
}
