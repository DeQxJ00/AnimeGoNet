using System.Text;
using AnimeGoNet.Core.Configuration;
using AnimeGoNet.Core.Library;
using Microsoft.Data.Sqlite;

namespace AnimeGoNet.Data.Sqlite;

/// <summary>
/// Normalized path metadata. Existing path columns are immutable lookup/audit snapshots;
/// operational reads use app_path and never interpret a snapshot as the current location.
/// </summary>
internal static class PortablePathStorage
{
    internal static readonly (string Table, string Column, string Hint, string? Condition, string Scope)[] Fields =
    [
        ("completion_records", "media_path", "tv_library", null, "''"),
        ("movie_completion_records", "media_path", "movie_library", null, "''"),
        ("fallback_completion_records", "media_path", "tv_library", null, "''"),
        ("file_operations", "source_path", "", null,
            "COALESCE((SELECT task.downloader_id FROM task_files file JOIN ingest_tasks task ON task.id = file.task_id WHERE file.id = NEW.task_file_id), '')"),
        ("file_operations", "target_path", "library", null, "''"),
        ("download_jobs", "download_root_path", "download", null, "NEW.downloader_id"),
        ("download_jobs", "save_root_path", "library", null, "''"),
        ("other_file_readaptation_jobs", "source_media_path", "library", null, "''"),
        ("pending_tmdb_nfo_rewrite_jobs", "save_root_path", "tv_library", null, "''"),
        ("delete_execution_items", "target_key", "", "NEW.item_kind IN ('source_file', 'media_file')", "''"),
        ("delete_execution_items", "root_path", "", "NEW.item_kind IN ('source_file', 'media_file')", "''"),
    ];

    internal static string SchemaSql
    {
        get
        {
            var sql = new StringBuilder("""
                CREATE TABLE stored_path_locations (
                    original_path TEXT NOT NULL,
                    scope_id TEXT NOT NULL DEFAULT '',
                    root_kind TEXT,
                    root_id TEXT,
                    relative_path TEXT,
                    relative_key TEXT,
                    mapping_conflict INTEGER NOT NULL DEFAULT 0 CHECK (mapping_conflict IN (0, 1)),
                    PRIMARY KEY(original_path, scope_id),
                    CHECK ((root_kind IS NULL AND root_id IS NULL AND relative_path IS NULL)
                        OR (root_kind IS NOT NULL AND root_id IS NOT NULL AND relative_path IS NOT NULL))
                ) STRICT;
                CREATE INDEX ix_stored_path_locations_root
                    ON stored_path_locations(root_kind, root_id, relative_key);
                CREATE TABLE path_root_history (
                    root_kind TEXT NOT NULL,
                    root_id TEXT NOT NULL,
                    original_root TEXT NOT NULL,
                    PRIMARY KEY(root_kind, root_id, original_root)
                ) STRICT;
                """);
            foreach (var (table, column, hint, condition, scope) in Fields)
            {
                foreach (var action in new[] { "INSERT", "UPDATE OF " + column })
                {
                    var suffix = action == "INSERT" ? "insert" : "update";
                    sql.Append(System.Globalization.CultureInfo.InvariantCulture, $"""

                        CREATE TRIGGER capture_{table}_{column}_{suffix}
                        AFTER {action} ON {table}
                        WHEN NEW.{column} IS NOT NULL {(condition is null ? "" : "AND " + condition)}
                        BEGIN
                            SELECT RAISE(ABORT, 'path_identity_conflict')
                            WHERE EXISTS (
                                SELECT 1 FROM stored_path_locations
                                WHERE original_path = NEW.{column} AND scope_id = {scope}
                                  AND (mapping_conflict = 1 OR (root_kind IS NOT NULL
                                  AND app_path_kind(NEW.{column}, '{hint}', {scope}) IS NOT NULL
                                  AND (root_kind <> app_path_kind(NEW.{column}, '{hint}', {scope})
                                    OR root_id <> app_path_id(NEW.{column}, '{hint}', {scope})
                                    OR relative_path <> app_path_relative(NEW.{column}, '{hint}', {scope})))));
                            INSERT INTO stored_path_locations(original_path, scope_id, root_kind, root_id, relative_path, relative_key)
                            VALUES (NEW.{column}, {scope}, app_path_kind(NEW.{column}, '{hint}', {scope}),
                                app_path_id(NEW.{column}, '{hint}', {scope}), app_path_relative(NEW.{column}, '{hint}', {scope}),
                                app_path_key(NEW.{column}, '{hint}', {scope}))
                            ON CONFLICT(original_path, scope_id) DO UPDATE SET
                                root_kind = excluded.root_kind, root_id = excluded.root_id,
                                relative_path = excluded.relative_path, relative_key = excluded.relative_key
                            WHERE stored_path_locations.root_kind IS NULL
                              AND stored_path_locations.mapping_conflict = 0 AND excluded.root_kind IS NOT NULL;
                        END;
                        """);
                }
            }
            return sql.ToString();
        }
    }

    internal static IReadOnlyList<PortablePathRoot> Roots(AnimeGoOptions options) =>
    [
        new("tv_library", "default", options.Paths.SavePath),
        new("movie_library", "default", options.Paths.EffectiveMovieSavePath),
        .. options.Downloaders.Select(entry => new PortablePathRoot("download", entry.Key, entry.Value.DownloadPath)),
    ];

    internal static void Register(
        SqliteConnection connection,
        IReadOnlyList<PortablePathRoot>? currentRoots,
        IReadOnlyList<PortablePathRoot>? captureRoots = null)
    {
        PortableFileLocation? Capture(string? path, string hint, string scope)
        {
            if (path is null || currentRoots is null) return null;
            var candidates = (captureRoots ?? currentRoots)
                .Where(root => hint.Length == 0 || root.Kind == hint || (hint == "library" && root.Kind != "download"))
                .Where(root => scope.Length == 0 || root.Kind != "download" || root.Id == scope)
                .Select(root => PortableFilePaths.TryMakeRelative(root.Path, path, out var relative)
                    ? (Root: root, Location: new PortableFileLocation(root.Kind, root.Id, relative))
                    : (Root: root, Location: (PortableFileLocation?)null))
                .Where(value => value.Location is not null)
                .ToArray();
            if (candidates.Length == 0) return null;
            var longest = candidates.Max(value => value.Root.Path.TrimEnd('/', '\\').Length);
            var matches = candidates.Where(value => value.Root.Path.TrimEnd('/', '\\').Length == longest)
                .Select(value => value.Location!).Distinct().ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }

        connection.CreateFunction<string?, string, string, string?>("app_path_kind", (path, hint, scope) => Capture(path, hint, scope)?.RootKind);
        connection.CreateFunction<string?, string, string, string?>("app_path_id", (path, hint, scope) => Capture(path, hint, scope)?.RootId);
        connection.CreateFunction<string?, string, string, string?>("app_path_relative", (path, hint, scope) => Capture(path, hint, scope)?.RelativePath);
        connection.CreateFunction<string?, string, string, string?>("app_path_key", (path, hint, scope) => Capture(path, hint, scope)?.RelativePath.ToUpperInvariant());

        string? Resolve(string? snapshot, bool required, bool rootOnly = false, string? scope = null)
        {
            if (snapshot is null || currentRoots is null) return snapshot;
            // SQLite permits a nested read on the same connection. It observes the caller's
            // transaction, including trigger inserts, without a stale process-wide path cache.
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT root_kind, root_id, relative_path, scope_id, mapping_conflict
                FROM stored_path_locations WHERE original_path = $path
                  AND ($scope IS NULL OR scope_id = $scope OR scope_id = '');
                """;
            command.Parameters.AddWithValue("$path", snapshot);
            command.Parameters.AddWithValue("$scope", (object?)scope ?? DBNull.Value);
            var locations = new List<(string? Kind, string? Id, string? Relative, string Scope, bool Conflict)>();
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                    locations.Add((reader.IsDBNull(0) ? null : reader.GetString(0),
                        reader.IsDBNull(1) ? null : reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
                        reader.GetString(3), reader.GetInt64(4) != 0));
            }
            if (scope is not null && locations.Any(value => value.Scope == scope))
                locations.RemoveAll(value => value.Scope != scope);

            try
            {
                var resolved = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
                foreach (var location in locations)
                {
                    if (location.Conflict) throw new PathMappingException("path_identity_conflict");
                    if (location.Kind is null) continue;
                    var root = currentRoots.SingleOrDefault(value => value.Kind == location.Kind && value.Id == location.Id)
                        ?? throw new PathMappingException("path_mapping_required");
                    var relative = location.Relative!;
                    if (OperatingSystem.IsWindows() && !rootOnly)
                    {
                        using var collision = connection.CreateCommand();
                        collision.CommandText = """
                            SELECT relative_path FROM stored_path_locations
                            WHERE root_kind = $kind AND root_id = $id
                              AND relative_key = $key AND relative_path <> $relative;
                            """;
                        collision.Parameters.AddWithValue("$kind", root.Kind);
                        collision.Parameters.AddWithValue("$id", root.Id);
                        collision.Parameters.AddWithValue("$relative", relative);
                        collision.Parameters.AddWithValue("$key", relative.ToUpperInvariant());
                        using var variants = collision.ExecuteReader();
                        while (variants.Read())
                            if (variants.GetString(0).Equals(relative, StringComparison.OrdinalIgnoreCase))
                                throw new PathMappingException("path_case_collision");
                    }
                    resolved.Add(PortableFilePaths.Resolve(root.Path, rootOnly ? "" : relative, OperatingSystem.IsWindows()));
                }
                if (resolved.Count == 1) return resolved.Single();
                throw new PathMappingException("path_mapping_required");
            }
            catch (PathMappingException) when (!required)
            {
                return null;
            }
        }

        connection.CreateFunction<string?, string?>("app_path", path => Resolve(path, required: true));
        connection.CreateFunction<string?, string?>("app_path_optional", path => Resolve(path, required: false));
        connection.CreateFunction<string?, string, string?>("app_path", (path, scope) => Resolve(path, required: true, scope: scope));
        connection.CreateFunction<string?, string, string?>("app_path_optional", (path, scope) => Resolve(path, required: false, scope: scope));
        connection.CreateFunction<string?, string?>("app_path_root", path =>
            currentRoots is null ? null : Resolve(path, required: true, rootOnly: true));
        connection.CreateFunction<string?, string, string?>("app_path_root", (path, scope) =>
            currentRoots is null ? null : Resolve(path, required: true, rootOnly: true, scope: scope));
        connection.CreateFunction<string?, string?, bool>("app_same_path", (left, right) =>
            left is not null && right is not null && PortableFilePaths.TryMakeRelative(left, right, out var relative)
                && relative.Length == 0);
    }

    internal static async Task UpgradeRecordsAsync(
        SqliteConnection connection,
        IReadOnlyList<PortablePathRoot> roots,
        CancellationToken cancellationToken)
    {
        await using var transaction = connection.BeginTransaction();
        await using (var history = connection.CreateCommand())
        {
            history.Transaction = transaction;
            history.CommandText = """
                CREATE TEMP TABLE mapped_paths_before_upgrade(original_path TEXT NOT NULL, scope_id TEXT NOT NULL,
                    PRIMARY KEY(original_path, scope_id));
                INSERT INTO mapped_paths_before_upgrade
                SELECT original_path, scope_id FROM stored_path_locations WHERE root_kind IS NOT NULL;
                INSERT OR IGNORE INTO path_root_history
                SELECT 'download', downloader_id, download_root_path FROM download_jobs
                WHERE download_root_path IS NOT NULL AND download_root_path <> '';
                INSERT OR IGNORE INTO path_root_history
                SELECT COALESCE(location.root_kind,
                        CASE WHEN task.media_type = 'movie' THEN 'movie_library' ELSE 'tv_library' END),
                       'default', job.save_root_path
                FROM download_jobs job JOIN ingest_tasks task ON task.id = job.task_id
                LEFT JOIN stored_path_locations location ON location.original_path = job.save_root_path AND location.scope_id = ''
                WHERE job.save_root_path IS NOT NULL AND job.save_root_path <> '';
                """;
            await history.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var root in roots)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT OR IGNORE INTO path_root_history VALUES ($kind, $id, $path);";
            insert.Parameters.AddWithValue("$kind", root.Kind);
            insert.Parameters.AddWithValue("$id", root.Id);
            insert.Parameters.AddWithValue("$path", root.Path);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var captureRoots = new List<PortablePathRoot>();
        await using (var query = connection.CreateCommand())
        {
            query.Transaction = transaction;
            query.CommandText = "SELECT root_kind, root_id, original_root FROM path_root_history;";
            await using var reader = await query.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                captureRoots.Add(new PortablePathRoot(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }

        Register(connection, roots, captureRoots);
        try
        {
            foreach (var (table, column, hint, condition, scope) in Fields)
            {
                var legacyScope = scope.Replace("NEW.", "legacy.", StringComparison.Ordinal);
                var legacyCondition = condition is null ? "" : "AND " + condition.Replace("NEW.", "legacy.", StringComparison.Ordinal);
                await using var migrate = connection.CreateCommand();
                migrate.Transaction = transaction;
                migrate.CommandText = $"""
                    UPDATE stored_path_locations
                    SET root_kind = NULL, root_id = NULL, relative_path = NULL, relative_key = NULL, mapping_conflict = 1
                    WHERE root_kind IS NOT NULL AND EXISTS (
                        SELECT 1 FROM {table} legacy WHERE legacy.{column} = original_path AND {legacyScope} = scope_id {legacyCondition})
                      AND NOT EXISTS (SELECT 1 FROM mapped_paths_before_upgrade before
                        WHERE before.original_path = stored_path_locations.original_path AND before.scope_id = stored_path_locations.scope_id)
                      AND app_path_kind(original_path, '{hint}', scope_id) IS NOT NULL
                      AND (root_kind <> app_path_kind(original_path, '{hint}', scope_id)
                        OR root_id <> app_path_id(original_path, '{hint}', scope_id)
                        OR relative_path <> app_path_relative(original_path, '{hint}', scope_id));
                    INSERT INTO stored_path_locations(original_path, scope_id, root_kind, root_id, relative_path, relative_key)
                    SELECT DISTINCT legacy.{column}, {legacyScope}, app_path_kind(legacy.{column}, '{hint}', {legacyScope}),
                        app_path_id(legacy.{column}, '{hint}', {legacyScope}), app_path_relative(legacy.{column}, '{hint}', {legacyScope}),
                        app_path_key(legacy.{column}, '{hint}', {legacyScope})
                    FROM {table} legacy
                    WHERE legacy.{column} IS NOT NULL {legacyCondition}
                    ON CONFLICT(original_path, scope_id) DO UPDATE SET
                        root_kind = excluded.root_kind, root_id = excluded.root_id,
                        relative_path = excluded.relative_path, relative_key = excluded.relative_key
                    WHERE stored_path_locations.root_kind IS NULL
                      AND stored_path_locations.mapping_conflict = 0 AND excluded.root_kind IS NOT NULL;
                    """;
                await migrate.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            await using var cleanup = connection.CreateCommand();
            cleanup.Transaction = transaction;
            cleanup.CommandText = "DROP TABLE mapped_paths_before_upgrade;";
            await cleanup.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Register(connection, roots);
        }
    }
}
