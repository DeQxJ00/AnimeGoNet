using AnimeGoNet.Core.Configuration;

namespace AnimeGoNet.App.Configuration;

public static class MikanEndpointRewriter
{
    public static Uri Rewrite(Uri source, MikanClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        if (!source.IsAbsoluteUri
            || source.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(source.UserInfo)
            || !string.IsNullOrEmpty(source.Fragment))
        {
            throw new ArgumentException(
                "Mikan URL must be an absolute HTTP(S) URL without userinfo or fragment.",
                nameof(source));
        }

        var target = options.BaseUrl;
        return new UriBuilder(source)
        {
            Scheme = target.Scheme,
            Host = target.IdnHost,
            Port = target.IsDefaultPort ? -1 : target.Port,
        }.Uri;
    }
}
