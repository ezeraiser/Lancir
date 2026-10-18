namespace Lancir.Core.Models;

/// <summary>
/// A game the user has confirmed and added to their library. Persisted entity.
/// </summary>
public sealed class LibraryGame
{
    public int Id { get; set; }

    public required string FolderPath { get; set; }

    public string? ExecutablePath { get; set; }

    public required string DisplayName { get; set; }

    /// <summary>Steam app id, filled when the game is matched against the Steam store. Used for the
    /// "start via Steam" option (steam://rungameid/{id}) and for refreshing store data.</summary>
    public int? SteamAppId { get; set; }

    /// <summary>When true (and <see cref="SteamAppId"/> is set) the game is started through the steam://
    /// protocol instead of running its executable directly.</summary>
    public bool LaunchViaSteam { get; set; }

    /// <summary>
    /// Process name(s) (e.g. "Game-Win64-Shipping" or "Game.exe"; several separated by commas) that
    /// PlaytimeTracker counts instead of the executable, for games whose launcher exe starts the real game
    /// exe and exits. The game counts as running while any of them runs. Null = the executable.
    /// </summary>
    public string? TrackedProcessName { get; set; }

    /// <summary>Start the executable elevated ("Run as administrator"). Ignored for URI launches.</summary>
    public bool RunAsAdmin { get; set; }

    /// <summary>
    /// A custom launch address (e.g. "com.epicgames.launcher://apps/...", "goggalaxy://openGameView/...",
    /// "shell:AppsFolder\..."). When set it is opened instead of the executable and takes precedence over
    /// <see cref="LaunchViaSteam"/>.
    /// </summary>
    public string? LaunchUri { get; set; }

    /// <summary>SteamGridDB game id this library entry is matched to, if any.</summary>
    public int? SteamGridDbId { get; set; }

    public string? Description { get; set; }

    /// <summary>Local file path to the cached cover image, if one was downloaded.</summary>
    public string? CoverImagePath { get; set; }

    /// <summary>Local file path to the cached background/artwork image, if one was downloaded.</summary>
    public string? BackgroundImagePath { get; set; }

    /// <summary>The web address the cover was downloaded from (recorded since originals can be restored); null for older covers.</summary>
    public string? CoverImageUrl { get; set; }

    /// <summary>The web address the background was downloaded from; null for older backgrounds.</summary>
    public string? BackgroundImageUrl { get; set; }

    /// <summary>Comma-separated genre/tag list (e.g. "Shooter, Adventure"), from IGDB.</summary>
    public string? Genres { get; set; }

    /// <summary>Community rating in [0, 100], from IGDB (aggregated critic + user rating).</summary>
    public double? CommunityRating { get; set; }

    /// <summary>Original release date, used to sort the library newest-first.</summary>
    public DateTime? ReleaseDateUtc { get; set; }

    public DateTime AddedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>IGDB game id this library entry is matched to, if any.</summary>
    public int? IgdbId { get; set; }

    /// <summary>
    /// IGDB's "updated_at" timestamp as of the last update check. A newer value on a
    /// subsequent check indicates IGDB's listing for the game has changed (e.g. a new release).
    /// </summary>
    public DateTime? IgdbLastUpdatedAtUtc { get; set; }

    public DateTime? LastUpdateCheckUtc { get; set; }

    /// <summary>Total time the game's executable has been running, in seconds. Counted by
    /// PlaytimeTracker no matter how the game was started.</summary>
    public long PlaytimeSeconds { get; set; }

    /// <summary>
    /// How many seconds of <see cref="PlaytimeSeconds"/> came from Steam (time played before Lancir tracked the
    /// game, or while Lancir was not running). Lets a later Steam sync replace it instead of adding it twice.
    /// </summary>
    public long SteamPlaytimeOffsetSeconds { get; set; }

    /// <summary>Position in the user's custom (drag-and-drop) ordering, 1-based. 0 = never placed,
    /// which sorts before placed games so newly added games appear at the top.</summary>
    public int SortOrder { get; set; }

    public bool IsFavorite { get; set; }

    public GameStatus Status { get; set; }

    /// <summary>The user's own rating, 0 (none) to 5 stars.</summary>
    public int UserRating { get; set; }

    public string? Notes { get; set; }

    /// <summary>The user's own tags, comma-separated.</summary>
    public string? UserTags { get; set; }

    /// <summary>Command-line arguments passed when Lancir starts the game.</summary>
    public string? LaunchArguments { get; set; }

    /// <summary>Working directory used when Lancir starts the game; null = the executable's folder.</summary>
    public string? WorkingDirectory { get; set; }

    public DateTime? LastPlayedUtc { get; set; }

    /// <summary>Number of recorded play sessions.</summary>
    public int PlayCount { get; set; }

    // ---- store metadata (filled when matched against Steam) ----

    /// <summary>
    /// The description with media: like <see cref="Description"/> (headings "# ...", items "- ..."), plus
    /// lines "!video mp4Url|width|height" and "!image url" where the store page shows clips/pictures.
    /// </summary>
    public string? DescriptionRich { get; set; }

    public string? Developers { get; set; }

    public string? Publishers { get; set; }

    /// <summary>Comma-separated play modes, e.g. "Tek oyunculu, Co-op".</summary>
    public string? Modes { get; set; }

    /// <summary>Comma-separated supported interface languages.</summary>
    public string? Languages { get; set; }

    public string? AgeRating { get; set; }

    public string? Website { get; set; }

    /// <summary>Screenshot URLs, one per line.</summary>
    public string? ScreenshotUrls { get; set; }

    public string? TrailerUrl { get; set; }

    public string? TrailerThumbnailUrl { get; set; }
}
