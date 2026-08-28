using System.Text.Json;
using AnimeGoNet.Core.Metadata;

namespace AnimeGoNet.App.AiTesterCompat;

public static class ResultValidator
{
    private static readonly HashSet<string> ForbiddenTopLevelFields = new(StringComparer.Ordinal)
    {
        "title",
        "confidence",
        "air_date",
        "episode_title",
        "failure_stage",
        "failure_code",
        "matched_title",
        "season_number",
        "tmdb_episode_number",
        "episode_offset"
    };

    public static (bool Valid, string? Error, TmdbAiMatchResult? Result) Validate(string? json, MatchRequestInput? input = null)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return (false, "Model output was empty or unavailable.", null);
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (ForbiddenTopLevelFields.Contains(property.Name))
                {
                    return (false, $"Unexpected legacy response field '{property.Name}'.", null);
                }
            }

            TmdbAiMatchResult? result = JsonSerializer.Deserialize(json, AiTesterJsonContext.Default.TmdbAiMatchResult);
            if (result is null)
            {
                return (false, "Model output JSON did not deserialize to an object.", null);
            }

            string? error = ValidateResult(result, input);
            return (error is null, error, result);
        }
        catch (JsonException ex)
        {
            return (false, "Model output is not valid JSON: " + ex.Message, null);
        }
    }

    private static string? ValidateResult(TmdbAiMatchResult result, MatchRequestInput? input)
    {
        if (result.Matched is null) return "Required field matched is missing.";
        if (result.Files is null) return "Required field files is missing.";
        if (result.Reason is null && result.Matched == false) return "matched=false requires reason.";

        if (result.Matched == true)
        {
            if (result.TmdbId is null) return "matched=true requires tmdb_id.";
            if (result.Reason is not null) return "matched=true requires reason to be null.";
        }

        MatchRequestInput? normalizedInput = null;
        if (input is not null)
        {
            normalizedInput = InputNormalizer.Normalize(input);
            if (result.Files.Count != normalizedInput.Files.Count)
            {
                return $"files length must equal input files length ({normalizedInput.Files.Count}).";
            }

            if (normalizedInput.Files.Count > 1)
            {
                for (int i = 0; i < normalizedInput.Files.Count; i++)
                {
                    if (!string.Equals(result.Files[i].Name, normalizedInput.Files[i].Name, StringComparison.Ordinal))
                    {
                        return $"files[{i}].name must echo input name '{normalizedInput.Files[i].Name}' for multi-file mapping.";
                    }
                }
            }
        }

        for (int i = 0; i < result.Files.Count; i++)
        {
            TmdbAiFileResult file = result.Files[i];
            if (file.Matched is null) return $"files[{i}].matched is required.";
            if (file.Season == 0) return $"files[{i}].season must not be 0.";
            if (file.Matched == true)
            {
                if (file.Season is null or <= 0) return $"files[{i}].season must be greater than 0 when matched=true.";
                if (file.Episode is null || (file.Episode <= 0 && !file.IsExtras))
                {
                    return $"files[{i}].episode must be greater than 0 or Extras when matched=true.";
                }
                if (file.Reason is not null) return $"files[{i}].reason must be null when matched=true.";
            }
            else if ((file.Episode is not null && !file.IsExtras)
                || string.IsNullOrWhiteSpace(file.Reason))
            {
                return $"files[{i}] requires episode=null/Extras and a reason when matched=false.";
            }
        }

        return null;
    }
}
