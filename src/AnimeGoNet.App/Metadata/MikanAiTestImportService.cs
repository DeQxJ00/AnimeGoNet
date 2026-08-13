using System.Globalization;
using System.Text.RegularExpressions;
using AnimeGoNet.App.Configuration;
using AnimeGoNet.App.Feeds;
using AnimeGoNet.App.Torrents;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Feeds;
using AnimeGoNet.Core.Library;
using AnimeGoNet.Core.Metadata;
using AnimeGoNet.Data.Sources;

namespace AnimeGoNet.App.Metadata;

public sealed record MikanAiTestImportResult(
    string Title,
    int MikanId,
    int GroupId,
    int? BangumiSubjectId,
    DateTimeOffset? PublishedAt,
    int TorrentFileCount,
    IReadOnlyList<AiMetadataFileInput> VideoFiles);

public sealed record MikanEpisodeResolveResult(
    string Title,
    Uri EpisodeUrl,
    Uri TorrentUrl,
    int MikanId,
    int GroupId,
    int? BangumiSubjectId,
    DateTimeOffset? PublishedAt);

public sealed class MikanAiTestImportException(string code, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public string Code { get; } = code;
}

public sealed partial class MikanAiTestImportService(
    IRssFeedHttpClient httpClient,
    SourceProfileStore profiles,
    ITorrentStagingService staging,
    AnimeGoOptions options,
    MikanEpisodeIdentityCache identityCache,
    MikanBangumiIdentityCache bangumiIdentityCache)
{
    public async Task<MikanAiTestImportResult> ImportAsync(
        string episodeUrl,
        CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveAsync(episodeUrl, "mikan", cancellationToken).ConfigureAwait(false);
        var profile = await profiles.GetEnabledAsync("mikan", cancellationToken).ConfigureAwait(false)
            ?? throw Error("ai_test_mikan_profile_missing", "Enabled Mikan source profile was not found.");

        var allowedHosts = profile.AllowedTorrentHosts
            .Append(resolved.TorrentUrl.IdnHost)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var trustedPrivateHosts = string.Equals(
                resolved.TorrentUrl.IdnHost,
                options.Metadata.Mikan.BaseUrl.IdnHost,
                StringComparison.OrdinalIgnoreCase)
            ? new[] { resolved.TorrentUrl.IdnHost }
            : [];
        try
        {
            await using var staged = await staging.StageAsync(
                resolved.TorrentUrl,
                new TorrentSourcePolicy(
                    profile.Id,
                    allowedHosts,
                    profile.MikanIdentityCookie,
                    trustedPrivateHosts),
                cancellationToken).ConfigureAwait(false);
            var videos = staged.Metadata.Files
                .Where(file => !file.IsPadding && SubtitleAssociationResolver.IsVideo(file.RelativePath))
                .Select(file => new AiMetadataFileInput(file.RelativePath, file.Size))
                .ToArray();
            if (videos.Length == 0)
            {
                throw Error("ai_test_mikan_video_missing", "Torrent contains no supported video files.");
            }

            return new MikanAiTestImportResult(
                resolved.Title,
                resolved.MikanId,
                resolved.GroupId,
                resolved.BangumiSubjectId,
                resolved.PublishedAt,
                staged.Metadata.Files.Count,
                videos);
        }
        catch (TorrentStagingException exception)
        {
            throw Error(
                $"ai_test_mikan_torrent_{exception.Code.ToString().ToLowerInvariant()}",
                "Mikan torrent could not be downloaded or parsed.",
                exception);
        }
    }

    public async Task<MikanEpisodeResolveResult> ResolveAsync(
        string episodeUrl,
        string sourceProfileId,
        CancellationToken cancellationToken = default)
    {
        var episodeUri = ValidateEpisodeUri(episodeUrl, options.Metadata.Mikan.BaseUrl);
        var normalizedProfileId = sourceProfileId?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedProfileId))
        {
            throw Error("mikan_episode_source_profile_required", "Mikan source profile is required.");
        }
        var profile = await profiles.GetEnabledAsync(normalizedProfileId, cancellationToken).ConfigureAwait(false)
            ?? throw Error("mikan_episode_source_profile_missing", "Enabled Mikan source profile was not found.");
        if (!string.Equals(profile.Adapter, "mikan", StringComparison.OrdinalIgnoreCase))
        {
            throw Error("mikan_episode_source_profile_invalid", "Source profile must use the Mikan adapter.");
        }

        var identity = await identityCache.GetAsync(episodeUri, cancellationToken)
            .ConfigureAwait(false);
        if (identity is null)
        {
            ReadOnlyMemory<byte> episodeHtml;
            try
            {
                episodeHtml = await FetchAsync(episodeUri, profile.Id, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is RssFeedException or HttpRequestException)
            {
                throw Error("ai_test_mikan_episode_fetch_failed", "Mikan Episode page could not be fetched.", exception);
            }

            try
            {
                identity = MikanEpisodeIdentityParser.Parse(episodeHtml);
                if (identity.SubGroupId > 0)
                {
                    await identityCache.PutAsync(episodeUri, identity, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch (MikanEpisodeIdentityException exception)
            {
                throw Error(exception.Code, exception.Message, exception);
            }
        }
        if (identity.SubGroupId <= 0)
        {
            throw Error("mikan_identity_group_invalid", "Mikan Episode page has no valid groupid.");
        }

        var rssUri = new Uri(
            options.Metadata.Mikan.BaseUrl,
            $"/RSS/Bangumi?bangumiId={identity.MikanId.ToString(CultureInfo.InvariantCulture)}"
            + $"&subgroupid={identity.SubGroupId.ToString(CultureInfo.InvariantCulture)}");
        RssFeedDocument feed;
        try
        {
            var rss = await FetchAsync(rssUri, profile.Id, cancellationToken).ConfigureAwait(false);
            feed = RssFeedParser.Parse(rss, rssUri.AbsoluteUri);
        }
        catch (Exception exception) when (exception is RssFeedException or HttpRequestException)
        {
            throw Error("ai_test_mikan_rss_fetch_failed", "Mikan group RSS could not be read.", exception);
        }

        var matchingItems = feed.Items
            .Where(candidate => IsSameEpisode(candidate.MikanUrl, episodeUri))
            .ToArray();
        if (matchingItems.Length != 1)
        {
            throw Error("ai_test_mikan_rss_item_missing", "The Episode URL was not found exactly once in its Mikan RSS feed.");
        }
        var item = matchingItems[0];
        if (string.IsNullOrWhiteSpace(item.Title) || item.Title.Length > 1000)
        {
            throw Error("ai_test_mikan_title_invalid", "Mikan RSS title is empty or too long.");
        }
        if (!Uri.TryCreate(item.TorrentUrl, UriKind.Absolute, out var torrentUri))
        {
            throw Error("ai_test_mikan_torrent_url_invalid", "Mikan RSS torrent URL is invalid.");
        }

        var bgmid = await bangumiIdentityCache.GetAsync(
            identity.MikanId,
            cancellationToken).ConfigureAwait(false);
        if (bgmid is null)
        {
            var workUri = new Uri(
                options.Metadata.Mikan.BaseUrl,
                $"/Home/Bangumi/{identity.MikanId.ToString(CultureInfo.InvariantCulture)}");
            try
            {
                var workHtml = await FetchAsync(workUri, profile.Id, cancellationToken).ConfigureAwait(false);
                bgmid = MikanBangumiSubjectParser.Parse(workHtml);
                await bangumiIdentityCache.PutAsync(
                    identity.MikanId,
                    bgmid.Value,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (MikanBangumiSubjectException exception) when (
                exception.Code == "mikan_bgmid_link_missing")
            {
            }
            catch (Exception exception) when (
                exception is MikanBangumiSubjectException or RssFeedException or HttpRequestException)
            {
                throw Error("ai_test_mikan_bgmid_fetch_failed", "Mikan work page could not be resolved safely.", exception);
            }
        }

        return new MikanEpisodeResolveResult(
            item.Title.Trim(),
            episodeUri,
            MikanEndpointRewriter.Rewrite(torrentUri, options.Metadata.Mikan),
            identity.MikanId,
            identity.SubGroupId,
            bgmid,
            MikanPublishedAtParser.Parse(item.PublishedDate));
    }

    private Task<ReadOnlyMemory<byte>> FetchAsync(
        Uri uri,
        string sourceProfileId,
        CancellationToken cancellationToken) =>
        httpClient is ISourceProfileRssFeedHttpClient profileClient
            ? profileClient.GetAsync(uri, sourceProfileId, cancellationToken)
            : httpClient.GetAsync(uri, cancellationToken);

    public static Uri ValidateEpisodeUri(string value, Uri configuredBaseUrl)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || !EpisodePath().IsMatch(uri.AbsolutePath)
            || !IsAllowedOrigin(uri, configuredBaseUrl))
        {
            throw Error(
                "ai_test_mikan_episode_url_invalid",
                "Mikan Episode URL must use a supported host and /Home/Episode/<40-hex-id> path.");
        }
        return uri;
    }

    private static bool IsAllowedOrigin(Uri uri, Uri configuredBaseUrl)
    {
        if (string.Equals(uri.IdnHost, configuredBaseUrl.IdnHost, StringComparison.OrdinalIgnoreCase)
            && uri.Port == configuredBaseUrl.Port
            && uri.Scheme == configuredBaseUrl.Scheme)
        {
            return true;
        }
        return uri.Scheme == Uri.UriSchemeHttps
            && uri.IsDefaultPort
            && uri.IdnHost is "mikanime.tv" or "www.mikanime.tv" or "mikanani.me" or "www.mikanani.me";
    }

    private static bool IsSameEpisode(string candidate, Uri expected)
    {
        return Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            && string.Equals(
                uri.AbsolutePath.TrimEnd('/'),
                expected.AbsolutePath.TrimEnd('/'),
                StringComparison.OrdinalIgnoreCase);
    }

    private static MikanAiTestImportException Error(
        string code,
        string message,
        Exception? inner = null) => new(code, message, inner);

    [GeneratedRegex("^/Home/Episode/[0-9a-fA-F]{40}/?$", RegexOptions.CultureInvariant)]
    private static partial Regex EpisodePath();
}
