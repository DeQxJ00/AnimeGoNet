using System.Globalization;
using System.Net;
using System.Text;
using AnimeGoNet.Core.Diagnostics;

namespace AnimeGoNet.Core.Feeds;

public sealed class MikanBangumiSubjectException(
    string code,
    string message,
    Exception? innerException = null) : FormatException(message, innerException)
{
    public string Code { get; } = StableErrorCode.Require(code, nameof(code));
}

public static class MikanBangumiSubjectParser
{
    public const int MaximumBytes = 2 * 1024 * 1024;

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static int Parse(ReadOnlyMemory<byte> html)
    {
        if (html.IsEmpty)
        {
            throw Error("mikan_bgmid_html_empty", "Mikan work HTML is empty.");
        }
        if (html.Length > MaximumBytes)
        {
            throw Error("mikan_bgmid_html_too_large", "Mikan work HTML exceeds the size limit.");
        }

        string text;
        try
        {
            text = StrictUtf8.GetString(html.Span);
        }
        catch (DecoderFallbackException exception)
        {
            throw Error("mikan_bgmid_html_invalid", "Mikan work HTML is not valid UTF-8.", exception);
        }

        int? subjectId = null;
        var offset = 0;
        while (TryFindTag(text, "p", offset, out var start, out var end))
        {
            var tag = text.AsSpan(start, end - start + 1);
            offset = end + 1;
            if (!TryReadAttribute(tag, "class", out var classes)
                || !HasClass(classes, "bangumi-info"))
            {
                continue;
            }

            var close = text.IndexOf("</p", offset, StringComparison.OrdinalIgnoreCase);
            if (close < 0)
            {
                throw Error("mikan_bgmid_html_invalid", "Mikan work HTML contains an unterminated info paragraph.");
            }

            var anchorOffset = offset;
            while (TryFindTag(text, "a", anchorOffset, out var anchorStart, out var anchorEnd)
                   && anchorStart < close)
            {
                anchorOffset = anchorEnd + 1;
                var anchor = text.AsSpan(anchorStart, anchorEnd - anchorStart + 1);
                if (!TryReadAttribute(anchor, "href", out var href)
                    || !TryParseSubjectHref(WebUtility.HtmlDecode(href), out var parsed))
                {
                    continue;
                }

                if (subjectId is not null && subjectId != parsed)
                {
                    throw Error(
                        "mikan_bgmid_link_ambiguous",
                        "Mikan work HTML contains conflicting Bangumi subject links.");
                }
                subjectId = parsed;
            }

            offset = close + 3;
        }

        return subjectId
            ?? throw Error("mikan_bgmid_link_missing", "Bangumi subject link was not found on the Mikan work page.");
    }

    private static bool TryParseSubjectHref(string href, out int subjectId)
    {
        subjectId = 0;
        if (!Uri.TryCreate(href, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !IsBangumiHost(uri.IdnHost))
        {
            return false;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 2
            && string.Equals(segments[0], "subject", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(segments[1], NumberStyles.None, CultureInfo.InvariantCulture, out subjectId)
            && subjectId > 0;
    }

    private static bool IsBangumiHost(string host) =>
        host.Equals("bgm.tv", StringComparison.OrdinalIgnoreCase)
        || host.Equals("www.bgm.tv", StringComparison.OrdinalIgnoreCase)
        || host.Equals("bangumi.tv", StringComparison.OrdinalIgnoreCase)
        || host.Equals("www.bangumi.tv", StringComparison.OrdinalIgnoreCase);

    private static bool TryFindTag(
        string text,
        string tagName,
        int offset,
        out int start,
        out int end)
    {
        var marker = $"<{tagName}";
        while (offset < text.Length)
        {
            start = text.IndexOf(marker, offset, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                end = -1;
                return false;
            }
            var afterName = start + marker.Length;
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
                throw Error("mikan_bgmid_html_invalid", "Mikan work HTML contains an unterminated tag.");
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
        while (index < tag.Length && !char.IsWhiteSpace(tag[index]) && tag[index] != '>') index++;
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

    private static MikanBangumiSubjectException Error(
        string code,
        string message,
        Exception? inner = null) => new(code, message, inner);
}
