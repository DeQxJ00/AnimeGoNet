using System.IO.Compression;
using AnimeGoNet.App.Library;
using AnimeGoNet.Core.Configuration;

namespace AnimeGoNet.App.Tests.Library;

public sealed class SubtitleArchiveImportServiceTests
{
    [Fact]
    public async Task ImportParsesEpisodesAndConfirmPlacesUnmatchedFilesInExtras()
    {
        var root = Path.Combine(Path.GetTempPath(), "animegonet-subtitle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var paths = AnimeGoDefaults.CreateNative(root).Paths;
            var service = new SubtitleArchiveImportService(DirectoryLayout.From(paths));
            await using var archive = new MemoryStream();
            using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, leaveOpen: true))
            {
                await AddAsync(zip, "Show/Show - 03.zh.ass", "ep3");
                await AddAsync(zip, "Show/NCOP.ass", "opening");
            }
            archive.Position = 0;

            var session = await service.ImportAsync(
                archive, "subtitles.zip", 123, 1, "Show");
            Assert.Equal(2, session.Candidates.Count);
            Assert.Contains(session.Candidates, value => value.ParsedEpisode == 3);
            Assert.Contains(session.Candidates, value => value.ParsedEpisode is null);

            var parsed = Assert.Single(session.Candidates, value => value.ParsedEpisode == 3);
            var unparsed = Assert.Single(session.Candidates, value => value.ParsedEpisode is null);
            var result = await service.ConfirmAsync(
                session.SessionId,
                [
                    new SubtitleArchiveAssignment(parsed.Id, 3),
                    new SubtitleArchiveAssignment(unparsed.Id, null),
                ],
                paths.SavePath);

            Assert.NotNull(result);
            Assert.Equal(1, result!.ImportedCount);
            Assert.Equal(1, result.ExtrasCount);
            Assert.True(File.Exists(Path.Combine(paths.SavePath, "Show", "S01", "E003.zh.ass")));
            Assert.True(File.Exists(Path.Combine(paths.SavePath, "Show", "S01", "Extras", "NCOP.ass")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task AddAsync(ZipArchive archive, string name, string content)
    {
        await using var stream = archive.CreateEntry(name).Open();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(content);
    }
}
