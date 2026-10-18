using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Lancir.Core.Services;

/// <summary>
/// Finds where Steam installed a game, from the app manifests (steamapps/appmanifest_&lt;id&gt;.acf) of every Steam
/// library on this PC. Used to suggest the game's executable so its playtime can be tracked.
/// </summary>
public static partial class SteamLocator
{
    /// <summary>The install folder of a Steam app, or null when Steam or the app is not found.</summary>
    public static string? FindInstallFolder(int appId)
    {
        foreach (var library in GetLibraryFolders())
        {
            var manifest = Path.Combine(library, "steamapps", $"appmanifest_{appId}.acf");
            if (!File.Exists(manifest))
            {
                continue;
            }

            string text;
            try
            {
                text = File.ReadAllText(manifest);
            }
            catch (IOException)
            {
                continue;
            }

            var installDir = ParseInstallDir(text);
            if (installDir is null)
            {
                continue;
            }

            var folder = Path.Combine(library, "steamapps", "common", installDir);
            if (Directory.Exists(folder))
            {
                return folder;
            }
        }

        return null;
    }

    /// <summary>The "installdir" value of an appmanifest file, or null.</summary>
    public static string? ParseInstallDir(string manifestText)
    {
        var match = InstallDirRegex().Match(manifestText);
        return match.Success && match.Groups[1].Value.Length > 0 ? match.Groups[1].Value : null;
    }

    /// <summary>The library paths listed in a libraryfolders.vdf file ("path" entries, with doubled backslashes undone).</summary>
    public static IReadOnlyList<string> ParseLibraryPaths(string vdfText) =>
        LibraryPathRegex().Matches(vdfText)
            .Select(m => m.Groups[1].Value.Replace(@"\\", @"\"))
            .Where(p => p.Length > 0)
            .ToList();

    private static IEnumerable<string> GetLibraryFolders()
    {
        var steamPath = GetSteamPath();
        if (steamPath is null)
        {
            yield break;
        }

        yield return steamPath;

        var vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf))
        {
            yield break;
        }

        string text;
        try
        {
            text = File.ReadAllText(vdf);
        }
        catch (IOException)
        {
            yield break;
        }

        foreach (var path in ParseLibraryPaths(text))
        {
            if (!string.Equals(path, steamPath, StringComparison.OrdinalIgnoreCase))
            {
                yield return path;
            }
        }
    }

    private static string? GetSteamPath()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            var path = key?.GetValue("SteamPath") as string;
            return string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    [GeneratedRegex("\"installdir\"\\s+\"([^\"]*)\"", RegexOptions.IgnoreCase)]
    private static partial Regex InstallDirRegex();

    [GeneratedRegex("\"path\"\\s+\"([^\"]*)\"", RegexOptions.IgnoreCase)]
    private static partial Regex LibraryPathRegex();
}
