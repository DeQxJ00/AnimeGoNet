using System.Text.RegularExpressions;

namespace AnimeGoNet.Core.Configuration;

public static partial class AnimeGoOptionsValidator
{
    public static IReadOnlyList<string> Validate(AnimeGoOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var errors = new List<string>();

        ValidateAbsolutePath(options.Paths.DataPath, "data_path", errors);
        ValidateAbsolutePath(options.Paths.DownloadPath, "download_path", errors);
        ValidateAbsolutePath(options.Paths.SavePath, "save_path", errors);

        if (options.Downloaders.Count == 0)
        {
            errors.Add("At least one qBittorrent instance is required.");
        }

        foreach (var (rawId, downloader) in options.Downloaders)
        {
            var id = rawId.ToLowerInvariant();
            if (!rawId.Equals(id, StringComparison.Ordinal) || !StableId().IsMatch(id))
            {
                errors.Add($"Downloader id '{rawId}' must already be lowercase and contain only letters, digits, '.', '_' or '-'.");
            }

            if (!downloader.Type.Equals(DownloaderTypes.Qbittorrent, StringComparison.Ordinal))
            {
                errors.Add($"Downloader '{rawId}' has unsupported type '{downloader.Type}'. Only qBittorrent is supported.");
            }

            if (downloader.BaseUrl.Scheme is not ("http" or "https"))
            {
                errors.Add($"Downloader '{rawId}' base URL must use HTTP or HTTPS.");
            }

            if (!PathBoundary.IsWithin(options.Paths.DownloadPath, downloader.DownloadPath))
            {
                errors.Add($"Downloader '{rawId}' download path must be inside download_path.");
            }
        }

        foreach (var profile in options.InitialSourceProfiles)
        {
            if (!profile.Id.Equals(profile.Id.ToLowerInvariant(), StringComparison.Ordinal) || !StableId().IsMatch(profile.Id))
            {
                errors.Add($"Source profile id '{profile.Id}' is not a stable lowercase id.");
            }

            if (!options.Downloaders.ContainsKey(profile.DownloaderId))
            {
                errors.Add($"Source profile '{profile.Id}' references missing downloader '{profile.DownloaderId}'.");
            }
        }

        if (options.Metadata.Ai.HttpTimeout <= TimeSpan.Zero)
        {
            errors.Add("AI HTTP timeout must be positive.");
        }

        return errors;
    }

    private static void ValidateAbsolutePath(string path, string name, List<string> errors)
    {
        if (!PathBoundary.IsAbsolute(path))
        {
            errors.Add($"{name} must be an absolute path.");
        }
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]*$")]
    private static partial Regex StableId();
}
