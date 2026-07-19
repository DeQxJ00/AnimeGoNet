using AnimeGoNet.Data.Sqlite;

namespace AnimeGoNet.Data.Tests;

public sealed class SqliteDatabaseFixture : IAsyncDisposable
{
    private SqliteDatabaseFixture(string rootPath, AnimeGoSqliteDatabase database)
    {
        RootPath = rootPath;
        Database = database;
    }

    public string RootPath { get; }

    public AnimeGoSqliteDatabase Database { get; }

    public static async Task<SqliteDatabaseFixture> CreateAsync()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "animegonet-data-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootPath);
        var database = new AnimeGoSqliteDatabase(Path.Combine(rootPath, "animegonet.db"));
        await database.InitializeAsync();
        return new SqliteDatabaseFixture(rootPath, database);
    }

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(RootPath))
        {
            Directory.Delete(RootPath, recursive: true);
        }

        return ValueTask.CompletedTask;
    }
}
