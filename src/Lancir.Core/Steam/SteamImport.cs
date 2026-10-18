using Lancir.Core.Models;

namespace Lancir.Core.Steam;

public enum SteamImportState
{
    /// <summary>Not in the library yet.</summary>
    New,

    /// <summary>Already in the library with this Steam app id.</summary>
    InLibrary,

    /// <summary>In the library without a Steam id, but with the same name: it gets linked.</summary>
    MatchedByName,
}

/// <summary>One line of the import list: a Steam game and what importing it would do.</summary>
public sealed record SteamImportRow(SteamOwnedGame Game, SteamImportState State, int? ExistingGameId);

/// <summary>What the import window needs after asking Steam: the rows, or the reason there are none.</summary>
public sealed record SteamImportPlan(SteamFetchStatus Status, IReadOnlyList<SteamImportRow> Rows)
{
    /// <summary>Not configured (no profile or key) is reported without calling Steam.</summary>
    public bool NotConfigured { get; init; }
}

public sealed record SteamImportResult(int Added, int Updated, IReadOnlyList<int> NewGameIds);

public static class SteamImport
{
    /// <summary>
    /// Brings a game's playtime up to Steam's. Lancir's own tracked time is kept; only the part that Steam knows about
    /// and Lancir does not (before Lancir, or while it was closed) is added, and it is replaced - never stacked - on the
    /// next sync. Playtime never goes down: if Lancir has tracked more than Steam reports, nothing is added.
    /// </summary>
    public static void ApplyPlaytime(LibraryGame game, long steamSeconds)
    {
        var lancirOwn = Math.Max(0, game.PlaytimeSeconds - game.SteamPlaytimeOffsetSeconds);
        var offset = Math.Max(0, steamSeconds - lancirOwn);
        game.PlaytimeSeconds = lancirOwn + offset;
        game.SteamPlaytimeOffsetSeconds = offset;
    }

    /// <summary>Lower-case letters and digits only, so "The Witcher® 3: Wild Hunt" and "witcher 3 wild hunt" can be compared.</summary>
    public static string NormalizeName(string name) =>
        new(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    /// <summary>
    /// Decides, for every Steam game, whether it is new, already in the library or matches a library game by name.
    /// A library game can be claimed by only one Steam game by name.
    /// </summary>
    public static IReadOnlyList<SteamImportRow> BuildRows(IReadOnlyList<SteamOwnedGame> owned, IReadOnlyList<LibraryGame> library)
    {
        var byAppId = new Dictionary<int, LibraryGame>();
        foreach (var game in library.Where(g => g.SteamAppId is not null))
        {
            byAppId.TryAdd(game.SteamAppId!.Value, game);
        }

        var unlinkedByName = new Dictionary<string, LibraryGame>();
        foreach (var game in library.Where(g => g.SteamAppId is null))
        {
            var key = NormalizeName(game.DisplayName);
            if (key.Length > 0)
            {
                unlinkedByName.TryAdd(key, game);
            }
        }

        var rows = new List<SteamImportRow>();
        foreach (var game in owned)
        {
            if (byAppId.TryGetValue(game.AppId, out var existing))
            {
                rows.Add(new SteamImportRow(game, SteamImportState.InLibrary, existing.Id));
                continue;
            }

            var key = NormalizeName(game.Name);
            if (key.Length > 0 && unlinkedByName.Remove(key, out var byName))
            {
                rows.Add(new SteamImportRow(game, SteamImportState.MatchedByName, byName.Id));
                continue;
            }

            rows.Add(new SteamImportRow(game, SteamImportState.New, null));
        }

        return rows;
    }
}
