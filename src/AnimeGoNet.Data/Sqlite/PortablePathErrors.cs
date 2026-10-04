using AnimeGoNet.Core.Library;
using Microsoft.Data.Sqlite;

namespace AnimeGoNet.Data.Sqlite;

public static class PortablePathErrors
{
    private static readonly string[] Codes =
    [
        "path_mapping_required", "path_root_platform_mismatch", "path_relative_invalid",
        "path_filename_platform_incompatible", "path_case_collision", "path_identity_conflict",
    ];

    public static string? GetCode(Exception exception)
    {
        if (exception is PathMappingException mapping) return mapping.Code;
        // SQLite surfaces UDF/trigger failures as SqliteException rather than retaining the
        // managed exception. Recognize only our exact, fixed error tokens, never file names.
        return exception is SqliteException sqlite && sqlite.SqliteErrorCode is 1 or 19
            ? Codes.FirstOrDefault(code => sqlite.Message.Contains("'" + code + "'", StringComparison.Ordinal))
            : null;
    }
}
