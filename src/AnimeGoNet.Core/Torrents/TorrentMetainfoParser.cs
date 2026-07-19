using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace AnimeGoNet.Core.Torrents;

public static class TorrentMetainfoParser
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static TorrentMetadata Parse(byte[] data, TorrentMetainfoLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        limits ??= new TorrentMetainfoLimits();
        var reader = new BencodeReader(data, limits.MaxDepth);
        var root = RequireDictionary(reader.ReadRoot(), "Torrent metainfo must be a dictionary.");
        var infoEntry = root.Find("info")
            ?? throw new TorrentMetainfoException("Torrent metainfo is missing its info dictionary.");
        var info = RequireDictionary(infoEntry.Value, "Torrent info must be a dictionary.");

        var name = ReadText(info, "name.utf-8") ?? ReadRequiredText(info, "name");
        ValidatePathComponent(name, "Torrent name");
        var pieceLength = ReadRequiredPositiveInteger(info, "piece length");
        var pieces = ReadRequiredBytes(info, "pieces");
        if (pieces.Length == 0 || pieces.Length % 20 != 0)
        {
            throw new TorrentMetainfoException("Torrent pieces must contain one or more SHA-1 hashes.");
        }

        var filesEntry = info.Find("files");
        var lengthEntry = info.Find("length");
        if ((filesEntry is null) == (lengthEntry is null))
        {
            throw new TorrentMetainfoException("Torrent info must contain exactly one of files or length.");
        }

        IReadOnlyList<TorrentFile> files;
        long totalSize;
        if (filesEntry is not null)
        {
            (files, totalSize) = ReadFiles(name, filesEntry.Value, limits);
        }
        else
        {
            totalSize = ReadNonNegativeInteger(lengthEntry!.Value, "Torrent length");
            files = [new TorrentFile(name, totalSize, IsPaddingPath(name))];
        }

        if (totalSize > limits.MaxTotalSize)
        {
            throw new TorrentMetainfoException("Torrent total size exceeds the configured limit.");
        }

        var expectedPieceBytes = checked((totalSize + pieceLength - 1) / pieceLength * 20);
        if (pieces.LongLength != expectedPieceBytes)
        {
            throw new TorrentMetainfoException("Torrent piece hash count does not match its declared size.");
        }

#pragma warning disable CA5350 // BitTorrent v1 mandates SHA-1 over the original bencoded info bytes.
        var infoHash = Convert.ToHexStringLower(SHA1.HashData(data.AsSpan(infoEntry.Start, infoEntry.End - infoEntry.Start)));
#pragma warning restore CA5350
        return new TorrentMetadata(name, infoHash, totalSize, files);
    }

    private static (IReadOnlyList<TorrentFile> Files, long TotalSize) ReadFiles(
        string rootName,
        BValue value,
        TorrentMetainfoLimits limits)
    {
        if (value is not BList list || list.Values.Count == 0)
        {
            throw new TorrentMetainfoException("Torrent files must be a non-empty list.");
        }

        if (list.Values.Count > limits.MaxFiles)
        {
            throw new TorrentMetainfoException("Torrent file count exceeds the configured limit.");
        }

        var files = new List<TorrentFile>(list.Values.Count);
        long totalSize = 0;
        foreach (var item in list.Values)
        {
            var file = RequireDictionary(item, "Each Torrent file must be a dictionary.");
            var length = ReadNonNegativeInteger(
                file.Find("length")?.Value ?? throw new TorrentMetainfoException("Torrent file is missing length."),
                "Torrent file length");
            var pathValue = file.Find("path.utf-8")?.Value ?? file.Find("path")?.Value
                ?? throw new TorrentMetainfoException("Torrent file is missing path.");
            if (pathValue is not BList path || path.Values.Count == 0 || path.Values.Count > limits.MaxPathComponents)
            {
                throw new TorrentMetainfoException("Torrent file path has an invalid component count.");
            }

            var components = new string[path.Values.Count + 1];
            components[0] = rootName;
            for (var index = 0; index < path.Values.Count; index++)
            {
                if (path.Values[index] is not BBytes bytes)
                {
                    throw new TorrentMetainfoException("Torrent file path components must be byte strings.");
                }

                var component = DecodeText(bytes.Value, "Torrent file path is not valid UTF-8.");
                ValidatePathComponent(component, "Torrent file path component");
                components[index + 1] = component;
            }

            try
            {
                totalSize = checked(totalSize + length);
            }
            catch (OverflowException exception)
            {
                throw new TorrentMetainfoException("Torrent total size is too large.", exception);
            }

            if (totalSize > limits.MaxTotalSize)
            {
                throw new TorrentMetainfoException("Torrent total size exceeds the configured limit.");
            }

            var relativePath = string.Join('/', components);
            files.Add(new TorrentFile(relativePath, length, IsPaddingPath(relativePath)));
        }

        return (files, totalSize);
    }

    private static bool IsPaddingPath(string path) =>
        path.Split('/').Any(component => component.StartsWith("_____padding_file", StringComparison.Ordinal));

    private static void ValidatePathComponent(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value is "." or ".."
            || value.Contains('/')
            || value.Contains('\\')
            || value.Contains('\0'))
        {
            throw new TorrentMetainfoException($"{label} is unsafe.");
        }
    }

    private static string ReadRequiredText(BDictionary dictionary, string key) =>
        ReadText(dictionary, key) ?? throw new TorrentMetainfoException($"Torrent info is missing {key}.");

    private static string? ReadText(BDictionary dictionary, string key)
    {
        var entry = dictionary.Find(key);
        if (entry is null)
        {
            return null;
        }

        return entry.Value is BBytes bytes
            ? DecodeText(bytes.Value, $"Torrent {key} is not valid UTF-8.")
            : throw new TorrentMetainfoException($"Torrent {key} must be a byte string.");
    }

    private static byte[] ReadRequiredBytes(BDictionary dictionary, string key) =>
        dictionary.Find(key)?.Value is BBytes bytes
            ? bytes.Value
            : throw new TorrentMetainfoException($"Torrent info is missing valid {key}.");

    private static long ReadRequiredPositiveInteger(BDictionary dictionary, string key)
    {
        var value = dictionary.Find(key)?.Value
            ?? throw new TorrentMetainfoException($"Torrent info is missing {key}.");
        var number = ReadNonNegativeInteger(value, $"Torrent {key}");
        return number > 0 ? number : throw new TorrentMetainfoException($"Torrent {key} must be positive.");
    }

    private static long ReadNonNegativeInteger(BValue value, string label) =>
        value is BInteger integer && integer.Value >= 0
            ? integer.Value
            : throw new TorrentMetainfoException($"{label} must be a non-negative integer.");

    private static BDictionary RequireDictionary(BValue value, string message) =>
        value as BDictionary ?? throw new TorrentMetainfoException(message);

    private static string DecodeText(byte[] value, string message)
    {
        try
        {
            return StrictUtf8.GetString(value);
        }
        catch (DecoderFallbackException exception)
        {
            throw new TorrentMetainfoException(message, exception);
        }
    }

    private abstract record BValue;

    private sealed record BInteger(long Value) : BValue;

    private sealed record BBytes(byte[] Value) : BValue;

    private sealed record BList(IReadOnlyList<BValue> Values) : BValue;

    private sealed record BDictionary(IReadOnlyList<BDictionaryEntry> Entries) : BValue
    {
        public BDictionaryEntry? Find(string key)
        {
            var encodedKey = Encoding.ASCII.GetBytes(key);
            return Entries.FirstOrDefault(entry => entry.Key.AsSpan().SequenceEqual(encodedKey));
        }
    }

    private sealed record BDictionaryEntry(byte[] Key, BValue Value, int Start, int End);

    private sealed class BencodeReader(byte[] data, int maxDepth)
    {
        private int _position;

        public BValue ReadRoot()
        {
            var result = ReadValue(0);
            if (_position != data.Length)
            {
                throw new TorrentMetainfoException("Torrent metainfo has trailing data.");
            }

            return result;
        }

        private BValue ReadValue(int depth)
        {
            if (depth > maxDepth || _position >= data.Length)
            {
                throw new TorrentMetainfoException("Torrent bencode is truncated or too deeply nested.");
            }

            return data[_position] switch
            {
                (byte)'i' => ReadInteger(),
                (byte)'l' => ReadList(depth),
                (byte)'d' => ReadDictionary(depth),
                >= (byte)'0' and <= (byte)'9' => new BBytes(ReadBytes()),
                _ => throw new TorrentMetainfoException("Torrent bencode contains an invalid value marker."),
            };
        }

        private BInteger ReadInteger()
        {
            _position++;
            var start = _position;
            while (_position < data.Length && data[_position] != (byte)'e')
            {
                _position++;
            }

            if (_position >= data.Length)
            {
                throw new TorrentMetainfoException("Torrent bencode integer is truncated.");
            }

            var text = Encoding.ASCII.GetString(data, start, _position - start);
            _position++;
            if (text.Length == 0
                || text is "-0"
                || (text.Length > 1 && text[0] == '0')
                || (text.Length > 2 && text.StartsWith("-0", StringComparison.Ordinal))
                || !long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
            {
                throw new TorrentMetainfoException("Torrent bencode integer is not canonical.");
            }

            return new BInteger(value);
        }

        private BList ReadList(int depth)
        {
            _position++;
            var values = new List<BValue>();
            while (!AtEndMarker())
            {
                values.Add(ReadValue(depth + 1));
            }

            _position++;
            return new BList(values);
        }

        private BDictionary ReadDictionary(int depth)
        {
            _position++;
            var entries = new List<BDictionaryEntry>();
            byte[]? previousKey = null;
            while (!AtEndMarker())
            {
                if (_position >= data.Length || data[_position] is < (byte)'0' or > (byte)'9')
                {
                    throw new TorrentMetainfoException("Torrent bencode dictionary key must be a byte string.");
                }

                var key = ReadBytes();
                if (previousKey is not null && previousKey.AsSpan().SequenceCompareTo(key) >= 0)
                {
                    throw new TorrentMetainfoException("Torrent bencode dictionary keys must be unique and sorted.");
                }

                var start = _position;
                var value = ReadValue(depth + 1);
                entries.Add(new BDictionaryEntry(key, value, start, _position));
                previousKey = key;
            }

            _position++;
            return new BDictionary(entries);
        }

        private byte[] ReadBytes()
        {
            var lengthStart = _position;
            while (_position < data.Length && data[_position] != (byte)':')
            {
                if (data[_position] is < (byte)'0' or > (byte)'9')
                {
                    throw new TorrentMetainfoException("Torrent bencode byte string length is invalid.");
                }

                _position++;
            }

            if (_position >= data.Length)
            {
                throw new TorrentMetainfoException("Torrent bencode byte string is truncated.");
            }

            var lengthText = Encoding.ASCII.GetString(data, lengthStart, _position - lengthStart);
            _position++;
            if (lengthText.Length == 0
                || (lengthText.Length > 1 && lengthText[0] == '0')
                || !int.TryParse(lengthText, NumberStyles.None, CultureInfo.InvariantCulture, out var length)
                || length < 0
                || length > data.Length - _position)
            {
                throw new TorrentMetainfoException("Torrent bencode byte string length is invalid.");
            }

            var result = data.AsSpan(_position, length).ToArray();
            _position += length;
            return result;
        }

        private bool AtEndMarker()
        {
            if (_position >= data.Length)
            {
                throw new TorrentMetainfoException("Torrent bencode collection is truncated.");
            }

            return data[_position] == (byte)'e';
        }
    }
}
