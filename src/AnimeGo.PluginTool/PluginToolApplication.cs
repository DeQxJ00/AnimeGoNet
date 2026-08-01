using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using AnimeGoNet.App.Plugins;

namespace AnimeGo.PluginTool;

internal sealed class PluginToolApplication(
    IPluginToolSessionFactory? sessionFactory = null,
    PluginPackageAuditor? auditor = null,
    PluginPackagePacker? packer = null)
{
    private const int MaximumFixtureBytes = 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly IPluginToolSessionFactory _sessionFactory =
        sessionFactory ?? new PluginToolSessionFactory();
    private readonly PluginPackageAuditor _auditor = auditor ?? new PluginPackageAuditor();
    private readonly PluginPackagePacker _packer = packer ?? new PluginPackagePacker();

    public async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        PluginToolCommand? command = null;
        try
        {
            command = PluginToolCommandParser.Parse(arguments);
            if (command.Kind == PluginToolCommandKind.Help)
            {
                await output.WriteLineAsync(PluginToolCommandParser.Usage).ConfigureAwait(false);
                return 0;
            }
            return command.Kind switch
            {
                PluginToolCommandKind.Validate => await ValidateAsync(
                    command,
                    output,
                    cancellationToken).ConfigureAwait(false),
                PluginToolCommandKind.Run => await RunFixtureAsync(
                    command,
                    output,
                    cancellationToken).ConfigureAwait(false),
                PluginToolCommandKind.Pack => await PackAsync(
                    command,
                    output,
                    cancellationToken).ConfigureAwait(false),
                _ => throw new InvalidOperationException("Unsupported PluginTool command."),
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return await WriteErrorAsync(
                error,
                command,
                "plugin_tool_canceled",
                "The operation was canceled.",
                130).ConfigureAwait(false);
        }
        catch (PluginToolException exception)
        {
            return await WriteErrorAsync(
                error,
                command,
                exception.Code,
                exception.Message,
                exception.ExitCode).ConfigureAwait(false);
        }
        catch (ExternalPluginManifestException exception)
        {
            return await WriteErrorAsync(
                error,
                command,
                exception.Code,
                exception.Message,
                3).ConfigureAwait(false);
        }
        catch (ExternalPluginConfigurationValidationException exception)
        {
            return await WriteErrorAsync(
                error,
                command,
                exception.Code,
                "The fixture configuration does not satisfy the plugin schema.",
                4).ConfigureAwait(false);
        }
        catch (ExternalPluginProtocolException exception)
        {
            return await WriteErrorAsync(
                error,
                command,
                exception.Code,
                "The plugin process or typed result validation failed.",
                5).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return await WriteErrorAsync(
                error,
                command,
                exception is JsonException ? "plugin_fixture_invalid" : "plugin_tool_io_failed",
                exception is JsonException
                    ? "The fixture must contain strict supported JSON."
                    : "The operation could not access one of its files.",
                exception is JsonException ? 4 : 6).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return await WriteErrorAsync(
                error,
                command,
                "plugin_tool_unexpected",
                "The plugin tool failed unexpectedly.",
                5).ConfigureAwait(false);
        }
    }

    private async Task<int> ValidateAsync(
        PluginToolCommand command,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var (_, audited) = await LoadAsync(command, cancellationToken).ConfigureAwait(false);
        await WriteAsync(
            output,
            new PluginValidateOutput(true, "validate", audited.ToOutput()),
            PluginToolJsonContext.Default.PluginValidateOutput).ConfigureAwait(false);
        return 0;
    }

    private async Task<int> RunFixtureAsync(
        PluginToolCommand command,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var (loader, audited) = await LoadAsync(command, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(audited.Package.Manifest.Rid, CurrentRid(), StringComparison.Ordinal))
        {
            throw new PluginToolException(
                "plugin_run_rid_mismatch",
                "The plugin can only run on a matching host RID.",
                5);
        }
        var fixture = await ReadFixtureAsync(
                command.FixturePath!,
                audited.Package.Manifest.Type,
                cancellationToken)
            .ConfigureAwait(false);
        try
        {
            ExternalPluginRequestValidator.Validate(
                audited.Package.Manifest.Type,
                fixture.Payload);
        }
        catch (ExternalPluginProtocolException exception)
        {
            throw new PluginToolException(
                exception.Code,
                "The fixture payload does not match the typed plugin contract.",
                4,
                exception);
        }
        await new ExternalPluginConfigurationValidator()
            .ValidateVarsAsync(audited.Package, fixture.Config, cancellationToken)
            .ConfigureAwait(false);
        var (dataPath, ownedDataPath) = PrepareDataPath(
            command.DataPath,
            audited.Package.DirectoryPath);
        try
        {
            await using var session = _sessionFactory.Create(
                loader,
                audited.Package,
                dataPath,
                command.ExecuteTimeout);
            await session.StartAsync(cancellationToken).ConfigureAwait(false);
            var result = await session.ExecuteAsync(
                fixture.Operation!,
                fixture.Payload,
                fixture.Config,
                cancellationToken).ConfigureAwait(false);
            ExternalPluginResultValidator.Validate(
                audited.Package.Manifest.Type,
                result,
                fixture.Payload);
            var healthy = await session.HealthAsync(cancellationToken).ConfigureAwait(false);
            if (!healthy)
            {
                throw new PluginToolException(
                    "plugin_health_unhealthy",
                    "The plugin reported an unhealthy state after the fixture.",
                    5);
            }
            await session.ShutdownAsync(cancellationToken).ConfigureAwait(false);
            await WriteAsync(
                output,
                new PluginRunOutput(
                    true,
                    "run",
                    audited.ToOutput(),
                    true,
                    result.Clone()),
                PluginToolJsonContext.Default.PluginRunOutput).ConfigureAwait(false);
            return 0;
        }
        finally
        {
            if (ownedDataPath)
            {
                DeleteOwnedDataPath(dataPath);
            }
        }
    }

    private async Task<int> PackAsync(
        PluginToolCommand command,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var (_, audited) = await LoadAsync(command, cancellationToken).ConfigureAwait(false);
        var archive = await _packer.PackAsync(
            audited,
            command.OutputPath!,
            command.Force,
            cancellationToken).ConfigureAwait(false);
        await WriteAsync(
            output,
            new PluginPackOutput(
                true,
                "pack",
                audited.ToOutput(),
                archive.OutputPath,
                archive.Length,
                archive.Sha256),
            PluginToolJsonContext.Default.PluginPackOutput).ConfigureAwait(false);
        return 0;
    }

    private async Task<(ExternalPluginManifestLoader Loader, AuditedPluginPackage Package)> LoadAsync(
        PluginToolCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            var packageDirectory = Path.GetFullPath(command.PackageDirectory!);
            var root = Path.GetDirectoryName(packageDirectory)
                ?? throw new PluginToolException(
                    "plugin_package_path_invalid",
                    "The plugin package directory has no parent.",
                    3);
            var loader = new ExternalPluginManifestLoader(root, command.ExpectedRid);
            var package = await loader.LoadPackageAsync(packageDirectory, cancellationToken)
                .ConfigureAwait(false);
            var audited = await _auditor.AuditAsync(package, cancellationToken)
                .ConfigureAwait(false);
            return (loader, audited);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException)
        {
            throw new PluginToolException(
                "plugin_package_unreadable",
                "The plugin package path or tree could not be read safely.",
                3,
                exception);
        }
    }

    private static async Task<PluginRunFixture> ReadFixtureAsync(
        string path,
        string pluginType,
        CancellationToken cancellationToken)
    {
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception exception) when (
            exception is IOException or ArgumentException or NotSupportedException)
        {
            throw new PluginToolException(
                "plugin_fixture_path_invalid",
                "The fixture path is invalid.",
                4,
                exception);
        }
        var info = new FileInfo(fullPath);
        if (!info.Exists || info.Length is <= 0 or > MaximumFixtureBytes)
        {
            throw new PluginToolException(
                "plugin_fixture_size_invalid",
                "The fixture must contain 1 byte to 1 MiB.",
                4);
        }
        await using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length != info.Length)
        {
            throw new PluginToolException(
                "plugin_fixture_changed",
                "The fixture changed while it was being read.",
                4);
        }
        var bytes = new byte[checked((int)info.Length)];
        var offset = 0;
        while (offset < bytes.Length)
        {
            var read = await stream.ReadAsync(
                    bytes.AsMemory(offset),
                    cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                throw new PluginToolException(
                    "plugin_fixture_changed",
                    "The fixture changed while it was being read.",
                    4);
            }
            offset += read;
        }
        var trailing = new byte[1];
        if (await stream.ReadAsync(trailing, cancellationToken).ConfigureAwait(false) != 0
            || stream.Length != info.Length)
        {
            throw new PluginToolException(
                "plugin_fixture_changed",
                "The fixture changed while it was being read.",
                4);
        }
        try
        {
            _ = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new PluginToolException(
                "plugin_fixture_utf8_invalid",
                "The fixture must use valid UTF-8.",
                4,
                exception);
        }
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 32,
        });
        EnsureUniqueProperties(document.RootElement);
        var fixture = JsonSerializer.Deserialize(
            document.RootElement,
            PluginToolJsonContext.Default.PluginRunFixture);
        if (fixture is null
            || string.IsNullOrWhiteSpace(fixture.Operation)
            || fixture.Operation.Length > 128
            || fixture.Payload.ValueKind != JsonValueKind.Object
            || fixture.Config.ValueKind != JsonValueKind.Object)
        {
            throw new PluginToolException(
                "plugin_fixture_invalid",
                "The fixture operation, payload or config is invalid.",
                4);
        }
        var expectedOperation = ExternalPluginOperations.ForType(pluginType);
        if (!string.Equals(fixture.Operation, expectedOperation, StringComparison.Ordinal))
        {
            throw new PluginToolException(
                "plugin_fixture_operation_mismatch",
                "The fixture operation does not match the plugin type.",
                4);
        }
        return fixture;
    }

    private static void EnsureUniqueProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new PluginToolException(
                        "plugin_fixture_duplicate_field",
                        "The fixture contains a duplicate JSON field.",
                        4);
                }
                EnsureUniqueProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                EnsureUniqueProperties(item);
            }
        }
    }

    private static (string Path, bool Owned) PrepareDataPath(
        string? configuredPath,
        string packagePath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var explicitPath = Path.GetFullPath(configuredPath);
            var packageRoot = Path.GetFullPath(packagePath);
            if (PathsEqual(packageRoot, explicitPath) || IsWithin(packageRoot, explicitPath))
            {
                throw new PluginToolException(
                    "plugin_data_path_inside_package",
                    "The plugin data path must be outside the package directory.",
                    5);
            }
            Directory.CreateDirectory(explicitPath);
            return (explicitPath, false);
        }
        var temporaryRoot = Path.GetFullPath(Path.GetTempPath());
        var ownedPath = Path.GetFullPath(Path.Combine(
            temporaryRoot,
            $"AnimeGoPluginTool-{Guid.NewGuid():N}"));
        if (!IsWithin(temporaryRoot, ownedPath))
        {
            throw new PluginToolException(
                "plugin_data_path_invalid",
                "The temporary plugin data path is invalid.",
                5);
        }
        Directory.CreateDirectory(ownedPath);
        return (ownedPath, true);
    }

    private static void DeleteOwnedDataPath(string path)
    {
        var temporaryRoot = Path.GetFullPath(Path.GetTempPath());
        var resolved = Path.GetFullPath(path);
        if (!IsWithin(temporaryRoot, resolved)
            || !Path.GetFileName(resolved).StartsWith("AnimeGoPluginTool-", StringComparison.Ordinal))
        {
            return;
        }
        if (Directory.Exists(resolved))
        {
            Directory.Delete(resolved, recursive: true);
        }
    }

    private static bool IsWithin(string root, string candidate)
    {
        var relative = Path.GetRelativePath(root, candidate);
        return relative != "."
            && !Path.IsPathRooted(relative)
            && relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);

    private static string CurrentRid()
    {
        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => "unsupported",
        };
        if (OperatingSystem.IsWindows())
        {
            return $"win-{architecture}";
        }
        if (OperatingSystem.IsLinux())
        {
            return $"linux-{architecture}";
        }
        if (OperatingSystem.IsMacOS())
        {
            return $"osx-{architecture}";
        }
        return "unsupported";
    }

    private static async Task WriteAsync<T>(
        TextWriter writer,
        T value,
        JsonTypeInfo<T> typeInfo)
    {
        await writer.WriteLineAsync(JsonSerializer.Serialize(value, typeInfo)).ConfigureAwait(false);
        await writer.FlushAsync().ConfigureAwait(false);
    }

    private static async Task<int> WriteErrorAsync(
        TextWriter writer,
        PluginToolCommand? command,
        string code,
        string message,
        int exitCode)
    {
        var commandName = command?.Kind.ToString().ToLowerInvariant() ?? "unknown";
        await WriteAsync(
            writer,
            new PluginToolErrorOutput(false, commandName, code, message),
            PluginToolJsonContext.Default.PluginToolErrorOutput).ConfigureAwait(false);
        return exitCode;
    }
}
