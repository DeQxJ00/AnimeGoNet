using System.Text.Json;
using System.Text.Json.Serialization;
using AnimeGoNet.Data.Cache;
using AnimeGoNet.Data.Sqlite;
using Microsoft.Data.Sqlite;

return await LegacyCacheImporterProgram.RunAsync(args);

internal static class LegacyCacheImporterProgram
{
    private const string Usage =
        "Usage: AnimeGoNet.LegacyCacheImporter --data-path <AnimeGoNet data directory> --input <export.json>";

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 1 && args[0] is "--help" or "-h")
        {
            Console.Out.WriteLine(Usage);
            return 0;
        }
        try
        {
            var options = Parse(args);
            if (!Directory.Exists(options.DataPath))
            {
                return Fail("data_path_not_found", "The supplied data path does not exist.");
            }
            if (!File.Exists(options.InputPath))
            {
                return Fail("input_not_found", "The supplied export package does not exist.");
            }

            var databasePath = Path.Combine(options.DataPath, "animegonet.db");
            if (!File.Exists(databasePath))
            {
                return Fail(
                    "database_not_found",
                    "animegonet.db does not exist under the supplied data path; start AnimeGoNet once first.");
            }

            var database = new AnimeGoSqliteDatabase(databasePath);
            await database.InitializeAsync().ConfigureAwait(false);
            var importer = new LegacyCacheImporter(database);
            await using var input = new FileStream(
                options.InputPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var report = await importer
                .ImportAsync(input, DateTimeOffset.UtcNow)
                .ConfigureAwait(false);
            Console.Out.WriteLine(JsonSerializer.Serialize(
                report,
                LegacyCacheImporterJsonContext.Default.LegacyCacheImportReport));
            return 0;
        }
        catch (LegacyCacheImportException exception)
        {
            return Fail(exception.Code, exception.Message);
        }
        catch (ArgumentException exception)
        {
            return Fail("invalid_arguments", exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return Fail("migration_failed", exception.Message);
        }
        catch (SqliteException)
        {
            return Fail("migration_database_failed", "The migration transaction could not be completed.");
        }
        catch (IOException)
        {
            return Fail("migration_io_failed", "The migration package or database could not be read or written.");
        }
        catch (UnauthorizedAccessException)
        {
            return Fail("migration_access_denied", "Access to the migration package or database was denied.");
        }
    }

    private static ImporterOptions Parse(string[] args)
    {
        string? dataPath = null;
        string? inputPath = null;
        for (var index = 0; index < args.Length; index++)
        {
            var name = args[index];
            if (name is "--help" or "-h")
            {
                throw new ArgumentException(Usage);
            }
            if (index + 1 >= args.Length)
            {
                throw new ArgumentException($"Missing value for {name}.");
            }
            var value = args[++index];
            switch (name)
            {
                case "--data-path" when dataPath is null:
                    dataPath = Path.GetFullPath(value);
                    break;
                case "--input" when inputPath is null:
                    inputPath = Path.GetFullPath(value);
                    break;
                default:
                    throw new ArgumentException($"Unknown or repeated argument: {name}.");
            }
        }

        if (dataPath is null || inputPath is null)
        {
            throw new ArgumentException("Both --data-path and --input are required.");
        }
        return new ImporterOptions(dataPath, inputPath);
    }

    private static int Fail(string code, string message)
    {
        Console.Error.WriteLine($"{code}: {message}");
        return 2;
    }

    private sealed record ImporterOptions(string DataPath, string InputPath);
}

[JsonSerializable(typeof(LegacyCacheImportReport))]
internal sealed partial class LegacyCacheImporterJsonContext : JsonSerializerContext;
