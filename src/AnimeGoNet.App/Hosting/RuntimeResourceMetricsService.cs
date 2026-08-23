using System.Diagnostics;
using AnimeGoNet.Core.Configuration;

namespace AnimeGoNet.App.Hosting;

public sealed record RuntimeResourceMetricsSnapshot(
    long WorkingSetBytes,
    double CpuPercent,
    int LogicalProcessorCount,
    long DataPathBytes,
    DateTimeOffset DataPathScannedAtUtc,
    bool DataPathScanComplete);

public sealed class RuntimeResourceMetricsService : IDisposable
{
    private static readonly TimeSpan DirectoryCacheDuration = TimeSpan.FromMinutes(1);
    private readonly DirectoryLayout _layout;
    private readonly Process _process = Process.GetCurrentProcess();
    private readonly object _cpuGate = new();
    private readonly SemaphoreSlim _directoryGate = new(1, 1);
    private long _lastCpuTimestamp;
    private TimeSpan _lastCpuTime;
    private double _lastCpuPercent;
    private DirectoryUsage? _directoryUsage;

    public RuntimeResourceMetricsService(DirectoryLayout layout)
    {
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
    }

    public void Dispose()
    {
        _directoryGate.Dispose();
        _process.Dispose();
    }

    public async Task<RuntimeResourceMetricsSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        var cpuPercent = SampleCpuPercent();
        _process.Refresh();
        var workingSetBytes = Math.Max(0, _process.WorkingSet64);
        var directory = await GetDirectoryUsageAsync(cancellationToken).ConfigureAwait(false);
        return new RuntimeResourceMetricsSnapshot(
            workingSetBytes,
            cpuPercent,
            Math.Max(1, Environment.ProcessorCount),
            directory.Bytes,
            directory.ScannedAtUtc,
            directory.Complete);
    }

    private double SampleCpuPercent()
    {
        lock (_cpuGate)
        {
            _process.Refresh();
            var timestamp = Stopwatch.GetTimestamp();
            var cpuTime = _process.TotalProcessorTime;
            double elapsedSeconds;
            double cpuSeconds;
            if (_lastCpuTimestamp == 0)
            {
                elapsedSeconds = Math.Max(
                    (DateTimeOffset.UtcNow - _process.StartTime.ToUniversalTime()).TotalSeconds,
                    0.001);
                cpuSeconds = cpuTime.TotalSeconds;
            }
            else
            {
                elapsedSeconds = Stopwatch.GetElapsedTime(_lastCpuTimestamp, timestamp).TotalSeconds;
                cpuSeconds = Math.Max(0, (cpuTime - _lastCpuTime).TotalSeconds);
            }

            _lastCpuTimestamp = timestamp;
            _lastCpuTime = cpuTime;
            if (elapsedSeconds >= 0.001)
            {
                _lastCpuPercent = Math.Clamp(
                    cpuSeconds / elapsedSeconds / Math.Max(1, Environment.ProcessorCount) * 100,
                    0,
                    100);
            }
            return Math.Round(_lastCpuPercent, 2, MidpointRounding.AwayFromZero);
        }
    }

    private async Task<DirectoryUsage> GetDirectoryUsageAsync(CancellationToken cancellationToken)
    {
        var cached = _directoryUsage;
        var now = DateTimeOffset.UtcNow;
        if (cached is not null && now - cached.ScannedAtUtc < DirectoryCacheDuration)
            return cached;

        await _directoryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cached = _directoryUsage;
            now = DateTimeOffset.UtcNow;
            if (cached is not null && now - cached.ScannedAtUtc < DirectoryCacheDuration)
                return cached;

            var scanned = await Task.Run(
                () => ScanDirectory(_layout.DataPath, cancellationToken),
                cancellationToken).ConfigureAwait(false);
            _directoryUsage = scanned with { ScannedAtUtc = DateTimeOffset.UtcNow };
            return _directoryUsage;
        }
        finally
        {
            _directoryGate.Release();
        }
    }

    private static DirectoryUsage ScanDirectory(
        string root,
        CancellationToken cancellationToken)
    {
        long total = 0;
        var complete = true;
        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                ReturnSpecialDirectories = false,
                AttributesToSkip = FileAttributes.ReparsePoint,
            };
            foreach (var path in Directory.EnumerateFiles(root, "*", options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var length = new FileInfo(path).Length;
                    total = length > long.MaxValue - total ? long.MaxValue : total + length;
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException)
                {
                    complete = false;
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            complete = false;
        }
        return new DirectoryUsage(total, DateTimeOffset.MinValue, complete);
    }

    private sealed record DirectoryUsage(
        long Bytes,
        DateTimeOffset ScannedAtUtc,
        bool Complete);
}
