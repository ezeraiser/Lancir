namespace Lancir.Core.Scanning;

/// <summary>
/// Heuristically finds plausible "main" executables within a game folder, for the user to
/// pick from (or discard) during scan review.
/// </summary>
public static class ExecutableFinder
{
    private static readonly string[] DeprioritizedNameFragments =
    [
        "unins", "setup", "install", "redist", "vcredist", "dxsetup",
        "crashreporter", "crashhandler", "helper", "updater", "launcher_be",
    ];

    /// <summary>
    /// Returns every .exe found under the folder that isn't obviously an installer/utility or
    /// explicitly ignored, ordered best-guess-first (shallower path, then larger file size).
    /// </summary>
    public static IReadOnlyList<string> FindCandidates(string folderPath, IReadOnlySet<string> ignoredPaths)
    {
        if (!Directory.Exists(folderPath))
        {
            return [];
        }

        return Directory.EnumerateFiles(folderPath, "*.exe", SearchOption.AllDirectories)
            .Where(path => !IsDeprioritized(Path.GetFileNameWithoutExtension(path)))
            .Where(path => !ignoredPaths.Contains(path))
            .OrderBy(path => GetDepth(folderPath, path))
            .ThenByDescending(TryGetFileLength)
            .ToList();
    }

    private static bool IsDeprioritized(string fileNameWithoutExtension)
    {
        var lowered = fileNameWithoutExtension.ToLowerInvariant();
        return DeprioritizedNameFragments.Any(lowered.Contains);
    }

    private static int GetDepth(string rootFolder, string filePath)
    {
        var relative = Path.GetRelativePath(rootFolder, filePath);
        return relative.Count(c => c == Path.DirectorySeparatorChar);
    }

    private static long TryGetFileLength(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (IOException)
        {
            return 0;
        }
    }
}
