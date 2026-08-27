using System.Globalization;
using System.Text;
using AnimeGoNet.Core.Metadata;

namespace AnimeGoNet.U2TorrentAudit;

public static class U2TorrentAuditCli
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".avi", ".mov", ".m4v", ".ts", ".m2ts", ".webm"
    };

    public static async Task<int> RunAsync(string[] args)
    {
        var directory = args.Length == 0
            ? Path.Combine(Environment.CurrentDirectory, "u2-torrent-test")
            : Path.GetFullPath(args[0]);
        if (!Directory.Exists(directory))
        {
            Console.Error.WriteLine($"目录不存在：{directory}");
            return 2;
        }

        foreach (var torrentPath in Directory.EnumerateFiles(directory, "*.torrent", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            string csvPath = Path.Combine(
                directory,
                Path.GetFileNameWithoutExtension(torrentPath) + ".csv");
            try
            {
                AuditTorrent(torrentPath, csvPath);
                Console.WriteLine($"已生成：{csvPath}");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                Console.Error.WriteLine($"处理失败：{torrentPath}\n{exception.Message}");
                return 1;
            }
        }

        await Task.CompletedTask.ConfigureAwait(false);
        return 0;
    }

    private static void AuditTorrent(string torrentPath, string csvPath)
    {
        var root = new BencodeReader(File.ReadAllBytes(torrentPath)).ReadDictionary();
        if (!root.TryGetValue("info", out var infoValue)
            || infoValue is not Dictionary<string, object?> info)
        {
            throw new ArgumentException("Torrent 缺少 info 字典。");
        }

        string torrentName = GetString(info, "name.utf-8") ?? GetString(info, "name") ?? "";
        var files = ReadFiles(info);
        var rows = files.Select(file => CreateRow(torrentPath, torrentName, file)).ToArray();

        using var writer = new StreamWriter(
            csvPath,
            append: false,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        writer.WriteLine("torrent_file,torrent_name,relative_path,size_bytes,file_kind,episode,parser_reason");
        foreach (var row in rows)
        {
            writer.WriteLine(string.Join(',',
                Csv(row.TorrentFile),
                Csv(row.TorrentName),
                Csv(row.RelativePath),
                row.SizeBytes.ToString(CultureInfo.InvariantCulture),
                Csv(row.FileKind),
                row.Episode?.ToString(CultureInfo.InvariantCulture) ?? "",
                Csv(row.ParserReason)));
        }
    }

    private static AuditRow CreateRow(string torrentPath, string torrentName, TorrentFile file)
    {
        string extension = Path.GetExtension(file.RelativePath);
        if (!VideoExtensions.Contains(extension))
        {
            return new AuditRow(
                Path.GetFileName(torrentPath), torrentName, file.RelativePath, file.SizeBytes,
                "non_video", null, "non_video_attachment");
        }

        try
        {
            var result = U2FileEpisodeCandidateResolver.Resolve(file.RelativePath);
            return new AuditRow(
                Path.GetFileName(torrentPath), torrentName, file.RelativePath, file.SizeBytes,
                result.IsCandidate ? "episode_candidate" : "unresolved",
                result.Episode, result.Reason);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException)
        {
            return new AuditRow(
                Path.GetFileName(torrentPath), torrentName, file.RelativePath, file.SizeBytes,
                "unresolved", null, "parser_exception: " + exception.Message);
        }
    }

    private static TorrentFile[] ReadFiles(Dictionary<string, object?> info)
    {
        if (info.TryGetValue("files", out var filesValue)
            && filesValue is List<object?> files)
        {
            return files.Select((value, index) => ReadMultiFile(value, index)).ToArray();
        }

        string name = GetString(info, "name.utf-8") ?? GetString(info, "name")
            ?? throw new ArgumentException("Torrent info 缺少 name。");
        long length = GetLong(info, "length");
        return [new TorrentFile(NormalizePath([name]), length)];
    }

    private static TorrentFile ReadMultiFile(object? value, int index)
    {
        if (value is not Dictionary<string, object?> file)
        {
            throw new ArgumentException($"Torrent files[{index}] 不是字典。");
        }

        var path = GetStringList(file, "path.utf-8") ?? GetStringList(file, "path")
            ?? throw new ArgumentException($"Torrent files[{index}] 缺少 path。");
        return new TorrentFile(NormalizePath(path), GetLong(file, "length"));
    }

    private static string NormalizePath(IReadOnlyList<string> segments)
    {
        var normalized = new List<string>();
        foreach (var segment in segments)
        {
            foreach (var part in segment.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                if (part is "." or ".." || part.Contains(':', StringComparison.Ordinal))
                {
                    throw new ArgumentException("Torrent 文件路径必须是相对安全路径。");
                }

                normalized.Add(part);
            }
        }

        return normalized.Count == 0
            ? throw new ArgumentException("Torrent 文件路径不能为空。")
            : string.Join('/', normalized);
    }

    private static string? GetString(Dictionary<string, object?> dictionary, string key) =>
        dictionary.TryGetValue(key, out var value) ? value as string : null;

    private static string[]? GetStringList(Dictionary<string, object?> dictionary, string key)
    {
        if (!dictionary.TryGetValue(key, out var value)) return null;
        if (value is string single) return [single];
        return value is List<object?> list && list.All(item => item is string)
            ? list.Cast<string>().ToArray()
            : null;
    }

    private static long GetLong(Dictionary<string, object?> dictionary, string key) =>
        dictionary.TryGetValue(key, out var value) && value is long number && number >= 0
            ? number
            : throw new ArgumentException($"Torrent 字段 {key} 必须是非负整数。");

    private static string Csv(string value) =>
        $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private sealed record TorrentFile(string RelativePath, long SizeBytes);

    private sealed record AuditRow(
        string TorrentFile,
        string TorrentName,
        string RelativePath,
        long SizeBytes,
        string FileKind,
        int? Episode,
        string ParserReason);
}

internal sealed class BencodeReader(byte[] bytes)
{
    private readonly byte[] _bytes = bytes;
    private int _offset;

    public Dictionary<string, object?> ReadDictionary()
    {
        var value = ReadValue();
        if (value is not Dictionary<string, object?> dictionary)
        {
            throw new ArgumentException("Torrent 根节点必须是字典。");
        }

        if (_offset != _bytes.Length)
        {
            throw new ArgumentException("Torrent bencode 存在尾部数据。");
        }

        return dictionary;
    }

    private object? ReadValue()
    {
        if (_offset >= _bytes.Length) throw new ArgumentException("Torrent bencode 已截断。");
        return _bytes[_offset] switch
        {
            (byte)'i' => ReadInteger(),
            (byte)'l' => ReadList(),
            (byte)'d' => ReadDictionaryValue(),
            >= (byte)'0' and <= (byte)'9' => ReadString(),
            _ => throw new ArgumentException("Torrent bencode 包含无效标记。")
        };
    }

    private long ReadInteger()
    {
        _offset++;
        int start = _offset;
        while (_offset < _bytes.Length && _bytes[_offset] != (byte)'e') _offset++;
        if (_offset >= _bytes.Length
            || !long.TryParse(
                Encoding.ASCII.GetString(_bytes, start, _offset - start),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var value))
        {
            throw new ArgumentException("Torrent bencode 整数无效。");
        }

        _offset++;
        return value;
    }

    private string ReadString()
    {
        int start = _offset;
        while (_offset < _bytes.Length && _bytes[_offset] != (byte)':') _offset++;
        if (_offset >= _bytes.Length
            || !int.TryParse(
                Encoding.ASCII.GetString(_bytes, start, _offset - start),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var length)
            || length < 0)
        {
            throw new ArgumentException("Torrent bencode 字符串长度无效。");
        }

        _offset++;
        if (length > _bytes.Length - _offset) throw new ArgumentException("Torrent bencode 字符串已截断。");
        var value = Encoding.UTF8.GetString(_bytes, _offset, length);
        _offset += length;
        return value;
    }

    private List<object?> ReadList()
    {
        _offset++;
        var list = new List<object?>();
        while (ReadByte() != (byte)'e')
        {
            list.Add(ReadValue());
        }

        _offset++;
        return list;
    }

    private Dictionary<string, object?> ReadDictionaryValue()
    {
        _offset++;
        var dictionary = new Dictionary<string, object?>(StringComparer.Ordinal);
        while (ReadByte() != (byte)'e')
        {
            var key = ReadString();
            dictionary[key] = ReadValue();
        }

        _offset++;
        return dictionary;
    }

    private byte ReadByte() =>
        _offset < _bytes.Length
            ? _bytes[_offset]
            : throw new ArgumentException("Torrent bencode 已截断。");
}
