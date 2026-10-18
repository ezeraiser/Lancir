using System.Text.RegularExpressions;

namespace Lancir.Core.Scanning;

/// <summary>
/// Turns a raw folder name (often full of release-group/scene tags) into a plausible game title
/// suitable for searching against the Steam app list.
/// </summary>
public static partial class GameNameCleaner
{
    // Matches a 4-digit year, e.g. 2019, optionally wrapped in brackets/parens.
    [GeneratedRegex(@"[\[\(]?(19|20)\d{2}[\]\)]?")]
    private static partial Regex YearRegex();

    // Common scene/repack/language tags to strip, matched as whole words (case-insensitive).
    [GeneratedRegex(
        @"\b(repack|reloaded|codex|plaza|skidrow|flt|fitgirl|goldberg|gog|steamrip|" +
        @"multi\d*|multilanguage|dlc|complete|edition|remastered|deluxe|goty|" +
        @"proper|crack(ed)?|full|unlocked|razor1911|hoodlum|prophet|cpy|" +
        @"build\.\d+)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex TagRegex();

    // Version strings like "v1.5.78.11833" or "1.5.78", using the original separators (dots).
    [GeneratedRegex(@"\bv\d+(\.\d+){1,}\b", RegexOptions.IgnoreCase)]
    private static partial Regex VersionRegex();

    // Anything in [] or () brackets entirely (e.g. release notes).
    [GeneratedRegex(@"[\[\(][^\]\)]*[\]\)]")]
    private static partial Regex BracketedRegex();

    [GeneratedRegex(@"[._\-]+")]
    private static partial Regex SeparatorRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    public static string Clean(string rawFolderName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawFolderName);

        var name = rawFolderName;

        name = BracketedRegex().Replace(name, " ");
        name = VersionRegex().Replace(name, " ");
        name = SeparatorRegex().Replace(name, " ");
        name = YearRegex().Replace(name, " ");
        name = TagRegex().Replace(name, " ");
        name = WhitespaceRegex().Replace(name, " ").Trim();

        return name;
    }
}
