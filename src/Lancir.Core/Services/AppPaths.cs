namespace Lancir.Core.Services;

public static class AppPaths
{
    public static string AppDataFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lancir");

    public static string DatabasePath { get; } = Path.Combine(AppDataFolder, "library.db");

    public static string SettingsPath { get; } = Path.Combine(AppDataFolder, "settings.json");

    public static string ImageCacheFolder { get; } = Path.Combine(AppDataFolder, "cache", "images");

    /// <summary>Downloaded Steam media (screenshots, clips, description pictures), one subfolder per game id.</summary>
    /// <summary>Cached Steam achievements, one JSON file per Steam app id.</summary>
    public static string AchievementsFolder { get; } = Path.Combine(AppDataFolder, "cache", "achievements");

    /// <summary>Achievement icons downloaded from Steam (one small file per icon), so they are fetched only once.</summary>
    public static string AchievementIconsFolder { get; } = Path.Combine(AppDataFolder, "cache", "achievements", "icons");

    /// <summary>Downscaled copies of large pictures (made in the background), so showing them again decodes a small file.</summary>
    public static string ThumbnailFolder { get; } = Path.Combine(AppDataFolder, "cache", "thumbs");

    public static string SteamMediaFolder { get; } = Path.Combine(AppDataFolder, "cache", "steam");

    public static void EnsureFoldersExist()
    {
        Directory.CreateDirectory(AppDataFolder);
        Directory.CreateDirectory(ImageCacheFolder);
        Directory.CreateDirectory(SteamMediaFolder);
        Directory.CreateDirectory(ThumbnailFolder);
        Directory.CreateDirectory(AchievementsFolder);
    }
}
