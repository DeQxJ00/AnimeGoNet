using System.Globalization;
using System.Text;
using System.Text.Json;
using AnimeGoNet.Core.Configuration;
using Microsoft.Extensions.Configuration;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace AnimeGoNet.App.Configuration;

internal sealed record LegacyConfigurationWriteResult(
    string FilePath,
    string? BackupFilePath);

internal sealed class LegacyDeploymentConfigurationFile(
    string filePath,
    AnimeGoOptions currentDeployment,
    AnimeGoOptions platformDefaults,
    bool runningInContainer) : IDisposable
{
    private const int MaximumBytes = 1024 * 1024;
    private const int MaximumDepth = 32;
    private const int MaximumNodes = 4096;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _filePath = Path.GetFullPath(filePath);

    public async Task<byte[]> ReadRawAsync(
        bool useDefaults,
        CancellationToken cancellationToken = default)
    {
        if (useDefaults || !File.Exists(_filePath))
        {
            return StrictUtf8.GetBytes(DeploymentYamlConfiguration.RenderDefault(
                useDefaults ? platformDefaults : currentDeployment));
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var info = new FileInfo(_filePath);
            if (!info.Exists)
            {
                return StrictUtf8.GetBytes(DeploymentYamlConfiguration.RenderDefault(
                    currentDeployment));
            }
            if (info.Length is <= 0 or > MaximumBytes)
            {
                throw new DeploymentYamlException(
                    $"Deployment YAML must contain 1 to {MaximumBytes} bytes.");
            }

            var bytes = await File.ReadAllBytesAsync(_filePath, cancellationToken)
                .ConfigureAwait(false);
            _ = StrictUtf8.GetString(bytes);
            return bytes;
        }
        catch (DecoderFallbackException exception)
        {
            throw new DeploymentYamlException(
                "Deployment YAML must use valid UTF-8.",
                exception);
        }
        catch (DeploymentYamlException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            throw new DeploymentYamlException(
                "Deployment YAML could not be read.",
                exception);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<JsonElement> ReadJsonAsync(
        bool useDefaults,
        CancellationToken cancellationToken = default)
    {
        var bytes = await ReadRawAsync(useDefaults, cancellationToken).ConfigureAwait(false);
        return YamlToJson(bytes);
    }

    public async Task<LegacyConfigurationWriteResult> WriteRawAsync(
        ReadOnlyMemory<byte> content,
        bool backup,
        CancellationToken cancellationToken = default)
    {
        if (content.Length is <= 0 or > MaximumBytes)
        {
            throw new DeploymentYamlException(
                $"Deployment YAML must contain 1 to {MaximumBytes} bytes.");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? temporaryPath = null;
        try
        {
            var parent = Path.GetDirectoryName(_filePath)
                ?? throw new DeploymentYamlException(
                    "Deployment YAML path has no parent directory.");
            Directory.CreateDirectory(parent);
            temporaryPath = Path.Combine(
                parent,
                $".{Path.GetFileName(_filePath)}.api-{Guid.NewGuid():N}.tmp");
            await WriteExclusiveAsync(temporaryPath, content, cancellationToken)
                .ConfigureAwait(false);
            var snapshot = await DeploymentYamlConfiguration.LoadOrCreateAsync(
                temporaryPath,
                platformDefaults,
                backupLegacy: false,
                cancellationToken).ConfigureAwait(false);
            ValidateTyped(snapshot.Values);

            var normalized = await File.ReadAllBytesAsync(temporaryPath, cancellationToken)
                .ConfigureAwait(false);
            var normalizedText = StrictUtf8.GetString(normalized);
            string? backupFilePath = null;
            if (backup && File.Exists(_filePath))
            {
                var original = await File.ReadAllBytesAsync(_filePath, cancellationToken)
                    .ConfigureAwait(false);
                backupFilePath = await DeploymentYamlConfiguration.WriteBackupAsync(
                    _filePath,
                    "api",
                    original,
                    cancellationToken).ConfigureAwait(false);
            }

            await DeploymentYamlConfiguration.ReplaceAtomicallyAsync(
                _filePath,
                normalizedText,
                cancellationToken).ConfigureAwait(false);
            return new LegacyConfigurationWriteResult(_filePath, backupFilePath);
        }
        catch (DecoderFallbackException exception)
        {
            throw new DeploymentYamlException(
                "Deployment YAML must use valid UTF-8.",
                exception);
        }
        finally
        {
            if (temporaryPath is not null)
            {
                TryDelete(temporaryPath);
            }
            _gate.Release();
        }
    }

    public Task<LegacyConfigurationWriteResult> WriteJsonAsync(
        JsonElement document,
        bool backup,
        CancellationToken cancellationToken = default)
    {
        if (document.ValueKind != JsonValueKind.Object)
        {
            throw new DeploymentYamlException(
                "Configuration JSON must contain one object document.");
        }

        var yaml = JsonToYaml(document);
        return WriteRawAsync(yaml, backup, cancellationToken);
    }

    public void Dispose() => _gate.Dispose();

    private void ValidateTyped(IReadOnlyDictionary<string, string?> values)
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(values);
        var candidate = AnimeGoApplication.LoadOptions(configuration, runningInContainer);
        var errors = AnimeGoOptionsValidator.Validate(candidate);
        if (errors.Count > 0)
        {
            throw new DeploymentYamlException(
                "Invalid AnimeGoNet configuration: " + string.Join("; ", errors));
        }
    }

    private static JsonElement YamlToJson(ReadOnlyMemory<byte> content)
    {
        string text;
        try
        {
            text = StrictUtf8.GetString(content.Span);
        }
        catch (DecoderFallbackException exception)
        {
            throw new DeploymentYamlException(
                "Deployment YAML must use valid UTF-8.",
                exception);
        }

        var yaml = new YamlStream();
        try
        {
            using var reader = new StringReader(text);
            yaml.Load(reader);
        }
        catch (Exception exception) when (
            exception is YamlException or ArgumentException or InvalidOperationException)
        {
            throw new DeploymentYamlException(
                "Deployment YAML syntax is invalid.",
                exception);
        }
        if (yaml.Documents.Count != 1
            || yaml.Documents[0].RootNode is not YamlMappingNode root)
        {
            throw new DeploymentYamlException(
                "Deployment YAML must contain exactly one mapping document.");
        }

        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output))
        {
            var nodeCount = 0;
            WriteJsonNode(writer, root, 0, ref nodeCount);
        }
        using var document = JsonDocument.Parse(output.ToArray());
        return document.RootElement.Clone();
    }

    private static byte[] JsonToYaml(JsonElement document)
    {
        var nodeCount = 0;
        var root = ReadJsonNode(document, 0, ref nodeCount);
        var stream = new YamlStream(new YamlDocument(root));
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        stream.Save(writer, assignAnchors: false);
        var bytes = StrictUtf8.GetBytes(writer.ToString());
        if (bytes.Length is <= 0 or > MaximumBytes)
        {
            throw new DeploymentYamlException(
                $"Deployment YAML must contain 1 to {MaximumBytes} bytes.");
        }
        return bytes;
    }

    private static void WriteJsonNode(
        Utf8JsonWriter writer,
        YamlNode node,
        int depth,
        ref int nodeCount)
    {
        CountNode(depth, ref nodeCount);
        switch (node)
        {
            case YamlMappingNode mapping:
                writer.WriteStartObject();
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var pair in mapping.Children)
                {
                    if (pair.Key is not YamlScalarNode { Value: { Length: > 0 } name }
                        || !names.Add(name))
                    {
                        throw new DeploymentYamlException(
                            "Deployment YAML mapping keys must be unique non-empty scalars.");
                    }
                    writer.WritePropertyName(name);
                    WriteJsonNode(writer, pair.Value, depth + 1, ref nodeCount);
                }
                writer.WriteEndObject();
                break;
            case YamlSequenceNode sequence:
                writer.WriteStartArray();
                foreach (var child in sequence.Children)
                {
                    WriteJsonNode(writer, child, depth + 1, ref nodeCount);
                }
                writer.WriteEndArray();
                break;
            case YamlScalarNode scalar:
                WriteJsonScalar(writer, scalar);
                break;
            default:
                throw new DeploymentYamlException(
                    "Deployment YAML contains an unsupported node type.");
        }
    }

    private static void WriteJsonScalar(Utf8JsonWriter writer, YamlScalarNode scalar)
    {
        if (scalar.Value is null)
        {
            writer.WriteNullValue();
            return;
        }
        if (scalar.Style == ScalarStyle.Plain)
        {
            if (bool.TryParse(scalar.Value, out var boolean))
            {
                writer.WriteBooleanValue(boolean);
                return;
            }
            if (long.TryParse(
                    scalar.Value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var integer))
            {
                writer.WriteNumberValue(integer);
                return;
            }
            if (double.TryParse(
                    scalar.Value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var number)
                && double.IsFinite(number))
            {
                writer.WriteNumberValue(number);
                return;
            }
            if (string.Equals(scalar.Value, "null", StringComparison.OrdinalIgnoreCase)
                || scalar.Value == "~")
            {
                writer.WriteNullValue();
                return;
            }
        }
        writer.WriteStringValue(scalar.Value);
    }

    private static YamlNode ReadJsonNode(
        JsonElement element,
        int depth,
        ref int nodeCount)
    {
        CountNode(depth, ref nodeCount);
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var mapping = new YamlMappingNode();
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in element.EnumerateObject())
                {
                    if (string.IsNullOrWhiteSpace(property.Name)
                        || !names.Add(property.Name))
                    {
                        throw new DeploymentYamlException(
                            "Configuration JSON mapping keys must be unique and non-empty.");
                    }
                    mapping.Add(
                        new YamlScalarNode(property.Name) { Style = ScalarStyle.SingleQuoted },
                        ReadJsonNode(property.Value, depth + 1, ref nodeCount));
                }
                return mapping;
            case JsonValueKind.Array:
                var sequence = new YamlSequenceNode();
                foreach (var item in element.EnumerateArray())
                {
                    sequence.Add(ReadJsonNode(item, depth + 1, ref nodeCount));
                }
                return sequence;
            case JsonValueKind.String:
                return new YamlScalarNode(element.GetString())
                {
                    Style = ScalarStyle.SingleQuoted,
                };
            case JsonValueKind.Number:
                return new YamlScalarNode(element.GetRawText())
                {
                    Style = ScalarStyle.Plain,
                };
            case JsonValueKind.True:
            case JsonValueKind.False:
                return new YamlScalarNode(element.GetBoolean() ? "true" : "false")
                {
                    Style = ScalarStyle.Plain,
                };
            case JsonValueKind.Null:
                return new YamlScalarNode("null")
                {
                    Style = ScalarStyle.Plain,
                };
            default:
                throw new DeploymentYamlException(
                    "Configuration JSON contains an unsupported value.");
        }
    }

    private static void CountNode(int depth, ref int nodeCount)
    {
        nodeCount++;
        if (depth > MaximumDepth || nodeCount > MaximumNodes)
        {
            throw new DeploymentYamlException(
                "Configuration document is too deeply nested or contains too many nodes.");
        }
    }

    private static async Task WriteExclusiveAsync(
        string filePath,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            BufferSize = 16 * 1024,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
        };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        await using var stream = new FileStream(filePath, options);
        await stream.WriteAsync(content, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }

    private static void TryDelete(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // A unique validation temp cannot replace the authoritative config.
        }
    }
}
