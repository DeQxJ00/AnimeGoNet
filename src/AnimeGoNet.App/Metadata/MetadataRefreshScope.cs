namespace AnimeGoNet.App.Metadata;

public sealed class MetadataRefreshScope
{
    private readonly AsyncLocal<int> _depth = new();

    public bool BypassCaches => _depth.Value > 0;

    public IDisposable Begin(bool enabled)
    {
        if (!enabled)
        {
            return EmptyScope.Instance;
        }

        _depth.Value++;
        return new Scope(this);
    }

    private sealed class Scope(MetadataRefreshScope owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner._depth.Value = Math.Max(0, owner._depth.Value - 1);
            }
        }
    }

    private sealed class EmptyScope : IDisposable
    {
        public static EmptyScope Instance { get; } = new();
        public void Dispose() { }
    }
}
