using AnimeGoNet.Core.Metadata;
using AnimeGoNet.Core.Library;
using AnimeGoNet.Data.Metadata;

namespace AnimeGoNet.App.Metadata;

internal static class U2AiFilePolicy
{
    public static MetadataFailure? Validate(
        MetadataTaskClaim claim,
        IReadOnlyList<ValidatedAiMetadataFile> files)
    {
        if (!string.Equals(claim.SourceAdapter, "u2", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        foreach (var file in files)
        {
            if (file.Episode is null
                && !file.IsExtra
                && SubtitleAssociationResolver.IsVideo(file.Input.Name)
                && !IsExplicitExtra(file.Input.Name))
            {
                return new MetadataFailure(
                    MetadataFailureKind.Protocol,
                    "ai_u2_main_video_unmatched",
                    TmdbAccessConfirmed: true);
            }
        }

        return null;
    }

    public static bool IsExplicitExtra(string relativePath) =>
        string.Equals(
            U2FileEpisodeCandidateResolver.Resolve(relativePath).Reason,
            "non_feature_episode",
            StringComparison.Ordinal)
        || ContainsMovieHint(relativePath);

    private static bool ContainsMovieHint(string relativePath) =>
        relativePath.Contains("劇場版", StringComparison.OrdinalIgnoreCase)
        || relativePath.Contains("剧场版", StringComparison.OrdinalIgnoreCase)
        || relativePath.Contains("movie", StringComparison.OrdinalIgnoreCase);
}
