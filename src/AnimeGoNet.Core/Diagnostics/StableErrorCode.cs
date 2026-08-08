namespace AnimeGoNet.Core.Diagnostics;

[Flags]
public enum StableErrorSemantic
{
    None = 0,
    AlreadyExists = 1 << 0,
    NotFound = 1 << 1,
    ParseFailed = 1 << 2,
}

public interface IStableError
{
    string Code { get; }

    StableErrorSemantic Semantics { get; }
}

public static class StableErrorCode
{
    public const int MaximumLength = 128;

    public static bool IsValid(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaximumLength)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!(char.IsAsciiLetterOrDigit(character) || character is '_' or '-'))
            {
                return false;
            }
        }

        return true;
    }

    public static string Require(string? value, string parameterName)
    {
        if (!IsValid(value))
        {
            throw new ArgumentException(
                $"Error code must be a 1-{MaximumLength} character ASCII identifier.",
                parameterName);
        }

        return value!;
    }

    public static bool HasSemantic(
        Exception? exception,
        StableErrorSemantic semantic)
    {
        if (semantic == StableErrorSemantic.None)
        {
            return false;
        }

        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is IStableError stable
                && IsValid(stable.Code)
                && (stable.Semantics & semantic) == semantic)
            {
                return true;
            }
        }

        return false;
    }

    public static bool TryGet(
        Exception? exception,
        out string? code,
        out StableErrorSemantic semantics)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is not IStableError stable)
            {
                continue;
            }

            if (!IsValid(stable.Code))
            {
                code = null;
                semantics = StableErrorSemantic.None;
                return false;
            }

            code = stable.Code;
            semantics = stable.Semantics;
            return true;
        }

        code = null;
        semantics = StableErrorSemantic.None;
        return false;
    }
}
