using System.Buffers;
using System.Text;

namespace AnimeGoNet.Core.Torrents;

public sealed record TorrentMagnetMetadata(
    string InfoHash,
    string DisplayName,
    int TrackerCount);

public sealed class TorrentMagnetException : FormatException
{
    public TorrentMagnetException(string message)
        : base(message)
    {
    }
}

public static class TorrentMagnetParser
{
    private const int MaximumUriLength = 16 * 1024;
    private const int MaximumQueryFields = 2048;
    private const int MaximumDisplayNameLength = 1024;
    private const int MaximumTrackers = 1024;
    private const string ExactTopicPrefix = "urn:btih:";
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static TorrentMagnetMetadata Parse(string uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (uri.Length is 0 or > MaximumUriLength
            || !uri.StartsWith("magnet:?", StringComparison.Ordinal))
        {
            throw Invalid("Magnet URI must use the lowercase magnet scheme.");
        }

        var fragmentIndex = uri.IndexOf('#', "magnet:?".Length);
        var query = fragmentIndex < 0
            ? uri.AsSpan("magnet:?".Length)
            : uri.AsSpan("magnet:?".Length, fragmentIndex - "magnet:?".Length);
        string? exactTopic = null;
        string? displayName = null;
        var trackerCount = 0;
        var fieldCount = 0;
        while (!query.IsEmpty)
        {
            var separator = query.IndexOf('&');
            var field = separator < 0 ? query : query[..separator];
            query = separator < 0 ? [] : query[(separator + 1)..];
            if (field.IsEmpty)
            {
                continue;
            }
            fieldCount++;
            if (fieldCount > MaximumQueryFields)
            {
                throw Invalid("Magnet URI contains too many query fields.");
            }

            var equals = field.IndexOf('=');
            var encodedKey = equals < 0 ? field : field[..equals];
            var encodedValue = equals < 0 ? [] : field[(equals + 1)..];
            var key = DecodeFormComponent(encodedKey);
            switch (key)
            {
                case "xt" when exactTopic is null:
                    exactTopic = DecodeFormComponent(encodedValue);
                    break;
                case "dn" when displayName is null:
                    displayName = DecodeFormComponent(encodedValue);
                    ValidateDisplayName(displayName);
                    break;
                case "tr":
                    trackerCount++;
                    if (trackerCount > MaximumTrackers)
                    {
                        throw Invalid("Magnet URI contains too many trackers.");
                    }
                    break;
            }
        }

        if (exactTopic is null
            || !exactTopic.StartsWith(ExactTopicPrefix, StringComparison.Ordinal))
        {
            throw Invalid("Magnet URI requires a BitTorrent v1 exact topic.");
        }

        var encodedHash = exactTopic.AsSpan(ExactTopicPrefix.Length);
        var hash = encodedHash.Length switch
        {
            40 => DecodeHexHash(encodedHash),
            32 => DecodeBase32Hash(encodedHash),
            _ => throw Invalid("Magnet BitTorrent info hash has an unsupported length."),
        };
        return new TorrentMagnetMetadata(
            Convert.ToHexStringLower(hash),
            displayName ?? string.Empty,
            trackerCount);
    }

    private static byte[] DecodeHexHash(ReadOnlySpan<char> value)
    {
        Span<byte> decoded = stackalloc byte[20];
        for (var index = 0; index < decoded.Length; index++)
        {
            if (!TryHex(value[index * 2], out var high)
                || !TryHex(value[(index * 2) + 1], out var low))
            {
                throw Invalid("Magnet hexadecimal info hash is invalid.");
            }

            decoded[index] = (byte)((high << 4) | low);
        }

        return decoded.ToArray();
    }

    private static byte[] DecodeBase32Hash(ReadOnlySpan<char> value)
    {
        Span<byte> decoded = stackalloc byte[20];
        var accumulator = 0;
        var bits = 0;
        var output = 0;
        foreach (var character in value)
        {
            var digit = Base32Alphabet.IndexOf(character, StringComparison.Ordinal);
            if (digit < 0)
            {
                throw Invalid("Magnet Base32 info hash is invalid.");
            }

            accumulator = (accumulator << 5) | digit;
            bits += 5;
            if (bits < 8)
            {
                continue;
            }

            bits -= 8;
            decoded[output++] = (byte)(accumulator >> bits);
            accumulator &= (1 << bits) - 1;
        }

        if (output != decoded.Length || bits != 0)
        {
            throw Invalid("Magnet Base32 info hash is invalid.");
        }

        return decoded.ToArray();
    }

    private static string DecodeFormComponent(ReadOnlySpan<char> value)
    {
        var bytes = new ArrayBufferWriter<byte>(Math.Max(value.Length, 1));
        for (var index = 0; index < value.Length;)
        {
            if (value[index] == '%')
            {
                if (index + 2 >= value.Length
                    || !TryHex(value[index + 1], out var high)
                    || !TryHex(value[index + 2], out var low))
                {
                    throw Invalid("Magnet URI contains invalid percent encoding.");
                }

                bytes.GetSpan(1)[0] = (byte)((high << 4) | low);
                bytes.Advance(1);
                index += 3;
                continue;
            }
            if (value[index] == '+')
            {
                bytes.GetSpan(1)[0] = (byte)' ';
                bytes.Advance(1);
                index++;
                continue;
            }

            var status = Rune.DecodeFromUtf16(
                value[index..],
                out var rune,
                out var consumed);
            if (status != OperationStatus.Done)
            {
                throw Invalid("Magnet URI contains invalid Unicode.");
            }

            var target = bytes.GetSpan(4);
            var written = rune.EncodeToUtf8(target);
            bytes.Advance(written);
            index += consumed;
        }

        try
        {
            return StrictUtf8.GetString(bytes.WrittenSpan);
        }
        catch (DecoderFallbackException)
        {
            throw Invalid("Magnet URI contains invalid UTF-8.");
        }
    }

    private static void ValidateDisplayName(string value)
    {
        if (value.Length > MaximumDisplayNameLength
            || value.Any(character => char.IsControl(character)))
        {
            throw Invalid("Magnet display name is invalid.");
        }
    }

    private static bool TryHex(char character, out int value)
    {
        value = character switch
        {
            >= '0' and <= '9' => character - '0',
            >= 'a' and <= 'f' => character - 'a' + 10,
            >= 'A' and <= 'F' => character - 'A' + 10,
            _ => -1,
        };
        return value >= 0;
    }

    private static TorrentMagnetException Invalid(string message) => new(message);
}
