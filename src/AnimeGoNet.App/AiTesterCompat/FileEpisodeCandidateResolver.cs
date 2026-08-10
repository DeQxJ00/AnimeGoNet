using System.Text.RegularExpressions;

namespace AnimeGoNet.App.AiTesterCompat;

/// <summary>
/// Applies AI-input safety policy after the upstream-compatible raw parser has run.
/// </summary>
public static partial class FileEpisodeCandidateResolver
{
    public static int? Resolve(string path)
    {
        string basename = FilenameTools.GetBasename(path);
        int episode = AutoBangumiRawParser.Parse(basename).Episode;
        if (IsUsableEpisode(episode)) return episode;

        Match seasonEpisode = SeasonEpisodePattern().Match(basename);
        return seasonEpisode.Success &&
               int.TryParse(seasonEpisode.Groups["episode"].Value, out episode) &&
               IsUsableEpisode(episode)
            ? episode
            : null;
    }

    private static bool IsUsableEpisode(int episode) =>
        episode > 0 && episode is not (>= 1900 and <= 2100);

    [GeneratedRegex(@"(?<![A-Za-z0-9])S\d{1,2}E(?<episode>\d{1,4})(?![A-Za-z0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SeasonEpisodePattern();
}
