using System.Collections.Frozen;
using System.Text.Json;

namespace AnimeGoNet.App.Plugins;

public sealed record ExternalPluginConfigurationView(
    ExternalPluginPackage Package,
    bool Configured,
    bool Enabled,
    long EntryRevision,
    DateTimeOffset? UpdatedAtUtc,
    JsonElement Args,
    ExternalPluginSafeVars Vars,
    JsonElement Schema);

public sealed class ExternalPluginConfigurationService
{
    private readonly FrozenDictionary<string, ExternalPluginPackage> _packages;
    private readonly ExternalPluginManifestLoader _loader;
    private readonly ExternalPluginConfigurationValidator _validator;
    private readonly ExternalPluginConfigurationStore _store;
    private readonly ExternalPluginHostManager _manager;
    private readonly TimeProvider _timeProvider;

    public ExternalPluginConfigurationService(
        ExternalPluginDiscoveryResult discovery,
        ExternalPluginManifestLoader loader,
        ExternalPluginConfigurationValidator validator,
        ExternalPluginConfigurationStore store,
        ExternalPluginHostManager manager,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(manager);
        _packages = discovery.Packages.ToFrozenDictionary(
            package => package.Manifest.Id,
            StringComparer.Ordinal);
        _loader = loader;
        _validator = validator;
        _store = store;
        _manager = manager;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public ExternalPluginConfigurationSnapshot Current => _store.Current;

    public ExternalPluginConfigurationEntry GetOrDefault(string pluginId) =>
        _store.GetOrDefault(pluginId);

    public async Task<IReadOnlyList<ExternalPluginConfigurationView>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var result = new List<ExternalPluginConfigurationView>(_packages.Count);
        foreach (var package in _packages.Values.OrderBy(
                     package => package.Manifest.Id,
                     StringComparer.Ordinal))
        {
            result.Add(await GetViewCoreAsync(package, cancellationToken)
                .ConfigureAwait(false));
        }
        return result;
    }

    public Task<ExternalPluginConfigurationView> GetAsync(
        string pluginId,
        CancellationToken cancellationToken = default) =>
        GetViewCoreAsync(GetRequiredPackage(pluginId), cancellationToken);

    public async Task<ExternalPluginConfigurationSnapshot> SaveSafeAsync(
        string pluginId,
        bool enabled,
        JsonElement args,
        JsonElement vars,
        IReadOnlyList<string>? clearWriteOnlyPaths,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        var package = GetRequiredPackage(pluginId);
        var currentPackage = await ReloadRequiredAsync(package, cancellationToken)
            .ConfigureAwait(false);
        var schema = await _validator.LoadSchemaAsync(currentPackage, cancellationToken)
            .ConfigureAwait(false);
        var existing = _store.GetOrDefault(pluginId);
        var mergedVars = ExternalPluginConfigurationSecrets.MergeWriteOnly(
            schema,
            existing.Vars,
            vars,
            clearWriteOnlyPaths);
        return await SaveAsync(
            pluginId,
            enabled,
            args,
            mergedVars,
            expectedRevision,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<ExternalPluginConfigurationSnapshot> SaveAsync(
        string pluginId,
        bool enabled,
        JsonElement args,
        JsonElement vars,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        var package = GetRequiredPackage(pluginId);
        ExternalPluginConfigurationStore.ValidateObject(args, "args");
        ExternalPluginConfigurationStore.ValidateObject(vars, "vars");
        var currentPackage = await ReloadRequiredAsync(package, cancellationToken)
            .ConfigureAwait(false);
        await _validator.ValidateVarsAsync(currentPackage, vars, cancellationToken)
            .ConfigureAwait(false);
        var saved = await _store.UpsertAsync(
            pluginId,
            enabled,
            args,
            vars,
            expectedRevision,
            _timeProvider.GetUtcNow(),
            cancellationToken).ConfigureAwait(false);
        await _manager.ResetAsync(pluginId, CancellationToken.None).ConfigureAwait(false);
        return saved;
    }

    public async Task<ExternalPluginConfigurationSnapshot> DeleteAsync(
        string pluginId,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        _ = GetRequiredPackage(pluginId);
        var saved = await _store.DeleteAsync(
            pluginId,
            expectedRevision,
            cancellationToken).ConfigureAwait(false);
        await _manager.ResetAsync(pluginId, CancellationToken.None).ConfigureAwait(false);
        return saved;
    }

    private ExternalPluginPackage GetRequiredPackage(string pluginId)
    {
        _ = _store.GetOrDefault(pluginId);
        if (!_packages.TryGetValue(pluginId, out var package))
        {
            throw new ExternalPluginUnavailableException(
                pluginId,
                "plugin_not_found");
        }
        return package;
    }

    private async Task<ExternalPluginConfigurationView> GetViewCoreAsync(
        ExternalPluginPackage package,
        CancellationToken cancellationToken)
    {
        var entry = _store.GetOrDefault(package.Manifest.Id);
        var currentPackage = await ReloadRequiredAsync(package, cancellationToken)
            .ConfigureAwait(false);
        var schema = await _validator.LoadSchemaAsync(currentPackage, cancellationToken)
            .ConfigureAwait(false);
        var projected = ExternalPluginConfigurationSecrets.ProjectSafe(schema, entry.Vars);
        return new ExternalPluginConfigurationView(
            package,
            entry.Revision > 0,
            entry.Enabled,
            entry.Revision,
            entry.Revision > 0 ? entry.UpdatedAtUtc : null,
            entry.Args.Clone(),
            new ExternalPluginSafeVars(
                entry.Vars.Clone(),
                projected.ConfiguredWriteOnlyPaths),
            schema.Clone());
    }

    private async Task<ExternalPluginPackage> ReloadRequiredAsync(
        ExternalPluginPackage package,
        CancellationToken cancellationToken)
    {
        var currentPackage = await _loader.LoadPackageAsync(
            package.DirectoryPath,
            cancellationToken).ConfigureAwait(false);
        if (!EquivalentIdentity(package.Manifest, currentPackage.Manifest))
        {
            throw new ExternalPluginProtocolException(
                "plugin_manifest_changed",
                "The external plugin manifest changed after discovery.");
        }
        return currentPackage;
    }

    private static bool EquivalentIdentity(
        ExternalPluginManifest expected,
        ExternalPluginManifest actual) =>
        string.Equals(expected.Id, actual.Id, StringComparison.Ordinal)
        && string.Equals(expected.Name, actual.Name, StringComparison.Ordinal)
        && string.Equals(expected.Version, actual.Version, StringComparison.Ordinal)
        && expected.ApiVersion == actual.ApiVersion
        && string.Equals(expected.Type, actual.Type, StringComparison.Ordinal)
        && string.Equals(expected.Rid, actual.Rid, StringComparison.Ordinal)
        && string.Equals(expected.EntryPoint, actual.EntryPoint, StringComparison.Ordinal)
        && string.Equals(expected.ConfigSchema, actual.ConfigSchema, StringComparison.Ordinal)
        && expected.Capabilities.SequenceEqual(
            actual.Capabilities,
            StringComparer.Ordinal);
}
