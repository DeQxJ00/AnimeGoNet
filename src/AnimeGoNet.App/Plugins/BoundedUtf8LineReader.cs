using System.Buffers;

namespace AnimeGoNet.App.Plugins;

internal sealed class BoundedUtf8LineReader(Stream stream, int bufferSize = 4096)
{
    private readonly byte[] _buffer = new byte[bufferSize];
    private int _offset;
    private int _count;

    public async Task<byte[]?> ReadLineAsync(
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumBytes, 1);
        var line = new ArrayBufferWriter<byte>(Math.Min(maximumBytes, 4096));
        while (true)
        {
            if (_offset < _count)
            {
                var newline = Array.IndexOf(
                    _buffer,
                    (byte)'\n',
                    _offset,
                    _count - _offset);
                if (newline >= 0)
                {
                    Append(line, _buffer.AsSpan(_offset, newline - _offset), maximumBytes);
                    _offset = newline + 1;
                    var length = line.WrittenCount;
                    if (length > 0 && line.WrittenSpan[length - 1] == (byte)'\r')
                    {
                        length--;
                    }
                    return line.WrittenSpan[..length].ToArray();
                }

                Append(line, _buffer.AsSpan(_offset, _count - _offset), maximumBytes);
                _offset = _count;
            }

            _count = await stream.ReadAsync(_buffer, cancellationToken).ConfigureAwait(false);
            _offset = 0;
            if (_count == 0)
            {
                if (line.WrittenCount == 0)
                {
                    return null;
                }
                throw new ExternalPluginProtocolException(
                    "plugin_response_truncated",
                    "The external plugin closed stdout before terminating a JSON line.");
            }
        }
    }

    private static void Append(
        ArrayBufferWriter<byte> writer,
        ReadOnlySpan<byte> value,
        int maximumBytes)
    {
        if (value.Length > maximumBytes - writer.WrittenCount)
        {
            throw new ExternalPluginProtocolException(
                "plugin_response_too_large",
                "The external plugin response line exceeds the configured limit.");
        }
        writer.Write(value);
    }
}
