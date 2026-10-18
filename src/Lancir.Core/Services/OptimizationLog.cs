using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lancir.Core.Services;

/// <summary>What compression did for one game: the last time, and the totals so far.</summary>
public sealed record GameOptimization(
    int GameId,
    DateTime LastRunUtc,
    int LastConverted,
    long LastBefore,
    long LastAfter,
    int TotalConverted,
    long TotalBefore,
    long TotalAfter)
{
    public long LastSaved => LastBefore - LastAfter;

    public long TotalSaved => TotalBefore - TotalAfter;
}

/// <summary>
/// Remembers, per game, when its media was last compressed and how much space that saved. There is one log for pictures
/// (covers, backgrounds, screenshots, store pictures) and one for clips; both are written by the optimizers (and for pictures
/// by the downloads that compress on the way in) and shown on the Settings page. Each is a small JSON file next to the caches.
/// </summary>
public sealed partial class OptimizationLog
{
    private readonly object _sync = new();
    private readonly string _fileName;

    private OptimizationLog(string fileName) => _fileName = fileName;

    /// <summary>The log of picture compression.</summary>
    public static OptimizationLog Pictures { get; } = new("optimization-log.json");

    /// <summary>The log of clip (video) compression.</summary>
    public static OptimizationLog Clips { get; } = new("clip-optimization-log.json");

    private string LogPath => Path.Combine(AppPaths.AppDataFolder, "cache", _fileName);

    /// <summary>The game a cached file belongs to, from where it is stored, or null when it cannot be told.</summary>
    public static int? GameIdFor(string file)
    {
        var full = Path.GetFullPath(file);
        var steam = Path.GetFullPath(AppPaths.SteamMediaFolder) + Path.DirectorySeparatorChar;
        if (full.StartsWith(steam, StringComparison.OrdinalIgnoreCase))
        {
            var first = full[steam.Length..].Split(Path.DirectorySeparatorChar)[0];
            return int.TryParse(first, out var steamGameId) ? steamGameId : null;
        }

        var match = CoverNameRegex().Match(Path.GetFileName(full));
        return match.Success && int.TryParse(match.Groups[1].Value, out var coverGameId) ? coverGameId : null;
    }

    /// <summary>Forgets a game in every log (when the game leaves the library).</summary>
    public static void RemoveEverywhere(int gameId)
    {
        Pictures.Remove(gameId);
        Clips.Remove(gameId);
    }

    /// <summary>All games that have been optimized, most recent first.</summary>
    public IReadOnlyList<GameOptimization> Load()
    {
        lock (_sync)
        {
            return Read().OrderByDescending(e => e.LastRunUtc).ToList();
        }
    }

    /// <summary>Adds the result of compressing some of a game's files: they replace the "last" figures and add to the totals.</summary>
    public void Record(int gameId, int converted, long before, long after) =>
        RecordMany([(gameId, converted, before, after)]);

    public void RecordMany(IEnumerable<(int GameId, int Converted, long Before, long After)> results)
    {
        var items = results.Where(r => r.Converted > 0).ToList();
        if (items.Count == 0)
        {
            return;
        }

        lock (_sync)
        {
            var entries = Read().ToDictionary(e => e.GameId);
            var now = DateTime.UtcNow;
            foreach (var (gameId, converted, before, after) in items)
            {
                entries.TryGetValue(gameId, out var old);
                entries[gameId] = new GameOptimization(
                    gameId, now, converted, before, after,
                    (old?.TotalConverted ?? 0) + converted, (old?.TotalBefore ?? 0) + before, (old?.TotalAfter ?? 0) + after);
            }

            Write(entries.Values);
        }
    }

    /// <summary>Forgets a game's entry in this log.</summary>
    public void Remove(int gameId)
    {
        lock (_sync)
        {
            Write(Read().Where(e => e.GameId != gameId).ToList());
        }
    }

    private List<GameOptimization> Read()
    {
        try
        {
            return File.Exists(LogPath)
                ? JsonSerializer.Deserialize<List<GameOptimization>>(File.ReadAllText(LogPath)) ?? []
                : [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return []; // a damaged log is just an empty history
        }
    }

    private void Write(IEnumerable<GameOptimization> entries)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.WriteAllText(LogPath, JsonSerializer.Serialize(entries));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // the history is informational only
        }
    }

    [GeneratedRegex(@"^(\d+)(?:[_.]|$)")]
    private static partial Regex CoverNameRegex();
}
