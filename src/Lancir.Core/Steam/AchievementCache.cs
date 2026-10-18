using System.Text.Json;
using Lancir.Core.Services;

namespace Lancir.Core.Steam;

/// <summary>
/// Keeps the last fetched achievements of each game on disk (one JSON file per Steam app id), so the card shows at once,
/// works offline, and Steam is asked only when the user refreshes or a play session ends.
/// </summary>
public static class AchievementCache
{
    private sealed record Entry(DateTime FetchedUtc, bool HasAchievements, List<SteamAchievement> Items);

    private static string PathFor(string folder, int appId) => Path.Combine(folder, $"{appId}.json");

    public static AchievementsView? Load(int appId, string? folder = null)
    {
        try
        {
            var path = PathFor(folder ?? AppPaths.AchievementsFolder, appId);
            if (!File.Exists(path))
            {
                return null;
            }

            var entry = JsonSerializer.Deserialize<Entry>(File.ReadAllText(path));
            return entry is null
                ? null
                : new AchievementsView(
                    entry.HasAchievements ? AchievementsStatus.Ok : AchievementsStatus.NoAchievements,
                    entry.Items, entry.FetchedUtc, FromCache: true);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null; // a broken cache file is just a cache miss
        }
    }

    /// <summary>Stores a successful answer (with or without achievements); failures are never cached.</summary>
    public static void Save(int appId, AchievementsView view, string? folder = null)
    {
        if (view.Status is not (AchievementsStatus.Ok or AchievementsStatus.NoAchievements))
        {
            return;
        }

        try
        {
            var target = folder ?? AppPaths.AchievementsFolder;
            Directory.CreateDirectory(target);
            var entry = new Entry(view.FetchedUtc ?? DateTime.UtcNow, view.Status == AchievementsStatus.Ok, view.Items.ToList());
            File.WriteAllText(PathFor(target, appId), JsonSerializer.Serialize(entry));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The cache is an optimisation; not being able to write it is not an error.
        }
    }
}
