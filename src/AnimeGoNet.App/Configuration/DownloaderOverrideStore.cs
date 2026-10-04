using System.Globalization;
using System.Text;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace AnimeGoNet.App.Configuration;

public sealed record DownloaderOverrideEntry(
    string BaseUrl,
    string? Username,
    string? Password,
    string DownloadPath,
    bool Enabled,
    long Revision,
    DateTimeOffset UpdatedAtUtc);

public sealed record DownloaderOverrideSnapshot(
    int FormatVersion,
    long Revision,
    IReadOnlyDictionary<string, DownloaderOverrideEntry> Downloaders);

public sealed class DownloaderOverrideRevisionException : InvalidOperationException;

public sealed record DownloaderConfigurationRuntimeState(long AppliedRevision);

public sealed class DownloaderOverrideStore : IDisposable
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate;
    private readonly DownloaderDeploymentLocks _locks;
    private readonly IReadOnlyDictionary<string, AnimeGoNet.Core.Configuration.QbittorrentInstanceOptions>? _defaults;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public DownloaderOverrideStore(
        string configurationPath,
        string? yamlFilePath = null,
        DownloaderDeploymentLocks? locks = null,
        IReadOnlyDictionary<string, AnimeGoNet.Core.Configuration.QbittorrentInstanceOptions>? defaults = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationPath);
        _path = Path.GetFullPath(yamlFilePath ?? Path.Combine(configurationPath, "animego.yaml"));
        _gate = DeploymentFileGate.ForPath(_path);
        _locks = locks ?? DownloaderDeploymentLocks.Empty;
        _defaults = defaults;
    }

    // The per-path gate is shared with the raw YAML editor and other store instances.
    public void Dispose() { }

    public async Task<DownloaderOverrideSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var root = await ReadAsync(cancellationToken).ConfigureAwait(false);
            return Snapshot(root);
        }
        finally { _gate.Release(); }
    }

    public async Task<DownloaderOverrideSnapshot> UpsertAsync(
        string id, DownloaderOverrideEntry definition, long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(definition);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var root = await ReadAsync(cancellationToken).ConfigureAwait(false);
            var current = Snapshot(root);
            if (current.Revision != expectedRevision) throw new DownloaderOverrideRevisionException();
            WriteEntry(root, id, definition with
            {
                Revision = current.Downloaders.TryGetValue(id, out var old) ? old.Revision + 1 : 1,
            }, preserveLockedFields: true);
            Set(root, "webui_downloader_revision", (current.Revision + 1).ToString(CultureInfo.InvariantCulture));
            await SaveAsync(root, cancellationToken).ConfigureAwait(false);
            return Snapshot(root);
        }
        finally { _gate.Release(); }
    }

    public async Task<DownloaderOverrideSnapshot> DeleteAsync(
        string id, long expectedRevision, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var root = await ReadAsync(cancellationToken).ConfigureAwait(false);
            var current = Snapshot(root);
            if (current.Revision != expectedRevision) throw new DownloaderOverrideRevisionException();
            var entries = Map(root, "downloaders");
            var key = entries.Children.Keys.OfType<YamlScalarNode>()
                .SingleOrDefault(k => string.Equals(k.Value, id, StringComparison.OrdinalIgnoreCase));
            if (key is null) throw new KeyNotFoundException("Downloader YAML entry was not found.");
            if (_locks.ForDownloader(id).Count > 0)
                throw new ArgumentException("Deployment-locked downloader cannot be deleted.");
            entries.Children.Remove(key);
            Set(root, "webui_downloader_revision", (current.Revision + 1).ToString(CultureInfo.InvariantCulture));
            await SaveAsync(root, cancellationToken).ConfigureAwait(false);
            return Snapshot(root);
        }
        finally { _gate.Release(); }
    }

    private async Task<YamlMappingNode> ReadAsync(CancellationToken token)
    {
        if (!File.Exists(_path))
            return new YamlMappingNode { { "version", DeploymentYamlConfiguration.CurrentVersion } };
        var info = new FileInfo(_path);
        if (info.Length is <= 0 or > 1024 * 1024)
            throw new DeploymentYamlException("Deployment YAML size is invalid.");
        var content = Utf8.GetString(await File.ReadAllBytesAsync(_path, token).ConfigureAwait(false));
        var stream = new YamlStream();
        stream.Load(new StringReader(content));
        if (stream.Documents.Count != 1 || stream.Documents[0].RootNode is not YamlMappingNode root)
            throw new DeploymentYamlException("Deployment YAML must contain one mapping.");
        return root;
    }

    private void WriteEntry(YamlMappingNode root, string id, DownloaderOverrideEntry entry, bool preserveLockedFields)
    {
        var all = Map(root, "downloaders");
        var key = all.Children.Keys.OfType<YamlScalarNode>()
            .SingleOrDefault(k => string.Equals(k.Value, id, StringComparison.OrdinalIgnoreCase))
            ?? new YamlScalarNode(id);
        if (!all.Children.TryGetValue(key, out var raw))
        {
            raw = new YamlMappingNode();
            all.Add(key, raw);
        }
        var item = raw as YamlMappingNode ?? throw new DeploymentYamlException("Downloader must be a mapping.");
        void Field(string name, string value)
        {
            if (!preserveLockedFields || !_locks.IsLocked(id, name)) Set(item, name, value);
        }
        if (!item.Children.ContainsKey(new YamlScalarNode("type"))) Field("type", "qbittorrent");
        Field("base_url", entry.BaseUrl);
        Field("username", entry.Username ?? "");
        Field("password", entry.Password ?? "");
        Field("download_path", entry.DownloadPath);
        Field("enabled", entry.Enabled ? "true" : "false");
        Set(item, "webui_revision", entry.Revision.ToString(CultureInfo.InvariantCulture));
        Set(item, "webui_updated_at", entry.UpdatedAtUtc.ToString("O", CultureInfo.InvariantCulture));
    }

    private DownloaderOverrideSnapshot Snapshot(YamlMappingNode root)
    {
        var entries = new Dictionary<string, DownloaderOverrideEntry>(StringComparer.OrdinalIgnoreCase);
        if (root.Children.TryGetValue(new YamlScalarNode("downloaders"), out var raw))
        {
            var map = raw as YamlMappingNode ?? throw new DeploymentYamlException("Downloaders must be a mapping.");
            foreach (var pair in map.Children)
            {
                var id = (pair.Key as YamlScalarNode)?.Value
                    ?? throw new DeploymentYamlException("Downloader ID is invalid.");
                var item = pair.Value as YamlMappingNode
                    ?? throw new DeploymentYamlException("Downloader must be a mapping.");
                // Incomplete environment-only entries remain owned by deployment configuration.
                var fallback = _defaults?.GetValueOrDefault(id);
                var url = Text(item, "base_url") ?? fallback?.BaseUrl.AbsoluteUri;
                var path = Text(item, "download_path") ?? fallback?.DownloadPath;
                if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(path)) continue;
                entries.Add(id, new DownloaderOverrideEntry(
                    url, Text(item, "username"), Text(item, "password"), path,
                    !string.Equals(Text(item, "enabled"), "false", StringComparison.OrdinalIgnoreCase),
                    Number(item, "webui_revision"),
                    DateTimeOffset.TryParse(Text(item, "webui_updated_at"), CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind, out var date) ? date : DateTimeOffset.MinValue));
            }
        }
        return new DownloaderOverrideSnapshot(1, Number(root, "webui_downloader_revision"), entries);
    }

    private async Task SaveAsync(YamlMappingNode root, CancellationToken token)
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        new YamlStream(new YamlDocument(root)).Save(writer, assignAnchors: false);
        if (File.Exists(_path))
            await DeploymentYamlConfiguration.WriteBackupAsync(
                _path, "downloaders", await File.ReadAllBytesAsync(_path, token).ConfigureAwait(false), token)
                .ConfigureAwait(false);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        await DeploymentYamlConfiguration.ReplaceAtomicallyAsync(_path, writer.ToString(), token).ConfigureAwait(false);
    }

    private static string? Text(YamlMappingNode map, string key) =>
        map.Children.TryGetValue(new YamlScalarNode(key), out var value)
            ? (value as YamlScalarNode)?.Value : null;
    private static long Number(YamlMappingNode map, string key) =>
        long.TryParse(Text(map, key), NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : 0;
    private static YamlMappingNode Map(YamlMappingNode root, string key)
    {
        var node = new YamlScalarNode(key);
        if (!root.Children.TryGetValue(node, out var value)) { value = new YamlMappingNode(); root.Add(node, value); }
        return value as YamlMappingNode ?? throw new DeploymentYamlException(key + " must be a mapping.");
    }
    private static void Set(YamlMappingNode map, string key, string value) =>
        map.Children[new YamlScalarNode(key)] = new YamlScalarNode(value) { Style = ScalarStyle.DoubleQuoted };
}
