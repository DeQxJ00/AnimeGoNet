using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using AnimeGoNet.App.Feeds;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Feeds;
using AnimeGoNet.Data.Mikan;

namespace AnimeGoNet.App.Metadata;

public sealed class MikanPublishGroupNameException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public static partial class MikanPublishGroupNameParser
{
    private const int MaximumBytes = 5 * 1024 * 1024;

    public static string Parse(ReadOnlyMemory<byte> html)
    {
        if (html.IsEmpty || html.Length > MaximumBytes)
            throw new MikanPublishGroupNameException("mikan_publish_group_page_invalid", "PublishGroup page is empty or too large.");
        string text;
        try
        {
            text = new UTF8Encoding(false, true).GetString(html.Span);
        }
        catch (DecoderFallbackException)
        {
            throw new MikanPublishGroupNameException("mikan_publish_group_encoding_invalid", "PublishGroup page is not valid UTF-8.");
        }

        var names = TextEndingWithDirectorySuffix().Matches(text)
            .Select(match => Normalize(match.Groups["name"].Value))
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (names.Length == 0)
            throw new MikanPublishGroupNameException("mikan_publish_group_name_missing", "PublishGroup page has no group directory name.");
        return names.OrderBy(value => value.Length).First();
    }

    private static string Normalize(string value)
    {
        var decoded = WebUtility.HtmlDecode(Tag().Replace(value, string.Empty));
        var suffix = decoded.LastIndexOf("作品年表", StringComparison.Ordinal);
        if (suffix >= 0) decoded = decoded[..suffix];
        var separator = Math.Max(decoded.LastIndexOf(" - ", StringComparison.Ordinal), decoded.LastIndexOf(" | ", StringComparison.Ordinal));
        if (separator >= 0) decoded = decoded[(separator + 3)..];
        return string.Join(' ', decoded.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
    }

    [GeneratedRegex("(?<name>[^<>\\\"']{1,200}作品年表)", RegexOptions.CultureInvariant)]
    private static partial Regex TextEndingWithDirectorySuffix();

    [GeneratedRegex("<[^>]+>", RegexOptions.CultureInvariant)]
    private static partial Regex Tag();
}

public sealed class MikanPublishGroupResolver(
    IRssFeedHttpClient httpClient,
    MikanPublishGroupStore store,
    AnimeGoOptions options,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<bool> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        var candidate = await store.FindNextCandidateAsync(now, cancellationToken).ConfigureAwait(false);
        if (candidate is null) return false;
        var uri = new Uri(options.Metadata.Mikan.BaseUrl, $"Home/PublishGroup/{candidate.GroupId}");
        try
        {
            var html = httpClient is ISourceProfileRssFeedHttpClient profileClient
                ? await profileClient.GetAsync(uri, candidate.SourceProfileId, cancellationToken).ConfigureAwait(false)
                : await httpClient.GetAsync(uri, cancellationToken).ConfigureAwait(false);
            var name = MikanPublishGroupNameParser.Parse(html);
            await store.SaveAutomaticAsync(
                candidate.GroupId, name, candidate.SourceProfileId, now, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (MikanPublishGroupNameException exception)
        {
            await store.SaveFailureAsync(
                candidate.GroupId, candidate.SourceProfileId, exception.Code, now, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is RssFeedException or HttpRequestException)
        {
            await store.SaveFailureAsync(
                candidate.GroupId, candidate.SourceProfileId, "mikan_publish_group_fetch_failed", now, cancellationToken).ConfigureAwait(false);
        }
        return true;
    }
}

public sealed class MikanPublishGroupWorker(MikanPublishGroupResolver resolver) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await resolver.RunOnceAsync(stoppingToken).ConfigureAwait(false))
                {
                    await Task.Yield();
                    continue;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                // A failed row is retried from persisted state; keep the worker alive.
            }
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ConfigureAwait(false);
        }
    }
}
