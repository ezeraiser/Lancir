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

    /// <summary>Start Lancir with Windows (a per-user Run entry; the registry is the source of truth, this remembers the choice).</summary>
    public bool RunAtStartup { get; set; }

    /// <summary>Start minimized (to the notification area when <see cref="MinimizeToTray"/> is on, else to the taskbar).</summary>
    public bool StartMinimized { get; set; }

    /// <summary>
    /// Start minimized only when Windows starts the app at sign-in (the "start with Windows" entry), not when the user opens it
    /// themselves. <see cref="StartMinimized"/> is the separate "every launch" option.
    /// </summary>
    public bool StartMinimizedAtWindowsStartup { get; set; }

    /// <summary>Minimizing sends the window to the notification area (bottom right) instead of the taskbar.</summary>
    public bool MinimizeToTray { get; set; }

    /// <summary>The window's close (X) button sends it to the notification area instead of quitting.</summary>
    public bool CloseToTray { get; set; }

    /// <summary>How downloaded pictures (screenshots, store pictures, covers, backgrounds) are stored on disk.</summary>
    public ImageStorageMode ImageStorage { get; set; } = ImageStorageMode.CompressReplace;

    /// <summary>
    /// The image settings ("mode|quality") the cache was last fully converted for. While it matches the current settings the
    /// optimizer has nothing to do and does not even scan the cache at startup; changing the settings makes it run once.
    /// </summary>
    public string? ImageOptimizedFor { get; set; }

    /// <summary>WebP quality (1-100) used when compressing pictures: 70 smallest, 80 balanced, 90 high quality.</summary>
    public int ImageQuality { get; set; } = 80;

    /// <summary>
    /// How the short clips of the store descriptions are stored: as downloaded, re-encoded smaller with the original kept in a
    /// separate folder, or re-encoded with the original deleted. Off by default: unlike pictures, a re-encoded clip is visibly
    /// different (a lower bit rate), so it is the user's choice.
    /// </summary>
    public ImageStorageMode ClipStorage { get; set; } = ImageStorageMode.Original;

    /// <summary>How hard clips are re-encoded when <see cref="ClipStorage"/> is on.</summary>
    public ClipQualityPreset ClipQuality { get; set; } = ClipQualityPreset.Balanced;

    /// <summary>Library sort mode name: Custom, Name, AddedDate or ReleaseDate.</summary>
    public string LibrarySortMode { get; set; } = "Custom";

    /// <summary>Font family for game names; null = the app's default font.</summary>
    public string? GameNameFontFamily { get; set; }

    /// <summary>Base font size of game names (library cards use it as-is, list/details derive from it).</summary>
    public double GameNameFontSize { get; set; } = 14;

    /// <summary>Zoom of the whole interface, 0.5 to 1 (the title bar's zoom control).</summary>
    public double UiZoom { get; set; } = 1;

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

    /// <summary>Layouts of the games whose card layout was changed on their own (game id to cards); every other game uses <see cref="DetailCards"/>.</summary>
    public Dictionary<int, List<DetailCardSetting>> GameCardLayouts { get; set; } = [];

    /// <summary>How many columns the cards and the pictures use, for games without their own options (see <see cref="GameCardOptions"/>).</summary>
    public CardViewOptions DefaultCardOptions { get; set; } = new();

    /// <summary>The same for the games whose layout was changed on their own (game id to options).</summary>
    public Dictionary<int, CardViewOptions> GameCardOptions { get; set; } = [];

    /// <summary>The status libraries in the left strip (Oynuyorum, Bitirdim, Bıraktım, Listede): each can be hidden in Settings.</summary>
    public bool ShowPlayingLibrary { get; set; } = true;

    public bool ShowFinishedLibrary { get; set; } = true;

    public bool ShowDroppedLibrary { get; set; } = true;

    public bool ShowBacklogLibrary { get; set; } = true;

    public bool ShowFavoritesLibrary { get; set; } = true;

    /// <summary>
    /// Sleep mode: while the window is minimized or hidden in the notification area the app stops everything but the game check
    /// (players closed, memory released, lowest priority, "efficiency mode"). On by default; it has no switch in Settings (a place for
    /// advanced options may hold one later), only this value in settings.json.
    /// </summary>
    public bool BackgroundSleepEnabled { get; set; } = true;

    // ---- backups ----

    /// <summary>Automatic backups: each switch makes a backup when its time has come (the newest <see cref="BackupKeepCount"/> of each kind are kept).</summary>
    public bool BackupDailyEnabled { get; set; }

    public bool BackupWeeklyEnabled { get; set; }

    public bool BackupMonthlyEnabled { get; set; }

    /// <summary>Whether a backup also holds the pictures (covers, backgrounds, store pictures, originals); without them it stays small.</summary>
    public bool BackupIncludeMedia { get; set; } = true;

    /// <summary>How many automatic backups of each kind (daily, weekly, monthly) are kept; older ones are deleted when a new one is taken.</summary>
    public int BackupKeepCount { get; set; } = 3;

    /// <summary>The folder for backups; null means "backups" inside the data folder.</summary>
    public string? BackupFolder { get; set; }

    public DateTime? LastBackupDailyUtc { get; set; }

    public DateTime? LastBackupWeeklyUtc { get; set; }

    public DateTime? LastBackupMonthlyUtc { get; set; }

    /// <summary>The order of the libraries in the left strip (keys such as "all", "favorites", "status:Playing", "custom:id"); the user drags them around.</summary>
    public List<string> LibraryOrder { get; set; } = [];

    /// <summary>Libraries the user made (a named list of games), in the order they appear in the left strip.</summary>
    public List<CustomLibrarySetting> CustomLibraries { get; set; } = [];
}

/// <summary>A library made by the user: a name and the games in it.</summary>
public sealed class CustomLibrarySetting
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "";

    public List<int> GameIds { get; set; } = [];

    /// <summary>The name of the built-in icon chosen for the library (a Fluent symbol), or null for the folder.</summary>
    public string? IconName { get; set; }

    /// <summary>A picture the user loaded as the library's icon (a small PNG in the app's data folder), or null.</summary>
    public string? IconPath { get; set; }
}

/// <summary>Column choices of the game details panel.</summary>
public sealed class CardViewOptions
{
    /// <summary>Columns of cards: 0 chooses from the width of the panel, otherwise 1 to 4.</summary>
    public int Columns { get; set; }

    /// <summary>Pictures per row in the "Görseller" card: 2 to 4.</summary>
    public int GalleryColumns { get; set; } = 2;
}

public sealed class DetailCardSetting
{
    public string Id { get; set; } = "";

    public string Size { get; set; } = "Auto";

    /// <summary>True when the card is hidden (it can be shown again in the panel's edit mode).</summary>
    public bool Hidden { get; set; }

    /// <summary>Columns the card spans (1 to 10), or 0 to follow <see cref="Size"/> (Full spans every column, the others one).</summary>
    public int Span { get; set; }

    /// <summary>Height of the card in rows (a fixed height, the content scrolls inside), or 0 for the height of the content.</summary>
    public int Rows { get; set; }
}

/// <summary>How downloaded pictures are kept on disk.</summary>
public enum ImageStorageMode
{
    /// <summary>Exactly as downloaded: no compression.</summary>
    Original,

    /// <summary>Converted to WebP for use, the downloaded original kept in a separate folder.</summary>
    CompressKeepOriginal,

    /// <summary>Converted to WebP and the original deleted (smallest on disk; it can be downloaded again from the store).</summary>
    CompressReplace,
}
