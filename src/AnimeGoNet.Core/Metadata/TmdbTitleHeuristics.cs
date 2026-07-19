using System.Text;
using System.Text.RegularExpressions;

namespace AnimeGoNet.Core.Metadata;

public static partial class TmdbTitleHeuristics
{
    public const double MinimumSimilarity = 0.75;

    public const int SuffixStepCount = 4;

    public static string ApplySuffixStep(string title, int step)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentOutOfRangeException.ThrowIfLessThan(step, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(step, SuffixStepCount);
        var regex = step switch
        {
            0 => ChineseSeasonSuffix(),
            1 => EnglishSeasonSuffix(),
            2 => ChapterAndFollowingText(),
            _ => RomanNumberOrFollowingToken(),
        };
        if (!regex.IsMatch(title))
        {
            return title;
        }

        var replacement = regex.Replace(title, string.Empty);
        var originalLength = Encoding.UTF8.GetByteCount(title);
        var replacementLength = Encoding.UTF8.GetByteCount(replacement);
        return replacementLength > 0 && replacementLength > originalLength / 10
            ? replacement
            : title;
    }

    public static double SimilarText(string first, string second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        var left = Encoding.UTF8.GetBytes(first);
        var right = Encoding.UTF8.GetBytes(second);
        if (left.Length + right.Length == 0)
        {
            return 0;
        }

        var similar = SimilarSegment(left, 0, left.Length, right, 0, right.Length);
        return similar * 200d / (left.Length + right.Length);
    }

    private static int SimilarSegment(
        byte[] left,
        int leftStart,
        int leftLength,
        byte[] right,
        int rightStart,
        int rightLength)
    {
        var maximum = 0;
        var leftPosition = 0;
        var rightPosition = 0;
        for (var leftIndex = 0; leftIndex < leftLength; leftIndex++)
        {
            for (var rightIndex = 0; rightIndex < rightLength; rightIndex++)
            {
                for (var length = 0;
                     leftIndex + length < leftLength
                     && rightIndex + length < rightLength
                     && left[leftStart + leftIndex + length] == right[rightStart + rightIndex + length];
                     length++)
                {
                    if (length + 1 > maximum)
                    {
                        maximum = length + 1;
                        leftPosition = leftIndex;
                        rightPosition = rightIndex;
                    }
                }
            }
        }

        var sum = maximum;
        if (sum == 0)
        {
            return 0;
        }

        if (leftPosition > 0 && rightPosition > 0)
        {
            sum += SimilarSegment(left, leftStart, leftPosition, right, rightStart, rightPosition);
        }

        if (leftPosition + maximum < leftLength && rightPosition + maximum < rightLength)
        {
            sum += SimilarSegment(
                left,
                leftStart + leftPosition + maximum,
                leftLength - leftPosition - maximum,
                right,
                rightStart + rightPosition + maximum,
                rightLength - rightPosition - maximum);
        }

        return sum;
    }

    [GeneratedRegex(@"[ \t\n\f\r]?第?([0-9]{1,2}|(一|二|三|四|五|伍|六|七|八|九|十))(期|部|季|篇|章|編)$", RegexOptions.CultureInvariant)]
    private static partial Regex ChineseSeasonSuffix();

    [GeneratedRegex(@"[ \t\n\f\r]?([0-9]{1,2}(st|nd|rd|th)[ \t\n\f\r]?Season|Season[ \t\n\f\r]?[0-9]{1,2}|[0-9]{1,2})$", RegexOptions.CultureInvariant)]
    private static partial Regex EnglishSeasonSuffix();

    [GeneratedRegex(@"[ \t\n\f\r](.*?)(期|部|季|篇|章|編).*$", RegexOptions.CultureInvariant)]
    private static partial Regex ChapterAndFollowingText();

    [GeneratedRegex(@"[ \t\n\f\r]?((V|X|IX|IV|V?I{1,3})|[2-9]|[1-9][0-9]).*$|[ \t\n\f\r][^ \t\n\f\r]+$", RegexOptions.CultureInvariant)]
    private static partial Regex RomanNumberOrFollowingToken();
}
