using System.Collections.Frozen;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AnimeGoNet.Core.Configuration;

namespace AnimeGoNet.App.Plugins;

public sealed record ExternalPluginConfigurationEntry(
    bool Enabled,
    JsonElement Args,
    JsonElement Vars,
    long Revision,
    DateTimeOffset UpdatedAtUtc);

public sealed record ExternalPluginConfigurationSnapshot(
    int FormatVersion,
    long Revision,
    IReadOnlyDictionary<string, ExternalPluginConfigurationEntry> Plugins);

public sealed class ExternalPluginConfigurationRevisionException : InvalidOperationException;

public sealed class ExternalPluginConfigurationStore : IDisposable
{
    private const int CurrentFormatVersion = 1;
    private const int MaximumFileBytes = 1024 * 1024;
    private const int MaximumValueBytes = 64 * 1024;
    private const int MaximumValueDepth = 16;
    private static readonly FrozenSet<string> SnapshotFields =
        new[] { "format_version", "revision", "plugins" }
            .ToFrozenSet(StringComparer.Ordinal);
    private static readonly FrozenSet<string> EntryFields =
        new[] { "enabled", "args", "vars", "revision", "updated_at_utc" }
            .ToFrozenSet(StringComparer.Ordinal);
    private static readonly JsonElement EmptyObject = CreateEmptyObject();
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ExternalPluginConfigurationSnapshot _current = EmptySnapshot();

    public ExternalPluginConfigurationStore(string configurationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationPath);
        _path = Path.Combine(
            Path.GetFullPath(configurationPath),
            "external-plugins.private.json");
    }

    public ExternalPluginConfigurationSnapshot Current => Volatile.Read(ref _current);

    public string FilePath => _path;

    public void Dispose() => _gate.Dispose();

    public ExternalPluginConfigurationEntry GetOrDefault(string pluginId)
    {
        RequireCanonicalPluginId(pluginId);
        return Current.Plugins.TryGetValue(pluginId, out var entry)
            ? entry
            : new ExternalPluginConfigurationEntry(
                false,
                EmptyObject,
                EmptyObject,
                0,
                DateTimeOffset.MinValue);
    }

    public async Task<ExternalPluginConfigurationSnapshot> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var snapshot = await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _current, snapshot);
            return snapshot;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ExternalPluginConfigurationSnapshot> UpsertAsync(
        string pluginId,
        bool enabled,
        JsonElement args,
        JsonElement vars,
        long expectedRevision,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        RequireCanonicalPluginId(pluginId);
        ValidateObject(args, "args");
        ValidateObject(vars, "vars");
        ArgumentOutOfRangeException.ThrowIfNegative(expectedRevision);
        if (nowUtc == default)
        {
            throw new ArgumentException("Update time is required.", nameof(nowUtc));
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
            if (current.Revision != expectedRevision)
            {
                throw new ExternalPluginConfigurationRevisionException();
            }
            var entries = new Dictionary<string, ExternalPluginConfigurationEntry>(
                current.Plugins,
                StringComparer.Ordinal)
            {
                [pluginId] = new ExternalPluginConfigurationEntry(
                    enabled,
                    args.Clone(),
                    vars.Clone(),
                    current.Plugins.TryGetValue(pluginId, out var existing)
                        ? checked(existing.Revision + 1)
                        : 1,
                    nowUtc.ToUniversalTime()),
            };
            var saved = new ExternalPluginConfigurationSnapshot(
                CurrentFormatVersion,
                checked(current.Revision + 1),
                entries.ToFrozenDictionary(StringComparer.Ordinal));
            await SaveCoreAsync(saved, cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _current, saved);
            return saved;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ExternalPluginConfigurationSnapshot> DeleteAsync(
        string pluginId,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        RequireCanonicalPluginId(pluginId);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedRevision);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
            if (current.Revision != expectedRevision)
            {
                throw new ExternalPluginConfigurationRevisionException();
            }
            var entries = new Dictionary<string, ExternalPluginConfigurationEntry>(
                current.Plugins,
                StringComparer.Ordinal);
            if (!entries.Remove(pluginId))
            {
                throw new KeyNotFoundException("External plugin configuration was not found.");
            }
            var saved = new ExternalPluginConfigurationSnapshot(
                CurrentFormatVersion,
                checked(current.Revision + 1),
                entries.ToFrozenDictionary(StringComparer.Ordinal));
            await SaveCoreAsync(saved, cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _current, saved);
            return saved;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ExternalPluginConfigurationSnapshot> LoadCoreAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return EmptySnapshot();
        }
        var info = new FileInfo(_path);
        if (info.Length is <= 0 or > MaximumFileBytes)
        {
            throw new InvalidOperationException(
                "External plugin private configuration has an invalid size.");
        }
        var bytes = await File.ReadAllBytesAsync(_path, cancellationToken)
            .ConfigureAwait(false);
        if (bytes.Length is <= 0 or > MaximumFileBytes)
        {
            throw new InvalidOperationException(
                "External plugin private configuration has an invalid size.");
        }
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(bytes, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 32,
            });
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "External plugin private configuration must contain strict JSON.",
                exception);
        }
        using (document)
        {
            EnsureUniqueProperties(document.RootElement);
            ValidateDocumentShape(document.RootElement);
        }
        ExternalPluginConfigurationSnapshot snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize(
                bytes,
                ExternalPluginConfigurationJsonContext.Default
                    .ExternalPluginConfigurationSnapshot)
                ?? throw new InvalidOperationException(
                    "External plugin private configuration is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "External plugin private configuration is invalid.",
                exception);
        }
        if (snapshot.FormatVersion != CurrentFormatVersion || snapshot.Revision < 0)
        {
            throw new InvalidOperationException(
                "External plugin private configuration version or revision is invalid.");
        }
        var entries = new Dictionary<string, ExternalPluginConfigurationEntry>(
            StringComparer.Ordinal);
        if (snapshot.Plugins is null)
        {
            throw new InvalidOperationException(
                "External plugin private configuration plugins are missing.");
        }
        foreach (var (pluginId, entry) in snapshot.Plugins)
        {
            RequireCanonicalPluginId(pluginId);
            if (entry is null || entry.Revision < 1 || entry.UpdatedAtUtc == default)
            {
                throw new InvalidOperationException(
                    "External plugin configuration entry metadata is invalid.");
            }
            ValidateObject(entry.Args, "args");
            ValidateObject(entry.Vars, "vars");
            entries.Add(pluginId, entry with
            {
                Args = entry.Args.Clone(),
                Vars = entry.Vars.Clone(),
                UpdatedAtUtc = entry.UpdatedAtUtc.ToUniversalTime(),
            });
        }
        return snapshot with
        {
            Plugins = entries.ToFrozenDictionary(StringComparer.Ordinal),
        };
    }

    private async Task SaveCoreAsync(
        ExternalPluginConfigurationSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            snapshot,
            ExternalPluginConfigurationJsonContext.Default
                .ExternalPluginConfigurationSnapshot);
        if (bytes.Length > MaximumFileBytes)
        {
            throw new InvalidOperationException(
                "External plugin private configuration is too large.");
        }
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(
            directory,
            $".external-plugins.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    temporary,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static ExternalPluginConfigurationSnapshot EmptySnapshot() =>
        new(
            CurrentFormatVersion,
            0,
            FrozenDictionary<string, ExternalPluginConfigurationEntry>.Empty);

    private static JsonElement CreateEmptyObject()
    {
        using var document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }

    private static void RequireCanonicalPluginId(string pluginId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        if (!AnimeGoOptionsValidator.IsStableId(pluginId)
            || !pluginId.Equals(
                pluginId.Trim().ToLowerInvariant(),
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Plugin ID must be canonical lowercase stable ID.",
                nameof(pluginId));
        }
    }

    internal static void ValidateObject(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException($"Plugin {name} must be a JSON object.", name);
        }
        if (Encoding.UTF8.GetByteCount(value.GetRawText()) > MaximumValueBytes)
        {
            throw new ArgumentException($"Plugin {name} is too large.", name);
        }
        ValidateUniqueValueProperties(value, name);
        ValidateDepth(value, 0, name);
    }

    private static void ValidateUniqueValueProperties(JsonElement value, string name)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new ArgumentException(
                        $"Plugin {name} contains a duplicate property.",
                        name);
                }
                ValidateUniqueValueProperties(property.Value, name);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                ValidateUniqueValueProperties(item, name);
            }
        }
    }

    private static void ValidateDepth(JsonElement value, int depth, string name)
    {
        if (depth > MaximumValueDepth)
        {
            throw new ArgumentException($"Plugin {name} is nested too deeply.", name);
        }
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in value.EnumerateObject())
            {
                ValidateDepth(property.Value, depth + 1, name);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                ValidateDepth(item, depth + 1, name);
            }
        }
    }

    private static void EnsureUniqueProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new InvalidOperationException(
                        "External plugin private configuration contains a duplicate property.");
                }
                EnsureUniqueProperties(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                EnsureUniqueProperties(item);
            }
        }
    }

    private static void ValidateDocumentShape(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                "External plugin private configuration must contain one object.");
        }
        foreach (var property in root.EnumerateObject())
        {
            if (!SnapshotFields.Contains(property.Name))
            {
                throw new InvalidOperationException(
                    "External plugin private configuration contains an unsupported field.");
            }
        }
        if (!root.TryGetProperty("plugins", out var plugins)
            || plugins.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                "External plugin private configuration plugins must be an object.");
        }
        foreach (var plugin in plugins.EnumerateObject())
        {
            if (plugin.Value.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException(
                    "External plugin private configuration entries must be objects.");
            }
            foreach (var property in plugin.Value.EnumerateObject())
            {
                if (!EntryFields.Contains(property.Name))
                {
                    throw new InvalidOperationException(
                        "External plugin private configuration entry contains an unsupported field.");
                }
            }
        }
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    WriteIndented = true,
    GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(ExternalPluginConfigurationSnapshot))]
internal sealed partial class ExternalPluginConfigurationJsonContext : JsonSerializerContext;
