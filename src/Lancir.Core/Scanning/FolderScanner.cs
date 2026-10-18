using Lancir.Core.Models;

namespace Lancir.Core.Scanning;

/// <summary>
/// Scans watched root folders for game candidates. Each watched root is considered a candidate
/// itself (in case the user pointed directly at a single game's folder), and each of its
/// immediate subfolders is considered a candidate too (in case the user pointed at a folder
/// containing many games). Candidates with no executable at all are dropped — there's nothing
/// useful to show the user for those.
/// </summary>
public static class FolderScanner
{
    public static IReadOnlyList<GameEntry> Scan(
        IEnumerable<string> watchedRootFolders, IReadOnlySet<string>? ignoredExecutablePaths = null,
        IReadOnlySet<string>? ignoredFolderPaths = null)
    {
        ignoredExecutablePaths ??= new HashSet<string>();
        ignoredFolderPaths ??= new HashSet<string>();
        var entries = new List<GameEntry>();
        var visitedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in watchedRootFolders)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            TryAddCandidate(root, ignoredExecutablePaths, ignoredFolderPaths, visitedFolders, entries);

            foreach (var subfolder in Directory.EnumerateDirectories(root))
            {
                TryAddCandidate(subfolder, ignoredExecutablePaths, ignoredFolderPaths, visitedFolders, entries);
            }
        }

        return entries;
    }

    private static void TryAddCandidate(
        string folderPath,
        IReadOnlySet<string> ignoredExecutablePaths,
        IReadOnlySet<string> ignoredFolderPaths,
        HashSet<string> visitedFolders,
        List<GameEntry> entries)
    {
        if (ignoredFolderPaths.Contains(folderPath) || !visitedFolders.Add(folderPath))
        {
            return;
        }

        var candidates = ExecutableFinder.FindCandidates(folderPath, ignoredExecutablePaths);
        if (candidates.Count == 0)
        {
            return;
        }

        var rawName = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        entries.Add(new GameEntry
        {
            FolderPath = folderPath,
            RawFolderName = rawName,
            CleanedName = GameNameCleaner.Clean(rawName),
            ExecutableCandidates = candidates,
        });
    }
}
