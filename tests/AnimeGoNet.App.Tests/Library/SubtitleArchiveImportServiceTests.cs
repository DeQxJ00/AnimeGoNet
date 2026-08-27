using System.IO.Compression;
using System.Formats.Tar;
using AnimeGoNet.App.Library;
using AnimeGoNet.App.Metadata;
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

    [Fact]
    public async Task ImportAcceptsAnAsyncOnlyUploadStream()
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
                await AddAsync(zip, "Show - 02.ass", "ep2");
            }

            await using var upload = new AsyncOnlyReadStream(archive.ToArray());
            var session = await service.ImportAsync(upload, "subtitles.zip", 123, 1, "Show");

            Assert.Single(session.Candidates);
            Assert.Equal(2, session.Candidates[0].ParsedEpisode);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ImportAcceptsTarSubtitleArchive()
    {
        var root = Path.Combine(Path.GetTempPath(), "animegonet-subtitle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var paths = AnimeGoDefaults.CreateNative(root).Paths;
            var service = new SubtitleArchiveImportService(DirectoryLayout.From(paths));
            await using var tar = new MemoryStream();
            using (var writer = new TarWriter(tar, TarEntryFormat.Pax, leaveOpen: true))
            {
                writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "Show - 04.zh.ass")
                {
                    DataStream = new MemoryStream("ep4"u8.ToArray()),
                });
            }

            tar.Position = 0;
            tar.Position = 0;
            var session = await service.ImportAsync(tar, "subtitles.tar", 123, 1, "Show");

            var candidate = Assert.Single(session.Candidates);
            Assert.Equal("Show - 04.zh.ass", candidate.FileName);
            Assert.Equal(4, candidate.ParsedEpisode);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ImportParsesJapaneseEpisodeMarkersWithoutTreatingAudioChannelsAsEpisodes()
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
                await AddAsync(
                    zip,
                    "[アニメ DVD] 機動戦艦ナデシコ 第01話 (WMV9 640x480 AC3 5.1ch).ass",
                    "ep1");
                await AddAsync(
                    zip,
                    "[アニメ DVD] 機動戦艦ナデシコ 第26話 (WMV9 640x480 AC3 5.1ch).ass",
                    "ep26");
                await AddAsync(zip, "音声 AC3 5.1ch.ass", "unmatched");
            }
            archive.Position = 0;

            var session = await service.ImportAsync(archive, "subtitles.zip", 123, 1, "Show");

            Assert.Equal(3, session.Candidates.Count);
            Assert.Equal(
                1,
                Assert.Single(session.Candidates, value => value.FileName.Contains("第01話", StringComparison.Ordinal))
                    .ParsedEpisode);
            Assert.Equal(
                26,
                Assert.Single(session.Candidates, value => value.FileName.Contains("第26話", StringComparison.Ordinal))
                    .ParsedEpisode);
            Assert.Null(
                Assert.Single(session.Candidates, value => value.FileName.StartsWith("音声", StringComparison.Ordinal))
                    .ParsedEpisode);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SubtitlePromptSatisfiesMetadataPromptContract()
    {
        AiMetadataPromptRenderer.ValidateTemplate(SubtitleAiPrompt.Template);
    }

    private static async Task AddAsync(ZipArchive archive, string name, string content)
    {
        await using var stream = archive.CreateEntry(name).Open();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(content);
    }

    private sealed class AsyncOnlyReadStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream _inner = new(bytes, writable: false);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => throw new NotSupportedException(); }
        public override void Flush() => throw new InvalidOperationException("Synchronous operations are disallowed.");
        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Synchronous operations are disallowed.");
        public override long Seek(long offset, SeekOrigin origin) => throw new InvalidOperationException("Synchronous operations are disallowed.");
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(buffer, cancellationToken);
    }
}
