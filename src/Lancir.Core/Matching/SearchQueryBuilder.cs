using System.Text;
using System.Text.RegularExpressions;

namespace Lancir.Core.Matching;

/// <summary>
/// Turns a display name into a search-friendly query string. Folder-derived names are often
/// smashed together with no separators (e.g. "HeroesOldenEra"), which full-text search engines
/// like IGDB's can fail to match at all against a properly spaced title ("Heroes Olden Era") —
/// so this splits camelCase/PascalCase runs back into separate words before searching.
/// </summary>
public static partial class SearchQueryBuilder
{
    // Splits "aB" (lower->upper) and "AAb" (upper run -> upper+lower) boundaries.
    [GeneratedRegex(@"(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])")]
    private static partial Regex WordBoundaryRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    public static string BuildSearchTerm(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        var spaced = WordBoundaryRegex().Replace(displayName, " ");
        return WhitespaceRegex().Replace(spaced, " ").Trim();
    }
}
