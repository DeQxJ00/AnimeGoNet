using System.Globalization;
using System.Text;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace AnimeGoNet.App.Configuration;

// Callers hold DeploymentFileGate for the entire read/modify/backup/write operation.
internal static class ConfigurationYamlDocument
{
    public static async Task<YamlMappingNode> ReadAsync(string path, CancellationToken token)
    {
        if (!File.Exists(path))
            return new YamlMappingNode { { "version", DeploymentYamlConfiguration.CurrentVersion } };
        if (new FileInfo(path).Length is <= 0 or > 1024 * 1024)
            throw new DeploymentYamlException("Deployment YAML size is invalid.");
        var text = new UTF8Encoding(false, true).GetString(
            await File.ReadAllBytesAsync(path, token).ConfigureAwait(false));
        var stream = new YamlStream();
        stream.Load(new StringReader(text));
        if (stream.Documents.Count != 1 || stream.Documents[0].RootNode is not YamlMappingNode root)
            throw new DeploymentYamlException("Deployment YAML must contain one mapping.");
        var count = 0;
        Validate(root, 0, ref count);
        return root;
    }

    private static void Validate(YamlNode node, int depth, ref int count)
    {
        if (depth > 32 || ++count > 4096)
            throw new DeploymentYamlException("Deployment YAML structure is too large.");
        if (node is YamlMappingNode map)
        {
            foreach (var pair in map.Children)
            {
                if (pair.Key is not YamlScalarNode)
                    throw new DeploymentYamlException("Deployment YAML keys must be scalars.");
                Validate(pair.Value, depth + 1, ref count);
            }
        }
        else if (node is YamlSequenceNode sequence)
        {
            foreach (var child in sequence.Children) Validate(child, depth + 1, ref count);
        }
    }

    public static YamlNode? Get(YamlMappingNode root, string path)
    {
        YamlNode current = root;
        foreach (var part in path.Split(':'))
        {
            if (current is not YamlMappingNode map || !map.Children.TryGetValue(new YamlScalarNode(part), out var next))
                return null;
            current = next;
        }
        return current;
    }

    public static void Set(YamlMappingNode root, string path, YamlNode? value)
    {
        var parts = path.Split(':');
        var current = root;
        foreach (var part in parts[..^1])
        {
            var key = new YamlScalarNode(part);
            if (!current.Children.TryGetValue(key, out var child))
            {
                if (value is null) return;
                child = new YamlMappingNode();
                current.Add(key, child);
            }
            current = child as YamlMappingNode ?? throw new DeploymentYamlException(path + " must be a mapping.");
        }
        var leaf = new YamlScalarNode(parts[^1]);
        if (value is null) current.Children.Remove(leaf);
        else current.Children[leaf] = value;
    }

    public static YamlScalarNode Scalar(string value) => new(value) { Style = ScalarStyle.DoubleQuoted };

    public static string Render(YamlMappingNode root)
    {
        var count = 0;
        Validate(root, 0, ref count);
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        new YamlStream(new YamlDocument(root)).Save(writer, assignAnchors: false);
        var text = writer.ToString();
        if (Encoding.UTF8.GetByteCount(text) > 1024 * 1024)
            throw new DeploymentYamlException("Deployment YAML size is invalid.");
        return text;
    }

    public static async Task SaveAsync(string path, YamlMappingNode root, string backupKind, CancellationToken token)
    {
        if (File.Exists(path))
            await DeploymentYamlConfiguration.WriteBackupAsync(path, backupKind,
                await File.ReadAllBytesAsync(path, token).ConfigureAwait(false), token).ConfigureAwait(false);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await DeploymentYamlConfiguration.ReplaceAtomicallyAsync(path, Render(root), token).ConfigureAwait(false);
    }
}
