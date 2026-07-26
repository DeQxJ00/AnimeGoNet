namespace AnimeGoNet.Core.Configuration;

public sealed record DirectoryLayout
{
    public required string DataPath { get; init; }

    public required string DatabaseFile { get; init; }

    public required string StagingPath { get; init; }

    public required string CachePath { get; init; }

    public required string LogsPath { get; init; }

    public required string BackupsPath { get; init; }

    public required string PluginsPath { get; init; }

    public required string ConfigurationPath { get; init; }

    public static DirectoryLayout From(PathOptions paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return new DirectoryLayout
        {
            DataPath = paths.DataPath,
            DatabaseFile = PathBoundary.Combine(paths.DataPath, "animegonet.db"),
            StagingPath = PathBoundary.Combine(paths.DataPath, "staging"),
            CachePath = PathBoundary.Combine(paths.DataPath, "cache"),
            LogsPath = PathBoundary.Combine(paths.DataPath, "logs"),
            BackupsPath = PathBoundary.Combine(paths.DataPath, "backups"),
            PluginsPath = PathBoundary.Combine(paths.DataPath, "plugins"),
            ConfigurationPath = PathBoundary.Combine(paths.DataPath, "config"),
        };
    }

    public void CreateDataDirectories()
    {
        Directory.CreateDirectory(DataPath);
        Directory.CreateDirectory(StagingPath);
        Directory.CreateDirectory(CachePath);
        Directory.CreateDirectory(LogsPath);
        Directory.CreateDirectory(BackupsPath);
        Directory.CreateDirectory(PluginsPath);
        Directory.CreateDirectory(ConfigurationPath);
    }
}
