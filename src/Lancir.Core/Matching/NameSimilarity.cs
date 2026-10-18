using System.Text.RegularExpressions;

namespace Lancir.Core.Matching;

/// <summary>
/// Small, dependency-free string similarity scorer used to rank Steam app-name candidates
/// against a cleaned folder name.
/// </summary>
public static partial class NameSimilarity
{
    [GeneratedRegex(@"[^a-z0-9 ]")]
    private static partial Regex NonAlphaNumericRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    public static string Normalize(string name)
    {
        var lowered = name.ToLowerInvariant();
        lowered = NonAlphaNumericRegex().Replace(lowered, " ");
        lowered = WhitespaceRegex().Replace(lowered, " ").Trim();
        return lowered;
    }

    /// <summary>
    /// Combines token overlap with normalized Levenshtein distance into a single [0, 1] score,
    /// where 1 means an exact (normalized) match.
    /// </summary>
    public static double Score(string candidateA, string candidateB)
    {
        var normalizedA = Normalize(candidateA);
        var normalizedB = Normalize(candidateB);

        if (normalizedA.Length == 0 || normalizedB.Length == 0)
        {
            return 0;
        }

        if (normalizedA == normalizedB)
        {
            return 1;
        }

        var tokenScore = TokenOverlapScore(normalizedA, normalizedB);
        var editScore = LevenshteinSimilarity(normalizedA, normalizedB);

        return (tokenScore * 0.6) + (editScore * 0.4);
    }

    private static double TokenOverlapScore(string normalizedA, string normalizedB)
    {
        var tokensA = normalizedA.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var tokensB = normalizedB.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();

        if (tokensA.Count == 0 || tokensB.Count == 0)
        {
            return 0;
        }

        var intersection = tokensA.Intersect(tokensB).Count();
        var union = tokensA.Union(tokensB).Count();

        return union == 0 ? 0 : (double)intersection / union;
    }

    private static double LevenshteinSimilarity(string a, string b)
    {
        var distance = LevenshteinDistance(a, b);
        var maxLength = Math.Max(a.Length, b.Length);
        return maxLength == 0 ? 1 : 1.0 - ((double)distance / maxLength);
    }

    private static int LevenshteinDistance(string a, string b)
    {
        var lengthA = a.Length;
        var lengthB = b.Length;
        var distances = new int[lengthA + 1, lengthB + 1];

        for (var i = 0; i <= lengthA; i++)
        {
            distances[i, 0] = i;
        }

        for (var j = 0; j <= lengthB; j++)
        {
            distances[0, j] = j;
        }

        for (var i = 1; i <= lengthA; i++)
        {
            for (var j = 1; j <= lengthB; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                distances[i, j] = Math.Min(
                    Math.Min(distances[i - 1, j] + 1, distances[i, j - 1] + 1),
                    distances[i - 1, j - 1] + cost);
            }
        }

        return distances[lengthA, lengthB];
    }
}
