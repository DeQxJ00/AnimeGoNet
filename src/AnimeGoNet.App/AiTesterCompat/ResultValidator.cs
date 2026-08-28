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

        var actualFileIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < result.Files.Count; i++)
        {
            var fileId = result.Files[i].FileId;
            if (string.IsNullOrWhiteSpace(fileId))
            {
                return $"files[{i}].file_id is required.";
            }

            if (!actualFileIds.Add(fileId))
            {
                return $"files[{i}].file_id '{fileId}' is duplicated.";
            }
        }

        MatchRequestInput? normalizedInput = null;
        if (input is not null)
        {
            normalizedInput = InputNormalizer.Normalize(input);
            if (result.Files.Count != normalizedInput.Files.Count)
            {
                return $"files length must equal input files length ({normalizedInput.Files.Count}).";
            }

            var expectedFileIds = Enumerable.Range(0, normalizedInput.Files.Count)
                .Select(AiMetadataFileIdentity.FromIndex)
                .ToHashSet(StringComparer.Ordinal);
            for (int i = 0; i < result.Files.Count; i++)
            {
                var fileId = result.Files[i].FileId!;
                if (!expectedFileIds.Contains(fileId))
                {
                    return $"files[{i}].file_id '{fileId}' is not present in the input.";
                }
            }

            if (!actualFileIds.SetEquals(expectedFileIds))
            {
                return "files[].file_id must contain every input file_id exactly once.";
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
