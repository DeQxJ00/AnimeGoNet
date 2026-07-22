using System.Globalization;
using System.Net;
using System.Text;

namespace AnimeGoNet.Core.Feeds;

public sealed record MikanEpisodeIdentity(int MikanId, int SubGroupId);

public sealed class MikanEpisodeIdentityException(string code, string message, Exception? innerException = null)
    : FormatException(message, innerException)
{
    public string Code { get; } = code;
}

public static class MikanEpisodeIdentityParser
{
    public const int MaximumBytes = 2 * 1024 * 1024;

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static MikanEpisodeIdentity Parse(ReadOnlyMemory<byte> html)
    {
        if (html.IsEmpty)
        {
            throw Error("mikan_identity_html_empty", "Mikan episode HTML is empty.");
        }
        if (html.Length > MaximumBytes)
        {
            throw Error("mikan_identity_html_too_large", "Mikan episode HTML exceeds the size limit.");
        }

        string text;
        try
        {
            text = StrictUtf8.GetString(html.Span);
        }
        catch (DecoderFallbackException exception)
        {
            throw Error("mikan_identity_html_invalid", "Mikan episode HTML is not valid UTF-8.", exception);
        }

        var offset = 0;
        while (TryFindAnchor(text, offset, out var start, out var end))
        {
            var tag = text.AsSpan(start, end - start + 1);
            if (TryReadAttribute(tag, "class", out var classes)
                && HasClass(classes, "mikan-rss"))
            {
                if (!TryReadAttribute(tag, "href", out var href))
                {
                    throw Error("mikan_identity_href_missing", "Mikan RSS link has no href attribute.");
                }
                return ParseHref(WebUtility.HtmlDecode(href));
            }
            offset = end + 1;
        }

        throw Error("mikan_identity_link_missing", "Mikan RSS identity link was not found.");
    }

    private static MikanEpisodeIdentity ParseHref(string href)
    {
        var queryStart = href.IndexOf('?');
        if (queryStart < 0)
        {
            throw Error("mikan_identity_id_missing", "Mikan RSS link has no bangumiId.");
        }
        var fragment = href.IndexOf('#', queryStart + 1);
        var query = fragment < 0
            ? href.AsSpan(queryStart + 1)
            : href.AsSpan(queryStart + 1, fragment - queryStart - 1);
        var mikanValue = ReadQueryValue(query, "bangumiId");
        if (!int.TryParse(mikanValue, NumberStyles.None, CultureInfo.InvariantCulture, out var mikanId)
            || mikanId <= 0)
        {
            throw Error("mikan_identity_id_invalid", "Mikan RSS bangumiId is missing or invalid.");
        }
        var groupValue = ReadQueryValue(query, "subgroupid");
        var groupId = int.TryParse(groupValue, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedGroup)
            && parsedGroup > 0
            ? parsedGroup
            : 0;
        return new MikanEpisodeIdentity(mikanId, groupId);
    }

    private static string? ReadQueryValue(ReadOnlySpan<char> query, string name)
    {
        while (!query.IsEmpty)
        {
            var separator = query.IndexOf('&');
            var pair = separator < 0 ? query : query[..separator];
            var equals = pair.IndexOf('=');
            var key = equals < 0 ? pair : pair[..equals];
            if (key.Equals(name, StringComparison.Ordinal))
            {
                var value = equals < 0 ? ReadOnlySpan<char>.Empty : pair[(equals + 1)..];
                return Uri.UnescapeDataString(value.ToString().Replace('+', ' '));
            }
            if (separator < 0) break;
            query = query[(separator + 1)..];
        }
        return null;
    }

    private static bool TryFindAnchor(string text, int offset, out int start, out int end)
    {
        while (offset < text.Length)
        {
            start = text.IndexOf("<a", offset, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                end = -1;
                return false;
            }
            var afterName = start + 2;
            if (afterName < text.Length
                && !char.IsWhiteSpace(text[afterName])
                && text[afterName] is not ('>' or '/'))
            {
                offset = afterName;
                continue;
            }
            end = FindTagEnd(text, afterName);
            if (end < 0)
            {
                throw Error("mikan_identity_html_invalid", "Mikan episode HTML contains an unterminated anchor.");
            }
            return true;
        }
        start = -1;
        end = -1;
        return false;
    }

    private static int FindTagEnd(string text, int offset)
    {
        var quote = '\0';
        for (var index = offset; index < text.Length; index++)
        {
            var current = text[index];
            if (quote != '\0')
            {
                if (current == quote) quote = '\0';
                continue;
            }
            if (current is '\'' or '"') quote = current;
            else if (current == '>') return index;
        }
        return -1;
    }

    private static bool TryReadAttribute(ReadOnlySpan<char> tag, string name, out string value)
    {
        var index = 2;
        while (index < tag.Length)
        {
            while (index < tag.Length && char.IsWhiteSpace(tag[index])) index++;
            if (index >= tag.Length || tag[index] is '>' or '/') break;
            var nameStart = index;
            while (index < tag.Length
                && !char.IsWhiteSpace(tag[index])
                && tag[index] is not ('=' or '>' or '/')) index++;
            var attributeName = tag[nameStart..index];
            while (index < tag.Length && char.IsWhiteSpace(tag[index])) index++;
            if (index >= tag.Length || tag[index] != '=')
            {
                continue;
            }
            index++;
            while (index < tag.Length && char.IsWhiteSpace(tag[index])) index++;
            if (index >= tag.Length) break;
            ReadOnlySpan<char> attributeValue;
            if (tag[index] is '\'' or '"')
            {
                var quote = tag[index++];
                var valueStart = index;
                while (index < tag.Length && tag[index] != quote) index++;
                if (index >= tag.Length) break;
                attributeValue = tag[valueStart..index++];
            }
            else
            {
                var valueStart = index;
                while (index < tag.Length && !char.IsWhiteSpace(tag[index]) && tag[index] != '>') index++;
                attributeValue = tag[valueStart..index];
            }
            if (attributeName.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = attributeValue.ToString();
                return true;
            }
        }
        value = string.Empty;
        return false;
    }

    private static bool HasClass(string classes, string expected) =>
        classes.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Contains(expected, StringComparer.Ordinal);

    private static MikanEpisodeIdentityException Error(string code, string message, Exception? inner = null) =>
        new(code, message, inner);
}
