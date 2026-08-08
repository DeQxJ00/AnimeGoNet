using System.Globalization;
using System.Text;

namespace AnimeGoNet.App.Logging;

public sealed record RollingFileLogOptions
{
    public const long DefaultMaximumFileBytes = 2 * 1024 * 1024;
    public const int DefaultMaximumBackups = 14;
    public static readonly TimeSpan DefaultMaximumAge = TimeSpan.FromDays(14);

    public required string FilePath { get; init; }

    public long MaximumFileBytes { get; init; } = DefaultMaximumFileBytes;

    public int MaximumBackups { get; init; } = DefaultMaximumBackups;

    public TimeSpan MaximumAge { get; init; } = DefaultMaximumAge;

    public LogLevel MinimumLevel { get; init; } = LogLevel.Information;
}

public sealed class RollingFileLoggerProvider : ILoggerProvider
{
    private static readonly byte[] NewLineBytes =
        Encoding.UTF8.GetBytes(Environment.NewLine);
    private readonly Lock _gate = new();
    private readonly string _filePath;
    private readonly long _maximumFileBytes;
    private readonly int _maximumBackups;
    private readonly TimeSpan _maximumAge;
    private readonly LogLevel _minimumLevel;
    private FileStream? _stream;
    private bool _disabled;
    private bool _disposed;

    public RollingFileLoggerProvider(RollingFileLogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.FilePath))
        {
            throw new ArgumentException(
                "The rolling log file path is required.",
                nameof(options));
        }
        if (options.MaximumFileBytes < 128)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "The rolling log maximum file size must be at least 128 bytes.");
        }
        if (options.MaximumBackups is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "The rolling log backup count must be between 1 and 100.");
        }
        if (options.MaximumAge <= TimeSpan.Zero
            || options.MaximumAge > TimeSpan.FromDays(3650))
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "The rolling log maximum age must be between one tick and ten years.");
        }
        if (options.MinimumLevel is LogLevel.None
            || options.MinimumLevel < LogLevel.Trace
            || options.MinimumLevel > LogLevel.Critical)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "The rolling log minimum level must be between Trace and Critical.");
        }

        _filePath = Path.GetFullPath(options.FilePath);
        _maximumFileBytes = options.MaximumFileBytes;
        _maximumBackups = options.MaximumBackups;
        _maximumAge = options.MaximumAge;
        _minimumLevel = options.MinimumLevel;
        var directory = Path.GetDirectoryName(_filePath)
            ?? throw new ArgumentException(
                "The rolling log file must have a parent directory.",
                nameof(options));
        Directory.CreateDirectory(directory);
        DeleteExpiredBackups(DateTimeOffset.UtcNow);
        _stream = OpenStream();
    }

    public ILogger CreateLogger(string categoryName)
    {
        ArgumentNullException.ThrowIfNull(categoryName);
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new RollingFileLogger(this, categoryName);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                _stream?.Flush(flushToDisk: true);
            }
            catch (IOException)
            {
            }
            finally
            {
                _stream?.Dispose();
                _stream = null;
            }
        }
    }

    private void Write(
        string category,
        LogLevel level,
        EventId eventId,
        string message,
        Exception? exception)
    {
        var line = WebSocketLogFormatter.Format(
            category,
            level,
            eventId,
            message,
            exception);
        var bytes = Encoding.UTF8.GetBytes(line);

        lock (_gate)
        {
            if (_disposed || _disabled || _stream is null)
            {
                return;
            }

            try
            {
                RotateIfRequired(bytes.Length + NewLineBytes.Length);
                _stream.Write(bytes);
                _stream.Write(NewLineBytes);
                _stream.Flush();
            }
            catch (IOException)
            {
                DisableAfterRuntimeFailure();
            }
            catch (UnauthorizedAccessException)
            {
                DisableAfterRuntimeFailure();
            }
        }
    }

    private void RotateIfRequired(int incomingBytes)
    {
        if (_stream is null
            || _stream.Length == 0
            || _stream.Length + incomingBytes <= _maximumFileBytes)
        {
            return;
        }

        _stream.Flush(flushToDisk: true);
        _stream.Dispose();
        _stream = null;

        var oldest = BackupPath(_maximumBackups);
        File.Delete(oldest);
        for (var index = _maximumBackups - 1; index >= 1; index--)
        {
            var source = BackupPath(index);
            if (File.Exists(source))
            {
                File.Move(source, BackupPath(index + 1), overwrite: true);
            }
        }
        File.Move(_filePath, BackupPath(1), overwrite: true);
        DeleteExpiredBackups(DateTimeOffset.UtcNow);
        _stream = OpenStream();
    }

    private void DeleteExpiredBackups(DateTimeOffset now)
    {
        var directory = Path.GetDirectoryName(_filePath)!;
        var fileName = Path.GetFileName(_filePath);
        var prefix = string.Concat(fileName, ".");
        foreach (var candidate in Directory.EnumerateFiles(
            directory,
            string.Concat(fileName, ".*"),
            SearchOption.TopDirectoryOnly))
        {
            var candidateName = Path.GetFileName(candidate);
            if (!candidateName.StartsWith(prefix, StringComparison.Ordinal)
                || !int.TryParse(
                    candidateName.AsSpan(prefix.Length),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var index))
            {
                continue;
            }

            var lastWrite = File.GetLastWriteTimeUtc(candidate);
            if (index > _maximumBackups
                || now - lastWrite > _maximumAge)
            {
                File.Delete(candidate);
            }
        }
    }

    private FileStream OpenStream()
    {
        var stream = new FileStream(
            _filePath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.SequentialScan);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                _filePath,
                UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.GroupRead);
        }
        return stream;
    }

    private string BackupPath(int index) =>
        string.Concat(
            _filePath,
            ".",
            index.ToString(CultureInfo.InvariantCulture));

    private void DisableAfterRuntimeFailure()
    {
        _disabled = true;
        try
        {
            _stream?.Dispose();
        }
        catch (IOException)
        {
        }
        _stream = null;
    }

    private sealed class RollingFileLogger(
        RollingFileLoggerProvider provider,
        string category) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull =>
            NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) =>
            logLevel >= provider._minimumLevel
            && logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            if (!IsEnabled(logLevel))
            {
                return;
            }

            string message;
            try
            {
                message = formatter(state, exception);
            }
            catch
            {
                message = "Log formatter failed.";
            }
            provider.Write(
                category,
                logLevel,
                eventId,
                message,
                exception);
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
