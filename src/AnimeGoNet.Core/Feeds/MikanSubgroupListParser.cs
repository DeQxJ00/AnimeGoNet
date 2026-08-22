using System.Globalization;
using System.Net;
using System.Text;
using AnimeGoNet.Core.Diagnostics;

namespace AnimeGoNet.Core.Feeds;

public sealed record MikanSubgroup(int GroupId, string Name);

public sealed class MikanSubgroupListException(
    string code,
    string message,
    Exception? innerException = null) : FormatException(message, innerException), IStableError
{
    public string Code { get; } = StableErrorCode.Require(code, nameof(code));

    public StableErrorSemantic Semantics => StableErrorSemantic.ParseFailed;
}

public static class MikanSubgroupListParser
{
    public const int MaximumBytes = 2 * 1024 * 1024;

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static IReadOnlyList<MikanSubgroup> Parse(ReadOnlyMemory<byte> html)
    {
        if (html.IsEmpty)
        {
            throw Error("mikan_subgroups_html_empty", "Mikan work HTML is empty.");
        }
        if (html.Length > MaximumBytes)
        {
            throw Error("mikan_subgroups_html_too_large", "Mikan work HTML exceeds the size limit.");
        }

        string text;
        try
        {
            text = StrictUtf8.GetString(html.Span);
        }
        catch (DecoderFallbackException exception)
        {
            throw Error("mikan_subgroups_html_invalid", "Mikan work HTML is not valid UTF-8.", exception);
        }

        var groups = new List<MikanSubgroup>();
        var seen = new Dictionary<int, string>();
        var offset = 0;
        while (TryFindTag(text, "a", offset, out var start, out var end))
        {
            var tag = text.AsSpan(start, end - start + 1);
            offset = end + 1;
            if (!TryReadAttribute(tag, "class", out var classes)
                || !HasClass(classes, "subgroup-name"))
            {
                continue;
            }

            var anchorId = TryReadAttribute(tag, "data-anchor", out var anchor)
                ? ParseAnchorId(WebUtility.HtmlDecode(anchor))
                : null;
            var classId = ParseClassId(classes);
            if (anchorId is not null && classId is not null && anchorId != classId)
            {
                throw Error(
                    "mikan_subgroups_id_conflict",
                    "Mikan subgroup entry contains conflicting IDs.");
            }
            var groupId = anchorId ?? classId;
            if (groupId is null)
            {
                continue;
            }

            var close = text.IndexOf("</a", offset, StringComparison.OrdinalIgnoreCase);
            if (close < 0)
            {
                throw Error("mikan_subgroups_html_invalid", "Mikan work HTML contains an unterminated subgroup link.");
            }
            var name = DecodeText(text.AsSpan(offset, close - offset));
            if (string.IsNullOrWhiteSpace(name))
            {
                name = $"groupid {groupId.Value.ToString(CultureInfo.InvariantCulture)}";
            }

            if (seen.TryGetValue(groupId.Value, out var existing))
            {
                if (!string.Equals(existing, name, StringComparison.Ordinal))
                {
                    throw Error(
                        "mikan_subgroups_name_conflict",
                        "Mikan work HTML contains conflicting names for one subgroup ID.");
                }
                continue;
            }
            seen.Add(groupId.Value, name);
            groups.Add(new MikanSubgroup(groupId.Value, name));
            offset = close + 3;
        }

        return groups.Count > 0
            ? groups
            : throw Error("mikan_subgroups_missing", "No subgroup entries were found on the Mikan work page.");
    }

    private static int? ParseAnchorId(string value)
    {
        var candidate = value.Trim();
        if (candidate.StartsWith('#')) candidate = candidate[1..];
        return int.TryParse(candidate, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            && parsed > 0
                ? parsed
                : null;
    }

    private static int? ParseClassId(string classes)
    {
        foreach (var value in classes.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            const string prefix = "subgroup-";
            if (value.StartsWith(prefix, StringComparison.Ordinal)
                && int.TryParse(value.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
                && parsed > 0)
            {
                return parsed;
            }
        }
        return null;
    }

    private static string DecodeText(ReadOnlySpan<char> value)
    {
        var output = new StringBuilder(value.Length);
        var insideTag = false;
        foreach (var current in value)
        {
            if (current == '<')
            {
                insideTag = true;
            }
            else if (current == '>')
            {
                insideTag = false;
            }
            else if (!insideTag)
            {
                output.Append(current);
            }
        }
        return WebUtility.HtmlDecode(output.ToString()).Trim();
    }

    private static bool TryFindTag(string text, string tagName, int offset, out int start, out int end)
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
                throw Error("mikan_subgroups_html_invalid", "Mikan work HTML contains an unterminated tag.");
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
            if (index >= tag.Length || tag[index] != '=') continue;
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

    private static MikanSubgroupListException Error(
        string code,
        string message,
        Exception? inner = null) => new(code, message, inner);
}
