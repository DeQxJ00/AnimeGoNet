using System.Text.Json;
using System.Text.Json.Nodes;

namespace AnimeGoNet.App.Plugins;

public sealed record ExternalPluginSafeVars(
    JsonElement Value,
    IReadOnlyList<string> ConfiguredWriteOnlyPaths);

public static class ExternalPluginConfigurationSecrets
{
    private const int MaximumDepth = 16;

    public static ExternalPluginSafeVars ProjectSafe(
        JsonElement schema,
        JsonElement vars)
    {
        ExternalPluginConfigurationStore.ValidateObject(vars, "vars");
        var configured = new List<string>();
        var projected = ProjectObject(schema, vars, [], configured, 0);
        return new ExternalPluginSafeVars(
            ToElement(projected),
            configured.Order(StringComparer.Ordinal).ToArray());
    }

    public static JsonElement ProjectSafeSchema(JsonElement schema)
    {
        var projected = JsonNode.Parse(schema.GetRawText())
            ?? throw SchemaError("The plugin configuration schema is empty.");
        RemoveWriteOnlyDefaults(projected, 0);
        return ToElement(projected);
    }

    public static JsonElement MergeWriteOnly(
        JsonElement schema,
        JsonElement existingVars,
        JsonElement proposedVars,
        IReadOnlyList<string>? clearWriteOnlyPaths)
    {
        ExternalPluginConfigurationStore.ValidateObject(existingVars, "vars");
        ExternalPluginConfigurationStore.ValidateObject(proposedVars, "vars");
        var descriptors = new List<WriteOnlyDescriptor>();
        CollectWriteOnly(schema, [], descriptors, 0);
        var byPath = descriptors.ToDictionary(
            descriptor => descriptor.Pointer,
            StringComparer.Ordinal);
        var clear = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in clearWriteOnlyPaths ?? [])
        {
            if (string.IsNullOrWhiteSpace(path)
                || !clear.Add(path)
                || !byPath.ContainsKey(path))
            {
                throw new ExternalPluginConfigurationValidationException(
                    "plugin_config_clear_path_invalid",
                    path ?? string.Empty,
                    "Write-only clear paths must be unique paths declared by the plugin schema.");
            }
        }

        var existing = JsonNode.Parse(existingVars.GetRawText())!.AsObject();
        var proposed = JsonNode.Parse(proposedVars.GetRawText())!.AsObject();
        foreach (var descriptor in descriptors)
        {
            var proposedContains = TryGetNode(
                proposed,
                descriptor.Segments,
                out _);
            if (proposedContains && clear.Contains(descriptor.Pointer))
            {
                throw new ExternalPluginConfigurationValidationException(
                    "plugin_config_clear_path_conflict",
                    descriptor.Pointer,
                    "A write-only value cannot be replaced and cleared in one request.");
            }
            if (proposedContains || clear.Contains(descriptor.Pointer))
            {
                continue;
            }
            if (TryGetNode(existing, descriptor.Segments, out var retained))
            {
                SetNode(
                    proposed,
                    descriptor.Segments,
                    retained is null
                        ? null
                        : JsonNode.Parse(retained.ToJsonString()));
            }
        }
        var merged = ToElement(proposed);
        ExternalPluginConfigurationValidator.Validate(schema, merged);
        return merged;
    }

    private static JsonObject ProjectObject(
        JsonElement schema,
        JsonElement value,
        IReadOnlyList<string> path,
        List<string> configured,
        int depth)
    {
        if (depth > MaximumDepth)
        {
            throw SchemaError("The plugin configuration schema is nested too deeply.");
        }
        var result = new JsonObject();
        if (!schema.TryGetProperty("properties", out var properties))
        {
            return result;
        }
        if (properties.ValueKind != JsonValueKind.Object)
        {
            throw SchemaError("Schema keyword 'properties' must be an object.");
        }
        foreach (var property in properties.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object)
            {
                throw SchemaError("Every property schema must be an object.");
            }
            if (!value.TryGetProperty(property.Name, out var current))
            {
                continue;
            }
            var childPath = path.Append(property.Name).ToArray();
            if (IsWriteOnly(property.Value)
                || IsArrayWithWriteOnlyDescendant(property.Value))
            {
                configured.Add(ToPointer(childPath));
                continue;
            }
            if (current.ValueKind == JsonValueKind.Object
                && property.Value.TryGetProperty("properties", out _))
            {
                result[property.Name] = ProjectObject(
                    property.Value,
                    current,
                    childPath,
                    configured,
                    depth + 1);
            }
            else
            {
                result[property.Name] = JsonNode.Parse(current.GetRawText());
            }
        }
        return result;
    }

    private static void CollectWriteOnly(
        JsonElement schema,
        IReadOnlyList<string> path,
        List<WriteOnlyDescriptor> result,
        int depth)
    {
        if (depth > MaximumDepth)
        {
            throw SchemaError("The plugin configuration schema is nested too deeply.");
        }
        if (!schema.TryGetProperty("properties", out var properties))
        {
            return;
        }
        if (properties.ValueKind != JsonValueKind.Object)
        {
            throw SchemaError("Schema keyword 'properties' must be an object.");
        }
        foreach (var property in properties.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object)
            {
                throw SchemaError("Every property schema must be an object.");
            }
            var childPath = path.Append(property.Name).ToArray();
            if (IsWriteOnly(property.Value)
                || IsArrayWithWriteOnlyDescendant(property.Value))
            {
                result.Add(new WriteOnlyDescriptor(
                    ToPointer(childPath),
                    childPath));
                continue;
            }
            CollectWriteOnly(property.Value, childPath, result, depth + 1);
        }
    }

    private static bool IsWriteOnly(JsonElement schema)
    {
        if (!schema.TryGetProperty("writeOnly", out var writeOnly))
        {
            return false;
        }
        if (writeOnly.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw SchemaError("Schema keyword 'writeOnly' must be boolean.");
        }
        return writeOnly.GetBoolean();
    }

    private static bool IsArrayWithWriteOnlyDescendant(JsonElement schema)
    {
        if (!schema.TryGetProperty("type", out var type)
            || type.ValueKind != JsonValueKind.String
            || !string.Equals(type.GetString(), "array", StringComparison.Ordinal)
            || !schema.TryGetProperty("items", out var items)
            || items.ValueKind != JsonValueKind.Object)
        {
            return false;
        }
        return ContainsWriteOnly(items, 0);
    }

    private static bool ContainsWriteOnly(JsonElement schema, int depth)
    {
        if (depth > MaximumDepth)
        {
            throw SchemaError("The plugin configuration schema is nested too deeply.");
        }
        if (IsWriteOnly(schema))
        {
            return true;
        }
        if (schema.TryGetProperty("properties", out var properties))
        {
            if (properties.ValueKind != JsonValueKind.Object)
            {
                throw SchemaError("Schema keyword 'properties' must be an object.");
            }
            foreach (var property in properties.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Object)
                {
                    throw SchemaError("Every property schema must be an object.");
                }
                if (ContainsWriteOnly(property.Value, depth + 1))
                {
                    return true;
                }
            }
        }
        return schema.TryGetProperty("items", out var items)
            && items.ValueKind == JsonValueKind.Object
            && ContainsWriteOnly(items, depth + 1);
    }

    private static void RemoveWriteOnlyDefaults(JsonNode node, int depth)
    {
        if (depth > MaximumDepth)
        {
            throw SchemaError("The plugin configuration schema is nested too deeply.");
        }
        if (node is JsonObject value)
        {
            if (value["writeOnly"]?.GetValueKind() == JsonValueKind.True)
            {
                value.Remove("default");
                value.Remove("example");
                value.Remove("examples");
                value.Remove("const");
            }
            foreach (var child in value.ToArray())
            {
                if (child.Value is not null)
                {
                    RemoveWriteOnlyDefaults(child.Value, depth + 1);
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array)
            {
                if (child is not null)
                {
                    RemoveWriteOnlyDefaults(child, depth + 1);
                }
            }
        }
    }

    private static bool TryGetNode(
        JsonObject root,
        IReadOnlyList<string> segments,
        out JsonNode? value)
    {
        JsonObject current = root;
        for (var index = 0; index < segments.Count; index++)
        {
            if (!current.TryGetPropertyValue(segments[index], out value))
            {
                return false;
            }
            if (index == segments.Count - 1)
            {
                return true;
            }
            if (value is not JsonObject child)
            {
                value = null;
                return false;
            }
            current = child;
        }
        value = root;
        return segments.Count == 0;
    }

    private static void SetNode(
        JsonObject root,
        IReadOnlyList<string> segments,
        JsonNode? value)
    {
        JsonObject current = root;
        for (var index = 0; index < segments.Count - 1; index++)
        {
            if (!current.TryGetPropertyValue(segments[index], out var existing))
            {
                var created = new JsonObject();
                current[segments[index]] = created;
                current = created;
            }
            else if (existing is JsonObject child)
            {
                current = child;
            }
            else
            {
                throw new ExternalPluginConfigurationValidationException(
                    "plugin_config_invalid",
                    ToPointer(segments.Take(index + 1).ToArray()),
                    "A retained write-only value requires an object parent.");
            }
        }
        current[segments[^1]] = value;
    }

    private static JsonElement ToElement(JsonNode node)
    {
        using var document = JsonDocument.Parse(node.ToJsonString());
        return document.RootElement.Clone();
    }

    private static string ToPointer(IReadOnlyList<string> segments) =>
        "/" + string.Join(
            '/',
            segments.Select(segment => segment
                .Replace("~", "~0", StringComparison.Ordinal)
                .Replace("/", "~1", StringComparison.Ordinal)));

    private static ExternalPluginConfigurationValidationException SchemaError(
        string message) =>
        new("plugin_config_schema_invalid", "schema", message);

    private sealed record WriteOnlyDescriptor(
        string Pointer,
        IReadOnlyList<string> Segments);
}
