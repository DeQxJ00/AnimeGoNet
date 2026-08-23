namespace AnimeGoNet.Core.Media;

public static class MediaTypes
{
    public const string Tv = "tv";

    public const string Movie = "movie";

    public static bool TryNormalize(string? value, out string mediaType)
    {
        mediaType = string.IsNullOrWhiteSpace(value)
            ? Tv
            : value.Trim().ToLowerInvariant();
        return mediaType is Tv or Movie;
    }
}
