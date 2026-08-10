using System.Text;

#pragma warning disable CA1859, CA1865 // Kept byte-for-byte behavior-compatible with the validated tester.

namespace AnimeGoNet.App.AiTesterCompat;

public static class TorrentFileImporter
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".avi", ".mov", ".m4v", ".ts", ".m2ts", ".webm"
    };

    public static IReadOnlyList<MatchFileInput> ImportFromFile(string path) => ImportDetailedFromFile(path).VideoFiles;

    public static TorrentImportResult ImportDetailedFromFile(string path) => ImportDetailed(File.ReadAllBytes(path));

    public static IReadOnlyList<MatchFileInput> Import(ReadOnlySpan<byte> bytes) => ImportDetailed(bytes).VideoFiles;

    public static TorrentImportResult ImportDetailed(ReadOnlySpan<byte> bytes)
    {
        BValue root = BencodeParser.Parse(bytes);
        BDictionary rootDict = root.AsDictionary("torrent root");
        BDictionary info = rootDict.GetDictionary("info");

        List<MatchFileInput> files = info.TryGetList("files", out BList? fileList)
            ? ImportMultiFile(fileList!)
            : ImportSingleFile(info);

        List<MatchFileInput> videos = files
            .Where(file => VideoExtensions.Contains(Path.GetExtension(file.Name)))
            .Select(file => file with
            {
                FileEpisodeCandidate = FileEpisodeCandidateResolver.Resolve(file.Name)
            })
            .ToList();

        if (videos.Count == 0)
        {
            throw new ArgumentException("torrent does not contain supported video files.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (MatchFileInput video in videos)
        {
            if (!seen.Add(video.Name))
            {
                throw new ArgumentException($"torrent contains duplicate path '{video.Name}'.");
            }
        }

        return new TorrentImportResult(videos, files.Count);
    }

    private static List<MatchFileInput> ImportSingleFile(BDictionary info)
    {
        string name = info.GetPreferredString("name.utf-8", "name");
        long length = info.GetNonNegativeLong("length");
        return [new MatchFileInput(NormalizeTorrentPath([name]), length)];
    }

    private static List<MatchFileInput> ImportMultiFile(BList files)
    {
        var result = new List<MatchFileInput>();
        foreach (BValue item in files.Items)
        {
            BDictionary file = item.AsDictionary("files item");
            long length = file.GetNonNegativeLong("length");
            IReadOnlyList<string> path = file.GetPreferredStringList("path.utf-8", "path");
            result.Add(new MatchFileInput(NormalizeTorrentPath(path), length));
        }

        return result;
    }

    private static string NormalizeTorrentPath(IReadOnlyList<string> segments)
    {
        if (segments.Count == 0)
        {
            throw new ArgumentException("torrent path must not be empty.");
        }

        var normalized = new List<string>(segments.Count);
        foreach (string segment in segments)
        {
            string value = segment.Trim().Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("torrent path segment must not be empty.");
            }

            if (value.StartsWith("/", StringComparison.Ordinal))
            {
                throw new ArgumentException("torrent path must be relative and must not contain '..'.");
            }

            foreach (string part in value.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                if (part is "." or ".." || Path.IsPathRooted(part) || part.Contains(':', StringComparison.Ordinal))
                {
                    throw new ArgumentException("torrent path must be relative and must not contain '..'.");
                }

                normalized.Add(part);
            }
        }

        if (normalized.Count == 0)
        {
            throw new ArgumentException("torrent path must not be empty.");
        }

        string path = string.Join('/', normalized);
        if (path.StartsWith("/", StringComparison.Ordinal) ||
            path.Contains("/../", StringComparison.Ordinal) ||
            path.StartsWith("../", StringComparison.Ordinal) ||
            path.EndsWith("/..", StringComparison.Ordinal))
        {
            throw new ArgumentException("torrent path must be relative and must not contain '..'.");
        }

        return path;
    }
}

internal abstract record BValue
{
    public BDictionary AsDictionary(string name) =>
        this as BDictionary ?? throw new ArgumentException($"{name} must be a dictionary.");
}

internal sealed record BInteger(long Value) : BValue;

internal sealed record BString(byte[] Bytes) : BValue
{
    public string Text => Encoding.UTF8.GetString(Bytes);
}

internal sealed record BList(IReadOnlyList<BValue> Items) : BValue;

internal sealed record BDictionary(IReadOnlyDictionary<string, BValue> Values) : BValue
{
    public BDictionary GetDictionary(string key)
    {
        if (!Values.TryGetValue(key, out BValue? value))
        {
            throw new ArgumentException($"torrent dictionary is missing '{key}'.");
        }

        return value.AsDictionary(key);
    }

    public bool TryGetList(string key, out BList? list)
    {
        if (Values.TryGetValue(key, out BValue? value) && value is BList typed)
        {
            list = typed;
            return true;
        }

        list = null;
        return false;
    }

    public long GetNonNegativeLong(string key)
    {
        if (!Values.TryGetValue(key, out BValue? value) || value is not BInteger integer)
        {
            throw new ArgumentException($"torrent dictionary requires integer '{key}'.");
        }

        if (integer.Value < 0)
        {
            throw new ArgumentException($"torrent integer '{key}' must be non-negative.");
        }

        return integer.Value;
    }

    public string GetPreferredString(string utf8Key, string fallbackKey)
    {
        if (Values.TryGetValue(utf8Key, out BValue? utf8) && utf8 is BString utf8String)
        {
            return utf8String.Text;
        }

        if (Values.TryGetValue(fallbackKey, out BValue? fallback) && fallback is BString fallbackString)
        {
            return fallbackString.Text;
        }

        throw new ArgumentException($"torrent dictionary requires '{utf8Key}' or '{fallbackKey}'.");
    }

    public IReadOnlyList<string> GetPreferredStringList(string utf8Key, string fallbackKey)
    {
        if (Values.TryGetValue(utf8Key, out BValue? utf8))
        {
            return ToStringList(utf8, utf8Key);
        }

        if (Values.TryGetValue(fallbackKey, out BValue? fallback))
        {
            return ToStringList(fallback, fallbackKey);
        }

        throw new ArgumentException($"torrent dictionary requires '{utf8Key}' or '{fallbackKey}'.");
    }

    private static IReadOnlyList<string> ToStringList(BValue value, string key)
    {
        if (value is BString single)
        {
            return [single.Text];
        }

        if (value is not BList list)
        {
            throw new ArgumentException($"torrent '{key}' must be a string or list of strings.");
        }

        var result = new List<string>(list.Items.Count);
        foreach (BValue item in list.Items)
        {
            if (item is not BString text)
            {
                throw new ArgumentException($"torrent '{key}' must contain only strings.");
            }

            result.Add(text.Text);
        }

        return result;
    }
}

internal ref struct BencodeParser
{
    private const int MaxDepth = 32;
    private const int MaxCollectionItems = 4096;
    private readonly ReadOnlySpan<byte> _bytes;
    private int _offset;

    private BencodeParser(ReadOnlySpan<byte> bytes)
    {
        _bytes = bytes;
        _offset = 0;
    }

    public static BValue Parse(ReadOnlySpan<byte> bytes)
    {
        var parser = new BencodeParser(bytes);
        BValue value = parser.ParseValue(0);
        if (parser._offset != bytes.Length)
        {
            throw new ArgumentException("torrent bencode has trailing bytes.");
        }

        return value;
    }

    private BValue ParseValue(int depth)
    {
        if (depth > MaxDepth)
        {
            throw new ArgumentException("torrent bencode nesting is too deep.");
        }

        if (_offset >= _bytes.Length)
        {
            throw new ArgumentException("torrent bencode is truncated.");
        }

        byte token = _bytes[_offset];
        return token switch
        {
            (byte)'i' => ParseInteger(),
            (byte)'l' => ParseList(depth + 1),
            (byte)'d' => ParseDictionary(depth + 1),
            >= (byte)'0' and <= (byte)'9' => ParseString(),
            _ => throw new ArgumentException("torrent bencode contains an invalid token.")
        };
    }

    private BInteger ParseInteger()
    {
        _offset++;
        int start = _offset;
        while (_offset < _bytes.Length && _bytes[_offset] != (byte)'e')
        {
            _offset++;
        }

        if (_offset >= _bytes.Length)
        {
            throw new ArgumentException("torrent integer is truncated.");
        }

        ReadOnlySpan<byte> raw = _bytes[start.._offset];
        _offset++;
        if (raw.IsEmpty || !long.TryParse(Encoding.ASCII.GetString(raw), out long value))
        {
            throw new ArgumentException("torrent integer is invalid or overflows Int64.");
        }

        return new BInteger(value);
    }

    private BString ParseString()
    {
        long length = 0;
        while (_offset < _bytes.Length && _bytes[_offset] != (byte)':')
        {
            byte digit = _bytes[_offset];
            if (digit is < (byte)'0' or > (byte)'9')
            {
                throw new ArgumentException("torrent string length is invalid.");
            }

            long digitValue = digit - (byte)'0';
            if (length > (long.MaxValue - digitValue) / 10)
            {
                throw new ArgumentException("torrent string length overflows Int64.");
            }

            length = length * 10 + digitValue;

            _offset++;
        }

        if (_offset >= _bytes.Length || _bytes[_offset] != (byte)':')
        {
            throw new ArgumentException("torrent string length is truncated.");
        }

        _offset++;
        if (length > int.MaxValue || _offset + length > _bytes.Length)
        {
            throw new ArgumentException("torrent string is truncated or too large.");
        }

        byte[] value = _bytes.Slice(_offset, (int)length).ToArray();
        _offset += (int)length;
        return new BString(value);
    }

    private BList ParseList(int depth)
    {
        _offset++;
        var items = new List<BValue>();
        while (ReadToken() != (byte)'e')
        {
            if (items.Count >= MaxCollectionItems)
            {
                throw new ArgumentException("torrent list contains too many items.");
            }

            items.Add(ParseValue(depth));
        }

        _offset++;
        return new BList(items);
    }

    private BDictionary ParseDictionary(int depth)
    {
        _offset++;
        var values = new Dictionary<string, BValue>(StringComparer.Ordinal);
        while (ReadToken() != (byte)'e')
        {
            if (values.Count >= MaxCollectionItems)
            {
                throw new ArgumentException("torrent dictionary contains too many items.");
            }

            BString key = ParseString();
            values[key.Text] = ParseValue(depth);
        }

        _offset++;
        return new BDictionary(values);
    }

    private byte ReadToken()
    {
        if (_offset >= _bytes.Length)
        {
            throw new ArgumentException("torrent bencode is truncated.");
        }

        return _bytes[_offset];
    }
}
