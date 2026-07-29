using AnimeGoNet.Core.Configuration;

namespace AnimeGoNet.Core.Tests.Configuration;

public sealed class DirectoryLayoutTests
{
    [Fact]
    public void CreatesOnlyDataSubdirectories()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "animegonet-core-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var options = AnimeGoDefaults.CreateNative(testRoot);
            var layout = DirectoryLayout.From(options.Paths);

            layout.CreateDataDirectories();

            Assert.True(Directory.Exists(layout.DataPath));
            Assert.True(Directory.Exists(layout.StagingPath));
            Assert.True(Directory.Exists(layout.CachePath));
            Assert.True(Directory.Exists(layout.LogsPath));
            Assert.True(Directory.Exists(layout.BackupsPath));
            Assert.True(Directory.Exists(layout.PluginsPath));
            Assert.True(Directory.Exists(layout.DataUpdatePath));
            Assert.False(Directory.Exists(options.Paths.DownloadPath));
            Assert.False(Directory.Exists(options.Paths.SavePath));
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }
}
