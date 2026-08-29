using AnimeGoNet.Core.Library;
using AnimeGoNet.Data.Metadata;

namespace AnimeGoNet.App.Metadata;

public sealed record U2MovieFileLayout(
    MetadataTaskFileProjection? MainFile,
    IReadOnlyList<MetadataTaskFileProjection> ExtraVideos,
    string? FailureCode)
{
    public bool IsResolved => MainFile is not null && FailureCode is null;
}

public static class U2MovieFileLayoutResolver
{
    private const long OneGibibyte = 1024L * 1024 * 1024;
    private const long MinimumAbsoluteGap = 512L * 1024 * 1024;
    private const int MinimumSizeRatio = 3;

    public static U2MovieFileLayout Resolve(
        IReadOnlyList<MetadataTaskFileProjection>? files)
    {
        var videos = (files ?? [])
            .Where(file => SubtitleAssociationResolver.IsVideo(file.RelativePath))
            .OrderByDescending(file => file.SizeBytes)
            .ThenBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToArray();
        if (videos.Length == 0)
        {
            return new U2MovieFileLayout(null, [], "movie_video_missing");
        }

        if (videos.Length == 1)
        {
            return new U2MovieFileLayout(videos[0], [], null);
        }

        var largest = videos[0];
        var runnerUp = videos[1];
        var hasFeatureLengthSize = largest.SizeBytes >= OneGibibyte;
        var hasAbsoluteGap = largest.SizeBytes - runnerUp.SizeBytes >= MinimumAbsoluteGap;
        var hasRatioGap = runnerUp.SizeBytes <= largest.SizeBytes / MinimumSizeRatio;
        if (!hasFeatureLengthSize || !hasAbsoluteGap || !hasRatioGap)
        {
            return new U2MovieFileLayout(null, [], "u2_movie_main_file_ambiguous");
        }

        return new U2MovieFileLayout(largest, videos[1..], null);
    }
}
