using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using AnimeGoNet.Core.Configuration;

namespace AnimeGoNet.App.Plugins;

public sealed class ExternalPluginManifestLoader
{
    private const int MaximumManifestBytes = 64 * 1024;
    private const int MaximumSchemaBytes = 256 * 1024;
    private const int MaximumCapabilities = 32;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly HashSet<string> ManifestFields = new(
        [
            "id",
            "name",
            "version",
            "apiVersion",
            "type",
            "rid",
            "entryPoint",
            "configSchema",
            "capabilities",
        ],
        StringComparer.Ordinal);
    private readonly string _pluginsRoot;
    private readonly string _expectedRid;

    public ExternalPluginManifestLoader(string pluginsRoot, string? expectedRid = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginsRoot);
        _pluginsRoot = Path.GetFullPath(pluginsRoot);
        _expectedRid = string.IsNullOrWhiteSpace(expectedRid)
            ? CurrentRid()
            : expectedRid.Trim().ToLowerInvariant();
        if (!ExternalPluginProtocol.SupportedRids.Contains(_expectedRid))
        {
            throw new ExternalPluginManifestException(
                "plugin_host_rid_unsupported",
                "The current host RID is not supported by the external plugin protocol.");
        }
    }

    public string ExpectedRid => _expectedRid;

    public async Task<ExternalPluginDiscoveryResult> DiscoverAsync(
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_pluginsRoot))
        {
            return new ExternalPluginDiscoveryResult([], []);
        }

        DirectoryInfo[] directories;
        try
        {
            directories = new DirectoryInfo(_pluginsRoot)
                .EnumerateDirectories()
                .OrderBy(directory => directory.Name, StringComparer.Ordinal)
                .ToArray();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return new ExternalPluginDiscoveryResult(
                [],
                [new ExternalPluginPackageError(
                    ".",
                    "plugin_root_unreadable",
                    "The external plugin root could not be enumerated.")]);
        }

        var packages = new List<ExternalPluginPackage>(directories.Length);
        var errors = new List<ExternalPluginPackageError>();
        foreach (var directory in directories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                packages.Add(await LoadPackageAsync(
                    directory.FullName,
                    cancellationToken).ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (ExternalPluginManifestException exception)
            {
                errors.Add(new ExternalPluginPackageError(
                    directory.Name,
                    exception.Code,
                    exception.Message));
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or JsonException
                    or DecoderFallbackException
                    or ArgumentException)
            {
                errors.Add(new ExternalPluginPackageError(
                    directory.Name,
                    "plugin_package_unreadable",
                    "The external plugin package could not be read safely."));
            }
        }

        var duplicateIds = packages
            .GroupBy(package => package.Manifest.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);
        if (duplicateIds.Count > 0)
        {
            foreach (var package in packages.Where(package =>
                         duplicateIds.Contains(package.Manifest.Id)))
            {
                errors.Add(new ExternalPluginPackageError(
                    Path.GetFileName(package.DirectoryPath),
                    "plugin_id_duplicate",
                    "External plugin IDs must be unique across package directories."));
            }
            packages.RemoveAll(package => duplicateIds.Contains(package.Manifest.Id));
        }

        return new ExternalPluginDiscoveryResult(
            packages
                .OrderBy(package => package.Manifest.Id, StringComparer.Ordinal)
                .ToArray(),
            errors
                .OrderBy(error => error.PackageDirectoryName, StringComparer.Ordinal)
                .ThenBy(error => error.Code, StringComparer.Ordinal)
                .ToArray());
    }

    public async Task<ExternalPluginPackage> LoadPackageAsync(
        string packageDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageDirectory);
        var packagePath = Path.GetFullPath(packageDirectory);
        EnsureDirectChildPackage(packagePath);
        EnsureNotLink(packagePath);
        if (!Directory.Exists(packagePath))
        {
            throw new ExternalPluginManifestException(
                "plugin_manifest_missing",
                "The package does not contain plugin.json.");
        }
        EnsureSafePermissions(packagePath, isExecutable: false);

        var manifestPath = Path.Combine(packagePath, "plugin.json");
        if (!File.Exists(manifestPath))
        {
            throw new ExternalPluginManifestException(
                "plugin_manifest_missing",
                "The package does not contain plugin.json.");
        }
        EnsureNotLink(manifestPath);
        EnsureSafePermissions(manifestPath, isExecutable: false);
        var manifest = ParseManifest(await ReadBoundedAsync(
            manifestPath,
            MaximumManifestBytes,
            "plugin_manifest_size_invalid",
            cancellationToken).ConfigureAwait(false));
        ValidateManifest(manifest);

        var entryPointPath = ResolvePackageFile(
            packagePath,
            manifest.EntryPoint,
            "plugin_entry_point_invalid");
        if (!File.Exists(entryPointPath))
        {
            throw new ExternalPluginManifestException(
                "plugin_entry_point_missing",
                "The manifest entry point does not exist.");
        }
        EnsureLinkFreePath(packagePath, entryPointPath);
        EnsureSafePermissions(entryPointPath, isExecutable: true);
        if (manifest.Rid.StartsWith("win-", StringComparison.Ordinal)
            && !string.Equals(
                Path.GetExtension(entryPointPath),
                ".exe",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ExternalPluginManifestException(
                "plugin_entry_point_invalid",
                "Windows external plugin entry points must use the .exe extension.");
        }

        var schemaPath = ResolvePackageFile(
            packagePath,
            manifest.ConfigSchema,
            "plugin_config_schema_invalid");
        if (!File.Exists(schemaPath))
        {
            throw new ExternalPluginManifestException(
                "plugin_config_schema_missing",
                "The manifest config schema does not exist.");
        }
        if (!string.Equals(Path.GetExtension(schemaPath), ".json", StringComparison.OrdinalIgnoreCase))
        {
            throw new ExternalPluginManifestException(
                "plugin_config_schema_invalid",
                "The plugin config schema must be a JSON file.");
        }
        EnsureLinkFreePath(packagePath, schemaPath);
        EnsureSafePermissions(schemaPath, isExecutable: false);
        ValidateSchema(await ReadBoundedAsync(
            schemaPath,
            MaximumSchemaBytes,
            "plugin_config_schema_size_invalid",
            cancellationToken).ConfigureAwait(false));

        return new ExternalPluginPackage(
            manifest,
            packagePath,
            manifestPath,
            entryPointPath,
            schemaPath);
    }

    private void EnsureDirectChildPackage(string packagePath)
    {
        if (!PathBoundary.IsWithin(_pluginsRoot, packagePath)
            || PathsEqual(_pluginsRoot, packagePath))
        {
            throw new ExternalPluginManifestException(
                "plugin_package_path_invalid",
                "Plugin packages must be direct children of the configured plugin root.");
        }
        var relative = Path.GetRelativePath(_pluginsRoot, packagePath);
        if (relative is "." or ".."
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || relative.Contains(Path.DirectorySeparatorChar)
            || relative.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new ExternalPluginManifestException(
                "plugin_package_path_invalid",
                "Plugin packages must be direct children of the configured plugin root.");
        }
    }

    private void ValidateManifest(ExternalPluginManifest manifest)
    {
        if (!IsReverseDomainId(manifest.Id))
        {
            throw new ExternalPluginManifestException(
                "plugin_id_invalid",
                "External plugin IDs must be lowercase reverse-domain identifiers.");
        }
        if (manifest.Name.Length is < 1 or > 128
            || !string.Equals(manifest.Name, manifest.Name.Trim(), StringComparison.Ordinal))
        {
            throw new ExternalPluginManifestException(
                "plugin_name_invalid",
                "External plugin names must contain 1 to 128 trimmed characters.");
        }
        if (!IsSemVer(manifest.Version))
        {
            throw new ExternalPluginManifestException(
                "plugin_version_invalid",
                "External plugin versions must use strict semantic versioning.");
        }
        if (manifest.ApiVersion != ExternalPluginProtocol.CurrentApiVersion)
        {
            throw new ExternalPluginManifestException(
                "plugin_api_version_unsupported",
                "The external plugin API version is not supported by this host.");
        }
        if (!ExternalPluginProtocol.SupportedTypes.Contains(manifest.Type))
        {
            throw new ExternalPluginManifestException(
                "plugin_type_invalid",
                "The external plugin type is not supported.");
        }
        if (!ExternalPluginProtocol.SupportedRids.Contains(manifest.Rid))
        {
            throw new ExternalPluginManifestException(
                "plugin_rid_unsupported",
                "The external plugin RID is not supported.");
        }
        if (!string.Equals(manifest.Rid, _expectedRid, StringComparison.Ordinal))
        {
            throw new ExternalPluginManifestException(
                "plugin_rid_mismatch",
                "The external plugin RID does not match the current host.");
        }
        if (manifest.Capabilities.Count > MaximumCapabilities
            || manifest.Capabilities.Any(capability => !IsStableToken(capability))
            || manifest.Capabilities.Distinct(StringComparer.Ordinal).Count()
                != manifest.Capabilities.Count)
        {
            throw new ExternalPluginManifestException(
                "plugin_capabilities_invalid",
                "External plugin capabilities must be unique lowercase stable tokens.");
        }
    }

    private static ExternalPluginManifest ParseManifest(ReadOnlyMemory<byte> bytes)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(bytes, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 16,
            });
        }
        catch (JsonException exception)
        {
            throw new ExternalPluginManifestException(
                "plugin_manifest_json_invalid",
                "plugin.json must contain strict JSON.",
                exception);
        }
        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ExternalPluginManifestException(
                    "plugin_manifest_json_invalid",
                    "plugin.json must contain one object document.");
            }

            var properties = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!ManifestFields.Contains(property.Name))
                {
                    throw new ExternalPluginManifestException(
                        "plugin_manifest_unknown_field",
                        "plugin.json contains an unsupported field.");
                }
                if (!properties.TryAdd(property.Name, property.Value))
                {
                    throw new ExternalPluginManifestException(
                        "plugin_manifest_duplicate_field",
                        "plugin.json contains a duplicate field.");
                }
            }

            return new ExternalPluginManifest(
                RequiredString(properties, "id", 128),
                RequiredString(properties, "name", 128),
                RequiredString(properties, "version", 64),
                RequiredInteger(properties, "apiVersion"),
                RequiredString(properties, "type", 32),
                RequiredString(properties, "rid", 32),
                RequiredString(properties, "entryPoint", 256),
                RequiredString(properties, "configSchema", 256),
                RequiredCapabilities(properties));
        }
    }

    private static string RequiredString(
        Dictionary<string, JsonElement> properties,
        string name,
        int maximumLength)
    {
        if (!properties.TryGetValue(name, out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            throw new ExternalPluginManifestException(
                "plugin_manifest_field_invalid",
                $"plugin.json field '{name}' must be a string.");
        }
        var value = property.GetString();
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > maximumLength
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            throw new ExternalPluginManifestException(
                "plugin_manifest_field_invalid",
                $"plugin.json field '{name}' is empty, untrimmed or too long.");
        }
        return value;
    }

    private static int RequiredInteger(
        Dictionary<string, JsonElement> properties,
        string name)
    {
        if (!properties.TryGetValue(name, out var property)
            || property.ValueKind != JsonValueKind.Number
            || !property.TryGetInt32(out var value))
        {
            throw new ExternalPluginManifestException(
                "plugin_manifest_field_invalid",
                $"plugin.json field '{name}' must be an integer.");
        }
        return value;
    }

    private static List<string> RequiredCapabilities(
        Dictionary<string, JsonElement> properties)
    {
        if (!properties.TryGetValue("capabilities", out var property)
            || property.ValueKind != JsonValueKind.Array)
        {
            throw new ExternalPluginManifestException(
                "plugin_manifest_field_invalid",
                "plugin.json field 'capabilities' must be an array.");
        }
        var result = new List<string>();
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(item.GetString()))
            {
                throw new ExternalPluginManifestException(
                    "plugin_manifest_field_invalid",
                    "plugin.json capabilities must contain strings.");
            }
            result.Add(item.GetString()!);
            if (result.Count > MaximumCapabilities)
            {
                throw new ExternalPluginManifestException(
                    "plugin_capabilities_invalid",
                    "plugin.json declares too many capabilities.");
            }
        }
        return result;
    }

    private static void ValidateSchema(ReadOnlyMemory<byte> bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 32,
            });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ExternalPluginManifestException(
                    "plugin_config_schema_invalid",
                    "The plugin config schema must contain one JSON object.");
            }
            EnsureUniqueJsonProperties(document.RootElement);
            ExternalPluginConfigurationValidator.ValidateSchemaDefinition(
                document.RootElement);
        }
        catch (ExternalPluginConfigurationValidationException exception)
        {
            throw new ExternalPluginManifestException(
                "plugin_config_schema_invalid",
                exception.Message,
                exception);
        }
        catch (JsonException exception)
        {
            throw new ExternalPluginManifestException(
                "plugin_config_schema_invalid",
                "The plugin config schema must contain strict JSON.",
                exception);
        }
    }

    private static void EnsureUniqueJsonProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new ExternalPluginManifestException(
                        "plugin_config_schema_invalid",
                        "The plugin config schema contains a duplicate property.");
                }
                EnsureUniqueJsonProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                EnsureUniqueJsonProperties(item);
            }
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(
        string filePath,
        int maximumBytes,
        string sizeErrorCode,
        CancellationToken cancellationToken)
    {
        var info = new FileInfo(filePath);
        if (!info.Exists || info.Length is <= 0 || info.Length > maximumBytes)
        {
            throw new ExternalPluginManifestException(
                sizeErrorCode,
                $"The file must contain 1 to {maximumBytes.ToString(CultureInfo.InvariantCulture)} bytes.");
        }
        var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);
        if (bytes.Length is <= 0 || bytes.Length > maximumBytes)
        {
            throw new ExternalPluginManifestException(
                sizeErrorCode,
                $"The file must contain 1 to {maximumBytes.ToString(CultureInfo.InvariantCulture)} bytes.");
        }
        try
        {
            _ = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new ExternalPluginManifestException(
                "plugin_file_utf8_invalid",
                "External plugin metadata files must use valid UTF-8.",
                exception);
        }
        return bytes;
    }

    private static string ResolvePackageFile(
        string packagePath,
        string relativePath,
        string errorCode)
    {
        if (PathBoundary.IsAbsolute(relativePath)
            || relativePath.Contains(':'))
        {
            throw new ExternalPluginManifestException(
                errorCode,
                "Plugin file paths must be relative to the package directory.");
        }
        var segments = relativePath.Split(['/', '\\']);
        if (segments.Length == 0
            || segments.Any(segment =>
                string.IsNullOrWhiteSpace(segment) || segment is "." or ".."))
        {
            throw new ExternalPluginManifestException(
                errorCode,
                "Plugin file paths must use non-empty child segments.");
        }
        var candidate = Path.GetFullPath(Path.Combine(
            packagePath,
            Path.Combine(segments)));
        if (!PathBoundary.IsWithin(packagePath, candidate)
            || PathsEqual(packagePath, candidate))
        {
            throw new ExternalPluginManifestException(
                errorCode,
                "Plugin file paths must remain inside the package directory.");
        }
        return candidate;
    }

    private static void EnsureLinkFreePath(string packagePath, string candidate)
    {
        EnsureNotLink(packagePath);
        var relative = Path.GetRelativePath(packagePath, candidate);
        var current = packagePath;
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, segment);
            EnsureNotLink(current);
            EnsureSafePermissions(current, isExecutable: false);
        }
    }

    private static void EnsureNotLink(string path)
    {
        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(path);
        }
        catch (FileNotFoundException)
        {
            return;
        }
        catch (DirectoryNotFoundException)
        {
            return;
        }
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new ExternalPluginManifestException(
                "plugin_path_link_disallowed",
                "External plugin package paths cannot contain symbolic links or reparse points.");
        }
    }

    private static void EnsureSafePermissions(string path, bool isExecutable)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        var mode = File.GetUnixFileMode(path);
        if ((mode & (UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)) != 0)
        {
            throw new ExternalPluginManifestException(
                "plugin_permissions_unsafe",
                "External plugin package paths cannot be group- or world-writable.");
        }
        if (isExecutable
            && (mode & (UnixFileMode.UserExecute
                | UnixFileMode.GroupExecute
                | UnixFileMode.OtherExecute)) == 0)
        {
            throw new ExternalPluginManifestException(
                "plugin_entry_point_not_executable",
                "The external plugin entry point is not executable.");
        }
    }

    private static bool IsReverseDomainId(string value)
    {
        if (value.Length is < 3 or > 128
            || !string.Equals(value, value.ToLowerInvariant(), StringComparison.Ordinal))
        {
            return false;
        }
        var segments = value.Split('.');
        return segments.Length >= 2 && segments.All(IsDomainSegment);
    }

    private static bool IsDomainSegment(string segment)
    {
        if (segment.Length == 0
            || segment[0] is not (>= 'a' and <= 'z')
            || segment[^1] == '-')
        {
            return false;
        }
        return segment.All(character =>
            character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');
    }

    private static bool IsStableToken(string value)
    {
        if (value.Length is < 1 or > 128
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || !string.Equals(value, value.ToLowerInvariant(), StringComparison.Ordinal)
            || value[0] is not (>= 'a' and <= 'z' or >= '0' and <= '9'))
        {
            return false;
        }
        var previousSeparator = false;
        foreach (var character in value)
        {
            var separator = character is '.' or '-' or '_';
            if (!separator
                && character is not (>= 'a' and <= 'z' or >= '0' and <= '9'))
            {
                return false;
            }
            if (separator && previousSeparator)
            {
                return false;
            }
            previousSeparator = separator;
        }
        return !previousSeparator;
    }

    private static bool IsSemVer(string value)
    {
        if (value.Length is < 5 or > 64 || value.Any(char.IsWhiteSpace))
        {
            return false;
        }
        var buildIndex = value.IndexOf('+', StringComparison.Ordinal);
        if (buildIndex >= 0
            && (value[(buildIndex + 1)..].Contains('+')
                || !ValidIdentifiers(
                    value[(buildIndex + 1)..],
                    numericLeadingZeroAllowed: true)))
        {
            return false;
        }
        var coreAndPre = buildIndex >= 0 ? value[..buildIndex] : value;
        var prereleaseIndex = coreAndPre.IndexOf('-', StringComparison.Ordinal);
        if (prereleaseIndex >= 0
            && !ValidIdentifiers(
                coreAndPre[(prereleaseIndex + 1)..],
                numericLeadingZeroAllowed: false))
        {
            return false;
        }
        var coreText = prereleaseIndex >= 0
            ? coreAndPre[..prereleaseIndex]
            : coreAndPre;
        var core = coreText.Split('.');
        return core.Length == 3 && core.All(ValidCoreNumber);
    }

    private static bool ValidCoreNumber(string value) =>
        value.Length > 0
        && (value.Length == 1 || value[0] != '0')
        && value.All(character => character is >= '0' and <= '9')
        && uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _);

    private static bool ValidIdentifiers(string value, bool numericLeadingZeroAllowed)
    {
        var identifiers = value.Split('.');
        return identifiers.All(identifier =>
            identifier.Length > 0
            && identifier.All(character =>
                character is >= 'A' and <= 'Z'
                    or >= 'a' and <= 'z'
                    or >= '0' and <= '9'
                    or '-')
            && (numericLeadingZeroAllowed
                || !identifier.All(character => character is >= '0' and <= '9')
                || identifier.Length == 1
                || identifier[0] != '0'));
    }

    private static string CurrentRid()
    {
        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => string.Empty,
        };
        if (architecture.Length == 0)
        {
            return "unsupported";
        }
        if (OperatingSystem.IsWindows())
        {
            return $"win-{architecture}";
        }
        if (OperatingSystem.IsLinux())
        {
            return $"linux-{architecture}";
        }
        if (OperatingSystem.IsMacOS() && architecture == "arm64")
        {
            return "osx-arm64";
        }
        return "unsupported";
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
}
