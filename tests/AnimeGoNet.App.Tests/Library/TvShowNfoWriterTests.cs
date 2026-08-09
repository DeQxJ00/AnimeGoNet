using System.Xml.Linq;
using AnimeGoNet.App.Library;
using AnimeGoNet.Core.Configuration;

namespace AnimeGoNet.App.Tests.Library;

public sealed class TvShowNfoWriterTests
{
    [Fact]
    public async Task TmdbMatchOmitsBangumiIdByDefault()
    {
        await using var fixture = new NfoFixture(writeBangumiIdWhenTmdbMatched: false);

        await fixture.Writer.WriteAsync(fixture.SaveRoot, "Series", 100, 547888);

        var document = fixture.Read("Series");
        Assert.Equal("100", document.Root?.Element("tmdbid")?.Value);
        Assert.Null(document.Root?.Element("bangumiid"));
    }

    [Fact]
    public async Task TmdbMatchWritesBangumiIdWhenExplicitlyEnabled()
    {
        await using var fixture = new NfoFixture(writeBangumiIdWhenTmdbMatched: true);

        await fixture.Writer.WriteAsync(fixture.SaveRoot, "Series", 100, 547888);

        var document = fixture.Read("Series");
        Assert.Equal("547888", document.Root?.Element("bangumiid")?.Value);
    }

    [Fact]
    public async Task BangumiFallbackAlwaysWritesBangumiId()
    {
        await using var fixture = new NfoFixture(writeBangumiIdWhenTmdbMatched: false);

        await fixture.Writer.WriteAsync(fixture.SaveRoot, "Fallback", 0, 547888);

        var document = fixture.Read("Fallback");
        Assert.Equal("0", document.Root?.Element("tmdbid")?.Value);
        Assert.Equal("547888", document.Root?.Element("bangumiid")?.Value);
    }

    private sealed class NfoFixture : IAsyncDisposable
    {
        private readonly string _root;

        public NfoFixture(bool writeBangumiIdWhenTmdbMatched)
        {
            _root = Path.Combine(Path.GetTempPath(), "animegonet-nfo-tests", Guid.NewGuid().ToString("N"));
            SaveRoot = Path.Combine(_root, "library");
            Directory.CreateDirectory(SaveRoot);
            var defaults = AnimeGoDefaults.CreateNative(_root);
            var options = defaults with
            {
                Metadata = defaults.Metadata with
                {
                    WriteBangumiIdWhenTmdbMatched = writeBangumiIdWhenTmdbMatched,
                },
            };
            Writer = new TvShowNfoWriter(options);
        }

        public string SaveRoot { get; }

        public TvShowNfoWriter Writer { get; }

        public XDocument Read(string series) =>
            XDocument.Load(Path.Combine(SaveRoot, series, "tvshow.nfo"));

        public ValueTask DisposeAsync()
        {
            Directory.Delete(_root, recursive: true);
            return ValueTask.CompletedTask;
        }
    }
}
