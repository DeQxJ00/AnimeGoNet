namespace AnimeGoNet.App.Configuration;

internal static class AnimeGoHostCommandLine
{
    private static readonly HashSet<string> BooleanSwitches = new(
        ["debug", "web", "backup"],
        StringComparer.Ordinal);

    private static readonly HashSet<string> LegacySwitches = new(
        ["config", "debug", "web", "backup"],
        StringComparer.Ordinal);

    public static string[] Normalize(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var normalized = new string[args.Count];

        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index]
                ?? throw new ArgumentException(
                    "Command-line arguments cannot contain null values.",
                    nameof(args));
            normalized[index] = NormalizeArgument(argument);
        }

        return normalized;
    }

    public static bool TryWriteHelp(
        IReadOnlyList<string> args,
        TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (!args.Any(static argument =>
                string.Equals(argument, "-h", StringComparison.Ordinal)
                || string.Equals(argument, "--help", StringComparison.Ordinal)
                || string.Equals(argument, "-help", StringComparison.Ordinal)))
        {
            return false;
        }

        writer.WriteLine("AnimeGoNet");
        writer.WriteLine("Usage: AnimeGoNet.App [options]");
        writer.WriteLine();
        writer.WriteLine("  -config, --config <path>        Deployment YAML path");
        writer.WriteLine("  -debug, --debug[=true|false]    Enable debug logging (default: false)");
        writer.WriteLine("  -web, --web[=true|false]        Enable the local Web API/UI (default: true)");
        writer.WriteLine("  -backup, --backup[=true|false]  Back up legacy YAML before upgrade (default: true)");
        writer.WriteLine("  -h, -help, --help               Show this help and exit");
        writer.WriteLine();
        writer.WriteLine(
            "Environment aliases: ANIMEGO_CONFIG, ANIMEGO_DEBUG, ANIMEGO_WEB, ANIMEGO_CONFIG_BACKUP");
        return true;
    }

    private static string NormalizeArgument(string argument)
    {
        if (argument.Length == 0
            || argument[0] != '-'
            || argument.StartsWith("--", StringComparison.Ordinal)
            || argument.Length == 1)
        {
            return NormalizeBareBoolean(argument);
        }

        var separator = argument.IndexOf('=');
        var name = separator < 0
            ? argument[1..]
            : argument[1..separator];
        if (!LegacySwitches.Contains(name))
        {
            return argument;
        }

        var modern = string.Concat("--", argument.AsSpan(1));
        return NormalizeBareBoolean(modern);
    }

    private static string NormalizeBareBoolean(string argument)
    {
        if (!argument.StartsWith("--", StringComparison.Ordinal)
            || argument.Contains('='))
        {
            return argument;
        }

        var name = argument[2..];
        return BooleanSwitches.Contains(name)
            ? string.Concat(argument, "=true")
            : argument;
    }
}
