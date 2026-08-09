using System.Globalization;

namespace AnimeGoNet.DataBuilder;

internal static class DataBuilderCli
{
    private static readonly HashSet<string> AllowedOptions =
    [
        "input",
        "output",
        "data-version",
        "asset-base-url",
        "upstream-repository",
        "upstream-release",
        "upstream-asset",
        "upstream-sha256",
        "generated-at-utc",
        "minimum-client-version",
        "subjects-per-shard",
        "minimum-subject-count",
        "minimum-episode-count",
        "minimum-relation-count",
    ];

    private const string Usage = """
        AnimeGoNet.DataBuilder
          --input <bangumi-archive.zip>
          --output <new-output-directory>
          --data-version <stable-version>
          --asset-base-url <https://release.example/assets/>
          --upstream-release <release-name>
          --upstream-asset <archive-file-name>
          --upstream-sha256 <lowercase-sha256>
          --generated-at-utc <ISO-8601 UTC>
          [--minimum-client-version <version>]
          [--subjects-per-shard <count>]
          [--minimum-subject-count <count>]
          [--minimum-episode-count <count>]
          [--minimum-relation-count <count>]
          [--upstream-repository <url>]
        """;

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 1 && args[0] is "--help" or "-h")
        {
            Console.WriteLine(Usage);
            return 0;
        }

        try
        {
            var values = Parse(args);
            var result = await BangumiArchivePackageBuilder.BuildAsync(
                new BangumiArchiveBuildOptions(
                    Required(values, "input"),
                    Required(values, "output"),
                    Required(values, "data-version"),
                    new Uri(Required(values, "asset-base-url"), UriKind.Absolute),
                    values.GetValueOrDefault(
                        "upstream-repository",
                        "https://github.com/bangumi/Archive"),
                    Required(values, "upstream-release"),
                    Required(values, "upstream-asset"),
                    Required(values, "upstream-sha256"),
                    DateTimeOffset.ParseExact(
                        Required(values, "generated-at-utc"),
                        "O",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind),
                    values.GetValueOrDefault("minimum-client-version", "0.1.0"),
                    ParsePositiveInt(
                        values.GetValueOrDefault("subjects-per-shard", "25000"),
                        "subjects-per-shard"),
                    ParsePositiveInt(
                        values.GetValueOrDefault("minimum-subject-count", "1"),
                        "minimum-subject-count"),
                    ParsePositiveInt(
                        values.GetValueOrDefault("minimum-episode-count", "1"),
                        "minimum-episode-count"),
                    ParsePositiveInt(
                        values.GetValueOrDefault("minimum-relation-count", "1"),
                        "minimum-relation-count")));
            Console.WriteLine(
                $"Built {result.Manifest.DataVersion}: "
                + $"{result.Manifest.SubjectCount} subjects, "
                + $"{result.Manifest.EpisodeCount} episodes, "
                + $"{result.Manifest.RelationCount} relations, "
                + $"{result.Manifest.Assets.Count} assets.");
            return 0;
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or FormatException
                or IOException
                or InvalidDataException)
        {
            Console.Error.WriteLine($"Data builder failed: {exception.Message}");
            return 2;
        }
    }

    private static Dictionary<string, string> Parse(string[] args)
    {
        if (args.Length == 0 || args.Length % 2 != 0)
        {
            throw new ArgumentException(Usage);
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index += 2)
        {
            var rawName = args[index];
            if (!rawName.StartsWith("--", StringComparison.Ordinal)
                || rawName.Length == 2
                || !AllowedOptions.Contains(rawName[2..])
                || !values.TryAdd(rawName[2..], args[index + 1]))
            {
                throw new ArgumentException("Command-line options must be unique --name value pairs.");
            }
        }
        return values;
    }

    private static string Required(Dictionary<string, string> values, string name) =>
        values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"--{name} is required.");

    private static int ParsePositiveInt(string value, string optionName) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result)
        && result > 0
            ? result
            : throw new ArgumentException($"--{optionName} must be a positive integer.");
}
