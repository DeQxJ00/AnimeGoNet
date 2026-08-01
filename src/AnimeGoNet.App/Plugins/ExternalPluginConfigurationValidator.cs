using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AnimeGoNet.App.Plugins;

public sealed class ExternalPluginConfigurationValidationException(
    string code,
    string path,
    string message,
    Exception? innerException = null) : ArgumentException(message, innerException)
{
    public string Code { get; } = code;

    public string Path { get; } = path;
}

public sealed partial class ExternalPluginConfigurationValidator
{
    private const int MaximumSchemaBytes = 256 * 1024;
    private const int MaximumDepth = 16;

    public async Task ValidateVarsAsync(
        ExternalPluginPackage package,
        JsonElement vars,
        CancellationToken cancellationToken = default)
    {
        ExternalPluginConfigurationStore.ValidateObject(vars, "vars");
        var schema = await LoadSchemaAsync(package, cancellationToken).ConfigureAwait(false);
        ValidateNode(schema, vars, "vars", 0);
    }

    public async Task<JsonElement> LoadSchemaAsync(
        ExternalPluginPackage package,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        var info = new FileInfo(package.ConfigSchemaPath);
        if (!info.Exists || info.Length is <= 0 or > MaximumSchemaBytes)
        {
            throw SchemaError("The plugin configuration schema has an invalid size.");
        }
        var bytes = await File.ReadAllBytesAsync(package.ConfigSchemaPath, cancellationToken)
            .ConfigureAwait(false);
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
            throw SchemaError(
                "The plugin configuration schema must contain strict JSON.",
                exception);
        }
        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw SchemaError("The plugin configuration schema must be an object.");
            }
            if (document.RootElement.TryGetProperty("type", out var rootType)
                && (rootType.ValueKind != JsonValueKind.String
                    || !string.Equals(
                        rootType.GetString(),
                        "object",
                        StringComparison.Ordinal)))
            {
                throw SchemaError("The plugin configuration schema root type must be object.");
            }
            EnsureUniqueProperties(document.RootElement);
            ValidateSchemaDefinition(document.RootElement);
            return document.RootElement.Clone();
        }
    }

    internal static void Validate(JsonElement schema, JsonElement vars)
    {
        ExternalPluginConfigurationStore.ValidateObject(vars, "vars");
        ValidateNode(schema, vars, "vars", 0);
    }

    internal static void ValidateSchemaDefinition(
        JsonElement schema,
        bool requireObjectRoot = true,
        int depth = 0)
    {
        if (depth > MaximumDepth || schema.ValueKind != JsonValueKind.Object)
        {
            throw SchemaError("The plugin configuration schema is invalid or nested too deeply.");
        }
        string? declaredType = null;
        if (schema.TryGetProperty("type", out var type))
        {
            if (type.ValueKind != JsonValueKind.String
                || type.GetString() is not (
                    "object" or "array" or "string" or "integer" or "number" or "boolean" or "null"))
            {
                throw SchemaError("Schema contains an unsupported type.");
            }
            declaredType = type.GetString();
        }
        if (requireObjectRoot && declaredType is not (null or "object"))
        {
            throw SchemaError("The plugin configuration schema root type must be object.");
        }
        if (schema.TryGetProperty("writeOnly", out var writeOnly)
            && writeOnly.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw SchemaError("Schema keyword 'writeOnly' must be boolean.");
        }
        if (schema.TryGetProperty("enum", out var choices)
            && (choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0))
        {
            throw SchemaError("Schema keyword 'enum' must be a non-empty array.");
        }
        if (schema.TryGetProperty("properties", out var properties))
        {
            if (properties.ValueKind != JsonValueKind.Object)
            {
                throw SchemaError("Schema keyword 'properties' must be an object.");
            }
            foreach (var property in properties.EnumerateObject())
            {
                ValidateSchemaDefinition(property.Value, requireObjectRoot: false, depth + 1);
            }
        }
        if (schema.TryGetProperty("required", out var required))
        {
            if (required.ValueKind != JsonValueKind.Array)
            {
                throw SchemaError("Schema keyword 'required' must be an array.");
            }
            var requiredNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in required.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(item.GetString())
                    || !requiredNames.Add(item.GetString()!))
                {
                    throw SchemaError("Schema required fields must be unique strings.");
                }
                if (schema.TryGetProperty("properties", out properties)
                    && !properties.TryGetProperty(item.GetString()!, out _))
                {
                    throw SchemaError("Schema required fields must be declared properties.");
                }
            }
        }
        if (schema.TryGetProperty("additionalProperties", out var additional))
        {
            if (additional.ValueKind == JsonValueKind.Object)
            {
                ValidateSchemaDefinition(additional, requireObjectRoot: false, depth + 1);
            }
            else if (additional.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                throw SchemaError(
                    "Schema keyword 'additionalProperties' must be boolean or object.");
            }
        }
        if (schema.TryGetProperty("items", out var items))
        {
            ValidateSchemaDefinition(items, requireObjectRoot: false, depth + 1);
        }
        var minLength = OptionalNonNegativeInteger(schema, "minLength");
        var maxLength = OptionalNonNegativeInteger(schema, "maxLength");
        var minItems = OptionalNonNegativeInteger(schema, "minItems");
        var maxItems = OptionalNonNegativeInteger(schema, "maxItems");
        if (minLength > maxLength || minItems > maxItems)
        {
            throw SchemaError("Schema minimum constraints cannot exceed maximum constraints.");
        }
        var minimum = OptionalFiniteNumber(schema, "minimum");
        var maximum = OptionalFiniteNumber(schema, "maximum");
        if (minimum > maximum)
        {
            throw SchemaError("Schema minimum cannot exceed maximum.");
        }
        if (schema.TryGetProperty("pattern", out var pattern))
        {
            if (pattern.ValueKind != JsonValueKind.String)
            {
                throw SchemaError("Schema keyword 'pattern' must be a string.");
            }
            try
            {
                _ = new Regex(
                    pattern.GetString()!,
                    RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
                    TimeSpan.FromMilliseconds(250));
            }
            catch (ArgumentException exception)
            {
                throw SchemaError("Schema contains an invalid string pattern.", exception);
            }
        }
    }

    private static void ValidateNode(
        JsonElement schema,
        JsonElement value,
        string path,
        int depth)
    {
        if (depth > MaximumDepth)
        {
            throw SchemaError("The plugin configuration schema is nested too deeply.");
        }
        ValidateType(schema, value, path);
        ValidateEnum(schema, value, path);
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                ValidateObject(schema, value, path, depth);
                break;
            case JsonValueKind.Array:
                ValidateArray(schema, value, path, depth);
                break;
            case JsonValueKind.String:
                ValidateString(schema, value.GetString()!, path);
                break;
            case JsonValueKind.Number:
                ValidateNumber(schema, value, path);
                break;
        }
    }

    private static void ValidateType(
        JsonElement schema,
        JsonElement value,
        string path)
    {
        if (!schema.TryGetProperty("type", out var type))
        {
            return;
        }
        if (type.ValueKind != JsonValueKind.String)
        {
            throw SchemaError("Schema keyword 'type' must be a string.");
        }
        var expected = type.GetString();
        var valid = expected switch
        {
            "object" => value.ValueKind == JsonValueKind.Object,
            "array" => value.ValueKind == JsonValueKind.Array,
            "string" => value.ValueKind == JsonValueKind.String,
            "integer" => value.ValueKind == JsonValueKind.Number
                && value.TryGetInt64(out _),
            "number" => value.ValueKind == JsonValueKind.Number,
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "null" => value.ValueKind == JsonValueKind.Null,
            _ => throw SchemaError("Schema contains an unsupported type."),
        };
        if (!valid)
        {
            throw ValueError(path, $"Expected {expected}.");
        }
    }

    private static void ValidateEnum(
        JsonElement schema,
        JsonElement value,
        string path)
    {
        if (!schema.TryGetProperty("enum", out var choices))
        {
            return;
        }
        if (choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
        {
            throw SchemaError("Schema keyword 'enum' must be a non-empty array.");
        }
        if (!choices.EnumerateArray().Any(choice => JsonElement.DeepEquals(choice, value)))
        {
            throw ValueError(path, "Value is not one of the allowed choices.");
        }
    }

    private static void ValidateObject(
        JsonElement schema,
        JsonElement value,
        string path,
        int depth)
    {
        var propertySchemas = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
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
                propertySchemas.Add(property.Name, property.Value);
            }
        }
        if (schema.TryGetProperty("required", out var required))
        {
            if (required.ValueKind != JsonValueKind.Array)
            {
                throw SchemaError("Schema keyword 'required' must be an array.");
            }
            var requiredNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in required.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(item.GetString())
                    || !requiredNames.Add(item.GetString()!))
                {
                    throw SchemaError("Schema required fields must be unique strings.");
                }
                if (!value.TryGetProperty(item.GetString()!, out _))
                {
                    throw ValueError(ChildPath(path, item.GetString()!), "Value is required.");
                }
            }
        }

        var additionalAllowed = true;
        JsonElement? additionalSchema = null;
        if (schema.TryGetProperty("additionalProperties", out var additional))
        {
            if (additional.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                additionalAllowed = additional.GetBoolean();
            }
            else if (additional.ValueKind == JsonValueKind.Object)
            {
                additionalSchema = additional;
            }
            else
            {
                throw SchemaError(
                    "Schema keyword 'additionalProperties' must be boolean or object.");
            }
        }
        foreach (var property in value.EnumerateObject())
        {
            if (propertySchemas.TryGetValue(property.Name, out var propertySchema))
            {
                ValidateNode(
                    propertySchema,
                    property.Value,
                    ChildPath(path, property.Name),
                    depth + 1);
            }
            else if (additionalSchema is { } extraSchema)
            {
                ValidateNode(
                    extraSchema,
                    property.Value,
                    ChildPath(path, property.Name),
                    depth + 1);
            }
            else if (!additionalAllowed)
            {
                throw ValueError(
                    ChildPath(path, property.Name),
                    "Additional value is not allowed.");
            }
        }
    }

    private static void ValidateArray(
        JsonElement schema,
        JsonElement value,
        string path,
        int depth)
    {
        var length = value.GetArrayLength();
        ValidateIntegerKeyword(schema, "minItems", minimum =>
        {
            if (length < minimum)
            {
                throw ValueError(path, $"At least {minimum} items are required.");
            }
        });
        ValidateIntegerKeyword(schema, "maxItems", maximum =>
        {
            if (length > maximum)
            {
                throw ValueError(path, $"At most {maximum} items are allowed.");
            }
        });
        if (!schema.TryGetProperty("items", out var items))
        {
            return;
        }
        if (items.ValueKind != JsonValueKind.Object)
        {
            throw SchemaError("Schema keyword 'items' must be an object.");
        }
        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            ValidateNode(items, item, $"{path}[{index}]", depth + 1);
            index++;
        }
    }

    private static void ValidateString(JsonElement schema, string value, string path)
    {
        ValidateIntegerKeyword(schema, "minLength", minimum =>
        {
            if (value.Length < minimum)
            {
                throw ValueError(path, $"Minimum length is {minimum}.");
            }
        });
        ValidateIntegerKeyword(schema, "maxLength", maximum =>
        {
            if (value.Length > maximum)
            {
                throw ValueError(path, $"Maximum length is {maximum}.");
            }
        });
        if (!schema.TryGetProperty("pattern", out var pattern))
        {
            return;
        }
        if (pattern.ValueKind != JsonValueKind.String)
        {
            throw SchemaError("Schema keyword 'pattern' must be a string.");
        }
        Regex expression;
        try
        {
            expression = new Regex(
                pattern.GetString()!,
                RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
                TimeSpan.FromMilliseconds(250));
        }
        catch (ArgumentException exception)
        {
            throw SchemaError("Schema contains an invalid string pattern.", exception);
        }
        if (!expression.IsMatch(value))
        {
            throw ValueError(path, "Value does not match the required pattern.");
        }
    }

    private static void ValidateNumber(
        JsonElement schema,
        JsonElement value,
        string path)
    {
        if (!value.TryGetDouble(out var number) || !double.IsFinite(number))
        {
            throw ValueError(path, "Number must be finite.");
        }
        ValidateNumberKeyword(schema, "minimum", minimum =>
        {
            if (number < minimum)
            {
                throw ValueError(path, $"Minimum value is {minimum.ToString(CultureInfo.InvariantCulture)}.");
            }
        });
        ValidateNumberKeyword(schema, "maximum", maximum =>
        {
            if (number > maximum)
            {
                throw ValueError(path, $"Maximum value is {maximum.ToString(CultureInfo.InvariantCulture)}.");
            }
        });
    }

    private static void ValidateIntegerKeyword(
        JsonElement schema,
        string name,
        Action<int> validate)
    {
        if (!schema.TryGetProperty(name, out var property))
        {
            return;
        }
        if (property.ValueKind != JsonValueKind.Number
            || !property.TryGetInt32(out var value)
            || value < 0)
        {
            throw SchemaError($"Schema keyword '{name}' must be a non-negative integer.");
        }
        validate(value);
    }

    private static void ValidateNumberKeyword(
        JsonElement schema,
        string name,
        Action<double> validate)
    {
        if (!schema.TryGetProperty(name, out var property))
        {
            return;
        }
        if (property.ValueKind != JsonValueKind.Number
            || !property.TryGetDouble(out var value)
            || !double.IsFinite(value))
        {
            throw SchemaError($"Schema keyword '{name}' must be a finite number.");
        }
        validate(value);
    }

    private static int? OptionalNonNegativeInteger(JsonElement schema, string name)
    {
        if (!schema.TryGetProperty(name, out var property))
        {
            return null;
        }
        if (property.ValueKind != JsonValueKind.Number
            || !property.TryGetInt32(out var value)
            || value < 0)
        {
            throw SchemaError($"Schema keyword '{name}' must be a non-negative integer.");
        }
        return value;
    }

    private static double? OptionalFiniteNumber(JsonElement schema, string name)
    {
        if (!schema.TryGetProperty(name, out var property))
        {
            return null;
        }
        if (property.ValueKind != JsonValueKind.Number
            || !property.TryGetDouble(out var value)
            || !double.IsFinite(value))
        {
            throw SchemaError($"Schema keyword '{name}' must be a finite number.");
        }
        return value;
    }

    private static string ChildPath(string path, string propertyName) =>
        $"{path}/{propertyName.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal)}";

    private static ExternalPluginConfigurationValidationException ValueError(
        string path,
        string message) =>
        new("plugin_config_invalid", path, message);

    private static ExternalPluginConfigurationValidationException SchemaError(
        string message,
        Exception? innerException = null) =>
        new("plugin_config_schema_invalid", "schema", message, innerException);

    private static void EnsureUniqueProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw SchemaError(
                        "The plugin configuration schema contains a duplicate property.");
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
}
