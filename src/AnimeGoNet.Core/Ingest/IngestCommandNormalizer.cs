using System.Globalization;
using AnimeGo.Plugin.Abstractions;
using AnimeGoNet.Core.Compatibility;
using AnimeGoNet.Core.Plugins;

namespace AnimeGoNet.Core.Ingest;

public static class IngestCommandNormalizer
{
    private static readonly PluginCatalog DefaultPlugins = BuiltInPluginCatalog.Create();

    public static IngestValidationResult Normalize(
        string source,
        IngestItemCommand command,
        bool requireModernMetadata = true)
    {
        var operation = NormalizeAsync(
            DefaultPlugins,
            source,
            command,
            requireModernMetadata,
            CancellationToken.None);
        if (!operation.IsCompletedSuccessfully)
        {
            throw new InvalidOperationException(
                "Built-in source adapters must complete normalization synchronously.");
        }

        return operation.Result;
    }

    public static async ValueTask<IngestValidationResult> NormalizeAsync(
        PluginCatalog plugins,
        string source,
        IngestItemCommand command,
        bool requireModernMetadata = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plugins);
        ArgumentNullException.ThrowIfNull(command);

        var normalizedSource = (source ?? string.Empty).Trim().ToLowerInvariant();
        var adapter = plugins.Find<IInputSourceAdapter>(normalizedSource);
        if (adapter is null)
        {
            return new IngestValidationResult(
                null,
                [$"source adapter '{normalizedSource}' is not registered"]);
        }

        var result = await adapter.NormalizeAsync(
            new SourceIngestContext(
                normalizedSource,
                command.TorrentUrl,
                command.Info.Title,
                command.Info.LegacyName,
                command.Info.SourceItemId,
                command.Info.SourceWorkId,
                command.Info.MikanUrl,
                command.Info.LegacyUrl,
                command.Info.MikanId,
                command.Info.BangumiId,
                command.Info.AniDbId,
                command.Info.ImdbId,
                command.SourceEvidence?.PublishedAtRaw,
                command.SourceEvidence?.PublishedAt,
                requireModernMetadata,
                command.Info.GroupId),
            cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded || result.Item is null)
        {
            return new IngestValidationResult(
                null,
                result.Errors.Select(error => error.Message).ToArray());
        }

        return ValidatePluginOutput(
            normalizedSource,
            result.Item,
            command.Info.MikanUrl ?? command.Info.LegacyUrl);
    }

    internal static IngestValidationResult NormalizeKnownSource(
        string source,
        IngestItemCommand command,
        bool requireModernMetadata)
    {
        var errors = new List<string>();
        var normalizedSource = (source ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedSource is not ("mikan" or "u2" or "ttg"))
        {
            errors.Add($"source adapter '{normalizedSource}' is not registered");
        }

        if (!TryResolveAlias(command.Info.Title, command.Info.LegacyName, "title", "name", out var title))
        {
            errors.Add("info.title and info.name must match when both are supplied");
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            errors.Add("info.title is required");
        }

        if (!Uri.TryCreate(command.TorrentUrl, UriKind.Absolute, out var torrentUrl)
            || (torrentUrl.Scheme != Uri.UriSchemeHttp && torrentUrl.Scheme != Uri.UriSchemeHttps))
        {
            errors.Add("torrent must be an absolute HTTP(S) URL");
        }

        _ = TryResolveAlias(command.Info.MikanUrl, command.Info.LegacyUrl, "mikan_url", "url", out var mikanUrl);
        if (command.Info.MikanUrl is not null
            && command.Info.LegacyUrl is not null
            && !string.Equals(command.Info.MikanUrl.Trim(), command.Info.LegacyUrl.Trim(), StringComparison.Ordinal))
        {
            errors.Add("info.mikan_url and info.url must match when both are supplied");
        }

        var workMikanId = ParseMikanId(command.Info.SourceWorkId);
        var urlMikanId = ParseMikanIdFromUrl(mikanUrl);
        var mikanIds = new[] { command.Info.MikanId, workMikanId, urlMikanId }
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .Distinct()
            .ToArray();
        if (mikanIds.Length > 1)
        {
            errors.Add("mikanid, source_work_id and mikan_url must identify the same work");
        }

        var mikanId = command.Info.MikanId ?? workMikanId ?? urlMikanId;
        var sourceWorkId = string.IsNullOrWhiteSpace(command.Info.SourceWorkId)
            ? mikanId?.ToString(CultureInfo.InvariantCulture)
            : command.Info.SourceWorkId.Trim();
        var imdbId = NormalizeImdbId(command.Info.ImdbId, errors);
        var publishedAtRaw = NullIfWhiteSpace(command.SourceEvidence?.PublishedAtRaw);
        var publishedAt = command.SourceEvidence?.PublishedAt;
        if (command.SourceEvidence is not null
            && (normalizedSource != "mikan"
                || publishedAtRaw is null))
        {
            errors.Add("source publication evidence requires a Mikan raw timestamp");
        }

        switch (normalizedSource)
        {
            case "mikan":
                if (!string.IsNullOrWhiteSpace(command.Info.SourceWorkId) && workMikanId is null)
                {
                    errors.Add("mikan source_work_id must be a positive decimal mikanid");
                }

                if (mikanId is null or <= 0)
                {
                    errors.Add("mikan source requires a positive mikanid or resolvable source_work_id/mikan_url");
                }

                if (command.Info.GroupId is not null and <= 0)
                {
                    errors.Add("mikan groupid must be positive when supplied");
                }

                if (requireModernMetadata && command.Info.BangumiId is null or <= 0)
                {
                    errors.Add("mikan source requires a positive bgmid");
                }
                break;
            case "u2" when command.Info.AniDbId is <= 0:
                errors.Add("anidbid must be positive when supplied");
                break;
        }

        if (errors.Count > 0 || torrentUrl is null)
        {
            return new IngestValidationResult(null, errors);
        }

        var fingerprint = StableHash.Sha256LowerHex(torrentUrl.AbsoluteUri);
        return new IngestValidationResult(
            new NormalizedIngestItem(
                normalizedSource,
                torrentUrl,
                fingerprint,
                title!.Trim(),
                NullIfWhiteSpace(command.Info.SourceItemId),
                NullIfWhiteSpace(sourceWorkId),
                mikanId,
                command.Info.BangumiId,
                command.Info.AniDbId,
                imdbId,
                publishedAtRaw,
                publishedAt,
                NormalizeSafeSourcePageUrl(mikanUrl),
                command.Info.GroupId),
            []);
    }

    private static string? NormalizeSafeSourcePageUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || !uri.AbsolutePath.StartsWith("/Home/Episode/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return uri.GetLeftPart(UriPartial.Path);
    }

    private static IngestValidationResult ValidatePluginOutput(
        string requestedSource,
        SourceNormalizedItem item,
        string? sourcePageUrl)
    {
        var errors = new List<string>();
        if (!string.Equals(item.Source, requestedSource, StringComparison.Ordinal))
        {
            errors.Add("source adapter returned a different source id");
        }

        if (!Uri.TryCreate(item.TorrentUrl, UriKind.Absolute, out var torrentUrl)
            || (torrentUrl.Scheme != Uri.UriSchemeHttp && torrentUrl.Scheme != Uri.UriSchemeHttps))
        {
            errors.Add("source adapter returned an invalid torrent URL");
        }

        if (string.IsNullOrWhiteSpace(item.Title))
        {
            errors.Add("source adapter returned an empty title");
        }

        if (item.TorrentUrlFingerprint is null
            || item.TorrentUrlFingerprint.Length != 64
            || !item.TorrentUrlFingerprint.All(IsLowerHexCharacter))
        {
            errors.Add("source adapter returned an invalid torrent URL fingerprint");
        }

        return errors.Count > 0 || torrentUrl is null
            ? new IngestValidationResult(null, errors)
            : new IngestValidationResult(
                new NormalizedIngestItem(
                    item.Source,
                    torrentUrl,
                    item.TorrentUrlFingerprint!,
                    item.Title.Trim(),
                    item.SourceItemId,
                    item.SourceWorkId,
                    item.MikanId,
                    item.BangumiId,
                    item.AniDbId,
                    item.ImdbId,
                    item.PublishedAtRaw,
                    item.PublishedAt,
                    SourcePageUrl: NormalizeSafeSourcePageUrl(sourcePageUrl),
                    GroupId: item.GroupId),
                []);
    }

    private static bool TryResolveAlias(string? primary, string? alias, string primaryName, string aliasName, out string? value)
    {
        _ = primaryName;
        _ = aliasName;
        var normalizedPrimary = NullIfWhiteSpace(primary);
        var normalizedAlias = NullIfWhiteSpace(alias);
        value = normalizedPrimary ?? normalizedAlias;
        return normalizedPrimary is null
            || normalizedAlias is null
            || string.Equals(normalizedPrimary, normalizedAlias, StringComparison.Ordinal);
    }

    private static int? ParseMikanId(string? value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 ? id : null;

    private static int? ParseMikanIdFromUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index + 2 < segments.Length; index++)
        {
            if (string.Equals(segments[index], "home", StringComparison.OrdinalIgnoreCase)
                && string.Equals(segments[index + 1], "bangumi", StringComparison.OrdinalIgnoreCase))
            {
                return ParseMikanId(segments[index + 2]);
            }
        }

        return null;
    }

    private static string? NormalizeImdbId(string? value, List<string> errors)
    {
        var normalized = NullIfWhiteSpace(value)?.ToLowerInvariant();
        if (normalized is null)
        {
            return null;
        }

        if (normalized.Length <= 2
            || !normalized.StartsWith("tt", StringComparison.Ordinal)
            || normalized.AsSpan(2).ContainsAnyExceptInRange('0', '9'))
        {
            errors.Add("imdbid must be a title ID in the form tt followed by digits");
        }

        return normalized;
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsLowerHexCharacter(char value) =>
        value is >= '0' and <= '9' or >= 'a' and <= 'f';
}
