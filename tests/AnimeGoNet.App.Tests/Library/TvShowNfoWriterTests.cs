using System.Xml.Linq;
using AnimeGoNet.App.Library;
using AnimeGoNet.Core.Configuration;

namespace AnimeGoNet.App.Tests.Library;

public sealed class TvShowNfoWriterTests
{
    [Theory]
    [InlineData("<bangumiid>123</bangumiid>", false, "123")]
    [InlineData("<bangumiid>123</bangumiid>", true, "888")]
    [InlineData("<bangumiid />", false, "888")]
    [InlineData("<bangumiid> </bangumiid>", false, "888")]
    [InlineData("", false, "888")]
    public async Task SeasonExistingIdPolicy(string existing, bool overwrite, string expected)
    {
        await using var fixture = new NfoFixture(false, true, overwrite);
        var directory = Path.Combine(fixture.SaveRoot, "Series", "S02");
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, "season.nfo");
        var original = "<season><title>Keep</title>" + existing + "</season>";
        await File.WriteAllTextAsync(target, original);
        await fixture.Writer.WriteSeasonAsync(fixture.SaveRoot, "Series", 100, 2, 888);
        var actual = await File.ReadAllTextAsync(target);
        var document = XDocument.Parse(actual);
        Assert.Equal(expected, Assert.Single(document.Root!.Elements("bangumiid")).Value);
        Assert.Equal("Keep", document.Root.Element("title")!.Value);
        if (!overwrite && expected == "123") Assert.Equal(original, actual);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SeriesAndSeasonSwitchesAreIndependent(bool series, bool season)
    {
        await using var fixture = new NfoFixture(series, season);
        await fixture.Writer.WriteAsync(fixture.SaveRoot, "Series", 100, 547888);
        await fixture.Writer.WriteSeasonAsync(fixture.SaveRoot, "Series", 100, 2, 547888);
        Assert.Equal(series ? "547888" : null, fixture.Read("Series").Root?.Element("bangumiid")?.Value);
        var target = Path.Combine(fixture.SaveRoot, "Series", "S02", "season.nfo");
        Assert.Equal(season, File.Exists(target));
        Assert.False(File.Exists(Path.Combine(fixture.SaveRoot, "Series", "S01", "season.nfo")));
        if (season)
        {
            var nfo = XDocument.Load(target);
            Assert.Equal("547888", nfo.Root?.Element("bangumiid")?.Value);
            Assert.Equal("2", nfo.Root?.Element("seasonnumber")?.Value);
            Assert.Null(nfo.Root?.Element("tmdbid"));
        }
    }

    [Fact]
    public async Task SeasonUpdatePreservesOtherMetadataAndNeverWritesWithoutSourceId()
    {
        await using var fixture = new NfoFixture(false, true, overwrite: true);
        var directory = Path.Combine(fixture.SaveRoot, "Series", "S03");
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, "season.nfo");
        const string original = "<season><title>Existing title</title><plot>Keep</plot><uniqueid type=\"tmdb\">123</uniqueid><bangumiid>1</bangumiid></season>";
        await File.WriteAllTextAsync(target, original);
        await fixture.Writer.WriteSeasonAsync(fixture.SaveRoot, "Series", 100, 3, null);
        Assert.Equal(original, await File.ReadAllTextAsync(target));
        await fixture.Writer.WriteSeasonAsync(fixture.SaveRoot, "Series", 0, 3, 888);
        Assert.Equal(original, await File.ReadAllTextAsync(target));
        await fixture.Writer.WriteSeasonAsync(fixture.SaveRoot, "Series", 100, 3, 888);
        var nfo = XDocument.Load(target);
        Assert.Equal("Existing title", nfo.Root?.Element("title")?.Value);
        Assert.Equal("Keep", nfo.Root?.Element("plot")?.Value);
        Assert.Equal("123", nfo.Root?.Element("uniqueid")?.Value);
        Assert.Equal("888", Assert.Single(nfo.Root!.Elements("bangumiid")).Value);
    }
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

        public NfoFixture(bool writeBangumiIdWhenTmdbMatched, bool writeSeasonBangumiIdWhenTmdbMatched = false, bool overwrite = false)
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
                    WriteSeasonBangumiIdWhenTmdbMatched = writeSeasonBangumiIdWhenTmdbMatched,
                    OverwriteSeasonBangumiId = overwrite,
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
