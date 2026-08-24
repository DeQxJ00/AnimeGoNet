namespace AnimeGoNet.Data.Sqlite;

internal sealed record SchemaMigration(
    int Version,
    string Name,
    string Sql,
    bool RequiresForeignKeysDisabled = false);
