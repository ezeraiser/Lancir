namespace Lancir.Core.Services;

public sealed class AppSettings
{
    /// <summary>
    /// Optional SteamGridDB API key. When set, its cover images are preferred over Steam's own
    /// header image / CDN fallback.
    /// </summary>
    public string? SteamGridDbApiKey { get; set; }

    /// <summary>The user's Steam profile: a community address (steamcommunity.com/id/name or /profiles/id),
    /// a custom-URL name or the 17-digit SteamID64. Used for importing owned games and playtime.</summary>
    public string? SteamProfile { get; set; }

    /// <summary>The Steam Web API key, encrypted with the Windows user's key (see <see cref="SecretProtector"/>).
    /// Read and write it through <see cref="SettingsService"/>.</summary>
    public string? SteamApiKeyProtected { get; set; }

    /// <summary>When true, Steam playtime of games already in the library is refreshed once at startup.</summary>
    public bool SyncSteamPlaytimeOnStart { get; set; }

    /// <summary>
    /// Optional IGDB (Twitch Developer Console) app credentials, used as a further cover image fallback and for update
    /// checks. There are no built-in credentials: each user enters their own in Settings, and without them IGDB is skipped.
    /// </summary>
    public string? IgdbClientId { get; set; }

    public string? IgdbClientSecret { get; set; }

    /// <summary>Whether the left navigation pane was collapsed to icons when the app last ran.</summary>
    public bool IsNavCollapsed { get; set; }

    /// <summary>True when the library is shown as a list instead of a grid of covers.</summary>
    public bool IsLibraryListView { get; set; }

    /// <summary>Whether the play button appears over a game's cover when the pointer rests on it in grid view.</summary>
    public bool ShowGridPlayButton { get; set; } = true;

    /// <summary>Whether memory is cleaned automatically (see <see cref="MemoryTrimThresholdMb"/>).</summary>
    public bool MemoryTrimEnabled { get; set; } = true;

    /// <summary>Working set (MB) above which an automatic memory clean-up runs.</summary>
    public int MemoryTrimThresholdMb { get; set; } = 300;

    /// <summary>Also clean memory shortly after the window goes to the background. Off by default: it makes Windows page memory back in.</summary>
    public bool MemoryTrimOnBackground { get; set; }

    /// <summary>
    /// How often (seconds, 5-600) Lancir looks for a newly started game while none is running. A game that is running is
    /// watched every few seconds regardless, and a session's start is backdated to the process start time, so a long interval
    /// only delays noticing the start, it does not lose playtime.
    /// </summary>
    public int PlaytimeCheckIntervalSeconds { get; set; } = 30;

    /// <summary>Library sort mode name: Custom, Name, AddedDate or ReleaseDate.</summary>
    public string LibrarySortMode { get; set; } = "Custom";

    /// <summary>Font family for game names; null = the app's default font.</summary>
    public string? GameNameFontFamily { get; set; }

    /// <summary>Base font size of game names (library cards use it as-is, list/details derive from it).</summary>
    public double GameNameFontSize { get; set; } = 14;

    /// <summary>Game name weight: Default (varies by place), Normal, SemiBold or Bold.</summary>
    public string GameNameFontWeight { get; set; } = "Default";

    /// <summary>User-chosen share (0-1) of the content width taken by the game details pane in grid view;
    /// null = default layout. A fraction (not pixels) so the layout scales with the window.</summary>
    public double? DetailsPaneFractionGrid { get; set; }

    /// <summary>Same as <see cref="DetailsPaneFractionGrid"/> for list view.</summary>
    public double? DetailsPaneFractionList { get; set; }

    /// <summary>Main window position/size as of the last close (restored when not maximized); null until first saved.</summary>
    public double? WindowLeft { get; set; }

    public double? WindowTop { get; set; }

    public double? WindowWidth { get; set; }

    public double? WindowHeight { get; set; }

    public bool WindowMaximized { get; set; }

    /// <summary>Id of the game selected in the library when the app last ran; reselected on the next start.</summary>
    public int? LastSelectedGameId { get; set; }

    /// <summary>App language: "System" (follow Windows; English if unsupported), "en" or "tr".</summary>
    public string Language { get; set; } = "System";

    /// <summary>Order and size of the cards in the game details panel; empty = default layout.
    /// Size is one of Full, Auto, Left, Right.</summary>
    public List<DetailCardSetting> DetailCards { get; set; } = [];
}

public sealed class DetailCardSetting
{
    public string Id { get; set; } = "";

    public string Size { get; set; } = "Auto";
}
