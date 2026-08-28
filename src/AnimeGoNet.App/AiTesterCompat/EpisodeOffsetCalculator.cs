using AnimeGoNet.Core.Metadata;

namespace AnimeGoNet.App.AiTesterCompat;

public static class EpisodeOffsetCalculator
{
    public static LocalEpisodeOffsetResult Calculate(MatchRequestInput input, TmdbAiMatchResult result)
    {
        if (!input.IsMikanRssSource)
        {
            return NotApplicable("not a Mikan RSS input");
        }

        MatchRequestInput normalized = InputNormalizer.Normalize(input);
        if (result.TmdbId is not > 0 || result.Files is null || result.Files.Count != normalized.Files.Count)
        {
            return NotApplicable("validated TMDB series and aligned file results are required");
        }

        var mappings = new List<(int Candidate, int Episode, int Season)>();
        var filesById = result.Files
            .Where(file => file.FileId is not null)
            .ToDictionary(file => file.FileId!, StringComparer.Ordinal);
        for (int i = 0; i < normalized.Files.Count; i++)
        {
            if (!filesById.TryGetValue(AiMetadataFileIdentity.FromIndex(i), out var file))
            {
                continue;
            }

            if (file.Matched == true &&
                file.Episode is > 0 &&
                file.Season is > 0 &&
                normalized.Files[i].FileEpisodeCandidate is int candidate)
            {
                mappings.Add((candidate, file.Episode.Value, file.Season.Value));
            }
        }

        if (mappings.Count == 0)
        {
            return new(true, false, null, result.TmdbId, null, 0,
                "no matched file has a file_episode_candidate");
        }

        int[] offsets = mappings.Select(item => item.Episode - item.Candidate).Distinct().ToArray();
        if (offsets.Length != 1)
        {
            return new(true, false, null, result.TmdbId, null, mappings.Count,
                "matched files produce different local episode offsets; cache evidence is not created");
        }

        int[] seasons = mappings.Select(item => item.Season).Distinct().ToArray();
        if (seasons.Length != 1)
        {
            return new(true, false, null, result.TmdbId, null, mappings.Count,
                "matched files span multiple TMDB seasons; cache evidence is not created");
        }

        return new(true, true, offsets[0], result.TmdbId, seasons[0], mappings.Count,
            "calculated locally from the returned per-file mapping; production TMDB verification is still required");
    }

    private static LocalEpisodeOffsetResult NotApplicable(string reason) =>
        new(false, false, null, null, null, 0, reason);
}
