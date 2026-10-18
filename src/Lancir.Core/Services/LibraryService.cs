using Lancir.Core.Data;
using Lancir.Core.GridImages;
using Lancir.Core.Matching;
using Lancir.Core.Models;
using Lancir.Core.Scanning;
using Lancir.Core.Steam;
using Microsoft.EntityFrameworkCore;

namespace Lancir.Core.Services;

/// <summary>
/// Orchestrates the scan -> confirm -> (optional) match flow and persists confirmed games to the
/// library database. Folder scanning never talks to the network: it only reads the filesystem, so
/// it stays fast and works offline. Matching (name/description/cover via SteamGridDB and IGDB) is
/// a separate, on-demand step run per game or in bulk from the library view.
/// </summary>
public sealed class LibraryService
{
    private readonly LibraryDbContext _dbContext;
    private readonly ISteamGridDbClient _steamGridDbClient;
    private readonly IIgdbClient _igdbClient;
    private readonly ISteamStoreClient _steamStoreClient;
    private readonly ISteamLibraryClient _steamLibraryClient;
    private readonly ISteamAchievementsClient _steamAchievementsClient;
    private (string Profile, string SteamId)? _resolvedSteamId;
    private readonly SteamMediaCache _steamMediaCache;
    private readonly ImageCacheService _imageCacheService;
    private readonly SettingsService _settingsService;
    private readonly DebugLogService _log;

    public LibraryService(
        LibraryDbContext dbContext,
        ISteamGridDbClient steamGridDbClient,
        IIgdbClient igdbClient,
        ISteamStoreClient steamStoreClient,
        ISteamLibraryClient steamLibraryClient,
        ISteamAchievementsClient steamAchievementsClient,
        SteamMediaCache steamMediaCache,
        ImageCacheService imageCacheService,
        SettingsService settingsService,
        DebugLogService log)
    {
        _dbContext = dbContext;
        _steamGridDbClient = steamGridDbClient;
        _igdbClient = igdbClient;
        _steamStoreClient = steamStoreClient;
        _steamLibraryClient = steamLibraryClient;
        _steamAchievementsClient = steamAchievementsClient;
        _steamMediaCache = steamMediaCache;
        _imageCacheService = imageCacheService;
        _settingsService = settingsService;
        _log = log;
    }

    public async Task<IReadOnlyList<string>> GetWatchedFolderPathsAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.WatchedFolders
            .Select(f => f.Path)
            .ToListAsync(cancellationToken);
    }

    public async Task AddWatchedFolderAsync(string path, CancellationToken cancellationToken = default)
    {
        var exists = await _dbContext.WatchedFolders.AnyAsync(f => f.Path == path, cancellationToken);
        if (exists)
        {
            return;
        }

        _dbContext.WatchedFolders.Add(new WatchedFolder { Path = path });
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveWatchedFolderByPathAsync(string path, CancellationToken cancellationToken = default)
    {
        var folder = await _dbContext.WatchedFolders.FirstOrDefaultAsync(f => f.Path == path, cancellationToken);
        if (folder is not null)
        {
            _dbContext.WatchedFolders.Remove(folder);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Marks an executable as "not a game" so it's excluded from future scan suggestions.
    /// </summary>
    public async Task IgnoreExecutableAsync(string executablePath, CancellationToken cancellationToken = default)
    {
        var exists = await _dbContext.IgnoredExecutables
            .AnyAsync(e => e.ExecutablePath == executablePath, cancellationToken);
        if (exists)
        {
            return;
        }

        _dbContext.IgnoredExecutables.Add(new IgnoredExecutable { ExecutablePath = executablePath });
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Marks an entire folder as "not a game" so it (and its executables) stop being suggested
    /// as a candidate in future scans — a shortcut over ignoring each executable individually.
    /// </summary>
    public async Task IgnoreFolderAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        var exists = await _dbContext.IgnoredFolders.AnyAsync(f => f.FolderPath == folderPath, cancellationToken);
        if (exists)
        {
            return;
        }

        _dbContext.IgnoredFolders.Add(new IgnoredFolder { FolderPath = folderPath });
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Scans all watched folders for new game candidates. Purely filesystem-based: no network
    /// calls, so it can't fail because of Steam being slow/unreachable/rate-limited.
    /// </summary>
    public async Task<IReadOnlyList<ScanResult>> ScanForNewGamesAsync(CancellationToken cancellationToken = default)
    {
        var watchedFolders = await GetWatchedFolderPathsAsync(cancellationToken);
        var existingFolderPaths = (await _dbContext.Games
            .Select(g => g.FolderPath)
            .ToListAsync(cancellationToken))
            .ToHashSet();
        var ignoredExecutablePaths = (await _dbContext.IgnoredExecutables
            .Select(e => e.ExecutablePath)
            .ToListAsync(cancellationToken))
            .ToHashSet();
        var ignoredFolderPaths = (await _dbContext.IgnoredFolders
            .Select(f => f.FolderPath)
            .ToListAsync(cancellationToken))
            .ToHashSet();

        var entries = FolderScanner.Scan(watchedFolders, ignoredExecutablePaths, ignoredFolderPaths)
            .Where(entry => !existingFolderPaths.Contains(entry.FolderPath))
            .ToList();

        return entries.Select(entry => new ScanResult { Entry = entry }).ToList();
    }

    /// <summary>
    /// Persists a reviewed scan result as a confirmed library entry. No match data is attached
    /// yet; call <see cref="MatchGameAsync"/> afterwards (per game or in bulk) to fill in the
    /// name/description/cover from SteamGridDB/IGDB.
    /// </summary>
    public async Task<LibraryGame> ConfirmGameAsync(
        GameEntry entry,
        string displayName,
        string? executablePath,
        CancellationToken cancellationToken = default)
    {
        var game = new LibraryGame
        {
            FolderPath = entry.FolderPath,
            ExecutablePath = executablePath,
            DisplayName = displayName,
        };

        _dbContext.Games.Add(game);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return game;
    }

    /// <summary>
    /// Adds a game the user typed in by hand (no scan): a game that isn't in a watched folder, or a
    /// Steam game that has no exe path. The folder defaults to the executable's folder when there is one.
    /// </summary>
    public async Task<LibraryGame> AddManualGameAsync(
        string displayName,
        string? executablePath,
        string? description,
        string? genres,
        int? steamAppId = null,
        GameLaunchSettings? launch = null,
        CancellationToken cancellationToken = default)
    {
        var exe = string.IsNullOrWhiteSpace(executablePath) ? null : executablePath.Trim();
        var game = new LibraryGame
        {
            FolderPath = exe is null ? "" : Path.GetDirectoryName(exe) ?? "",
            ExecutablePath = exe,
            DisplayName = displayName.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            Genres = string.IsNullOrWhiteSpace(genres) ? null : genres.Trim(),
            SteamAppId = steamAppId,
            LaunchViaSteam = launch?.LaunchViaSteam == true && steamAppId is not null,
            LaunchArguments = GameLaunchSettings.Clean(launch?.Arguments),
            WorkingDirectory = GameLaunchSettings.Clean(launch?.WorkingDirectory),
            TrackedProcessName = GameLaunchSettings.Clean(launch?.TrackedProcessName),
            RunAsAdmin = launch?.RunAsAdmin == true,
            LaunchUri = GameLaunchSettings.Clean(launch?.LaunchUri),
        };

        _dbContext.Games.Add(game);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _log.Log($"Elle eklendi: '{game.DisplayName}' (gameId={game.Id}).");

        return game;
    }

    /// <summary>True when a Steam profile and API key are both set in Settings.</summary>
    public bool IsSteamAccountConfigured =>
        !string.IsNullOrWhiteSpace(_settingsService.Load().SteamProfile) && !string.IsNullOrWhiteSpace(_settingsService.GetSteamApiKey());

    /// <summary>
    /// Asks Steam for the user's owned games (profile and key from Settings) and compares them with the library: each
    /// game is new, already in the library, or matches a library game by name.
    /// </summary>
    public async Task<SteamImportPlan> PlanSteamImportAsync(CancellationToken cancellationToken = default)
    {
        var fetch = await FetchSteamGamesAsync(cancellationToken);
        if (fetch is null)
        {
            return new SteamImportPlan(SteamFetchStatus.Failed, []) { NotConfigured = true };
        }

        if (fetch.Status != SteamFetchStatus.Ok)
        {
            return new SteamImportPlan(fetch.Status, []);
        }

        var library = await _dbContext.Games.ToListAsync(cancellationToken);
        return new SteamImportPlan(SteamFetchStatus.Ok, SteamImport.BuildRows(fetch.Games, library));
    }

    /// <summary>
    /// Imports the given rows: new games are added (started through Steam, with their Steam playtime), games already in
    /// the library get their Steam id (when matched by name), last-played date and Steam playtime.
    /// </summary>
    public async Task<SteamImportResult> ApplySteamImportAsync(
        IReadOnlyList<SteamImportRow> rows, CancellationToken cancellationToken = default)
    {
        var added = 0;
        var updated = 0;
        var newGames = new List<LibraryGame>();

        foreach (var row in rows)
        {
            var game = row.ExistingGameId is { } id ? await _dbContext.Games.FindAsync([id], cancellationToken) : null;
            if (game is null)
            {
                game = new LibraryGame
                {
                    FolderPath = "",
                    DisplayName = row.Game.Name,
                    SteamAppId = row.Game.AppId,
                    LaunchViaSteam = true,
                };
                _dbContext.Games.Add(game);
                newGames.Add(game);
                added++;
            }
            else
            {
                // PlaytimeTracker writes playtime through its own context, so this tracked copy can be stale:
                // reload before adding Steam's time on top of it, or tracked time would be overwritten.
                await _dbContext.Entry(game).ReloadAsync(cancellationToken);
                game.SteamAppId ??= row.Game.AppId;
                updated++;
            }

            ApplySteamPlaytimeTo(game, row.Game);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _log.Log($"Steam içe aktarma: {added} oyun eklendi, {updated} oyun güncellendi.");
        return new SteamImportResult(added, updated, newGames.Select(g => g.Id).ToList());
    }

    /// <summary>
    /// Refreshes the Steam playtime and last-played date of library games that have a Steam id (no games are added).
    /// Returns how many games changed, or null when Steam is not configured or could not be reached.
    /// </summary>
    public async Task<int?> SyncSteamPlaytimeAsync(CancellationToken cancellationToken = default)
    {
        var fetch = await FetchSteamGamesAsync(cancellationToken);
        if (fetch is null || fetch.Status != SteamFetchStatus.Ok)
        {
            return null;
        }

        var owned = fetch.Games.ToDictionary(g => g.AppId);
        var changed = 0;
        foreach (var game in await _dbContext.Games.Where(g => g.SteamAppId != null).ToListAsync(cancellationToken))
        {
            if (!owned.TryGetValue(game.SteamAppId!.Value, out var steamGame))
            {
                continue;
            }

            await _dbContext.Entry(game).ReloadAsync(cancellationToken); // see ApplySteamImportAsync: tracked copy may be stale
            if (ApplySteamPlaytimeTo(game, steamGame))
            {
                changed++;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _log.Log($"Steam süre senkronu: {changed} oyunun süresi güncellendi.");
        return changed;
    }

    /// <summary>
    /// The achievements of a library game. Served from the local cache when there is one (unless <paramref name="refresh"/>),
    /// otherwise asked from Steam and cached. When Steam can't be asked or fails, an older cached copy is still returned
    /// together with the reason, so the card never goes empty just because the network is down.
    /// </summary>
    public async Task<AchievementsView> GetAchievementsAsync(
        int? steamAppId, string gameName, bool refresh, CancellationToken cancellationToken = default)
    {
        // Deliberately no database access: this runs on every game selection, possibly overlapping other
        // work on the (single-threaded) context, so the caller passes what it already knows.
        if (steamAppId is not { } appId)
        {
            return AchievementsView.Empty(AchievementsStatus.NoSteamId);
        }

        var cached = AchievementCache.Load(appId);
        if (cached is not null && !refresh)
        {
            return cached;
        }

        var profile = _settingsService.Load().SteamProfile;
        var apiKey = _settingsService.GetSteamApiKey();
        if (string.IsNullOrWhiteSpace(profile) || string.IsNullOrWhiteSpace(apiKey))
        {
            return cached ?? AchievementsView.Empty(AchievementsStatus.NotConfigured);
        }

        if (_resolvedSteamId is not { } resolved || resolved.Profile != profile)
        {
            var steamId = await _steamLibraryClient.ResolveSteamIdAsync(profile, cancellationToken);
            if (steamId is null)
            {
                return cached ?? AchievementsView.Empty(AchievementsStatus.ProfileNotFound);
            }

            _resolvedSteamId = (profile, steamId);
        }

        var fresh = await _steamAchievementsClient.GetAchievementsAsync(
            apiKey, _resolvedSteamId!.Value.SteamId, appId, AppLanguage.SteamLanguage, cancellationToken);
        _log.Log($"Başarımlar: '{gameName}' durum={fresh.Status}, {fresh.Items.Count} başarım.");

        if (fresh.Status is AchievementsStatus.Ok or AchievementsStatus.NoAchievements)
        {
            AchievementCache.Save(appId, fresh);
            return fresh;
        }

        return cached is not null ? cached with { FromCache = true } : fresh;
    }

    private static bool ApplySteamPlaytimeTo(LibraryGame game, SteamOwnedGame steamGame)
    {
        var before = (game.PlaytimeSeconds, game.LastPlayedUtc);
        SteamImport.ApplyPlaytime(game, steamGame.PlaytimeSeconds);
        if (steamGame.LastPlayedUtc is { } last && (game.LastPlayedUtc is null || last > game.LastPlayedUtc))
        {
            game.LastPlayedUtc = last;
        }

        return before != (game.PlaytimeSeconds, game.LastPlayedUtc);
    }

    /// <summary>Resolves the configured profile and lists its games; null when the profile or key is not set.</summary>
    private async Task<SteamFetchResult?> FetchSteamGamesAsync(CancellationToken cancellationToken)
    {
        var profile = _settingsService.Load().SteamProfile;
        var apiKey = _settingsService.GetSteamApiKey();
        if (string.IsNullOrWhiteSpace(profile) || string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        var steamId = await _steamLibraryClient.ResolveSteamIdAsync(profile, cancellationToken);
        if (steamId is null)
        {
            return SteamFetchResult.Fail(SteamFetchStatus.ProfileNotFound);
        }

        var result = await _steamLibraryClient.GetOwnedGamesAsync(apiKey, steamId, cancellationToken);
        _log.Log($"Steam kütüphanesi: durum={result.Status}, {result.Games.Count} oyun.");
        return result;
    }

    /// <summary>Returns the library newest-added-first, per the library view's sort order.</summary>
    public async Task<List<LibraryGame>> GetLibraryAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Games
            .OrderByDescending(g => g.AddedAtUtc)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Updates the manually-editable fields on a library game from the details screen: name,
    /// description, genres, executable path. Does not touch match ids or images.
    /// </summary>
    public async Task UpdateGameDetailsAsync(
        int gameId,
        string displayName,
        string? description,
        string? genres,
        string? executablePath,
        int? steamAppId,
        GameLaunchSettings launch,
        CancellationToken cancellationToken = default)
    {
        var game = await _dbContext.Games.FindAsync([gameId], cancellationToken);
        if (game is null || string.IsNullOrWhiteSpace(displayName))
        {
            return;
        }

        game.DisplayName = displayName.Trim();
        var newDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (newDescription != game.Description)
        {
            game.DescriptionRich = null;
        }

        game.Description = newDescription;
        game.Genres = string.IsNullOrWhiteSpace(genres) ? null : genres.Trim();
        game.ExecutablePath = string.IsNullOrWhiteSpace(executablePath) ? null : executablePath.Trim();
        if (string.IsNullOrWhiteSpace(game.FolderPath) && game.ExecutablePath is not null)
        {
            // A game added without an exe (e.g. a Steam game) gets its folder once an exe is chosen.
            game.FolderPath = Path.GetDirectoryName(game.ExecutablePath) ?? "";
        }
        game.SteamAppId = steamAppId;
        game.LaunchViaSteam = launch.LaunchViaSteam && steamAppId is not null;
        game.LaunchArguments = GameLaunchSettings.Clean(launch.Arguments);
        game.WorkingDirectory = GameLaunchSettings.Clean(launch.WorkingDirectory);
        game.TrackedProcessName = GameLaunchSettings.Clean(launch.TrackedProcessName);
        game.RunAsAdmin = launch.RunAsAdmin;
        game.LaunchUri = GameLaunchSettings.Clean(launch.LaunchUri);

        await _dbContext.SaveChangesAsync(cancellationToken);
        _log.Log($"Detaylar güncellendi: '{game.DisplayName}' (gameId={gameId}).");
    }

    /// <summary>Persists the user's custom ordering: each game gets its 1-based position in <paramref name="orderedGameIds"/>.</summary>
    public async Task SaveCustomOrderAsync(IReadOnlyList<int> orderedGameIds, CancellationToken cancellationToken = default)
    {
        var positions = new Dictionary<int, int>();
        for (var i = 0; i < orderedGameIds.Count; i++)
        {
            positions[orderedGameIds[i]] = i + 1;
        }

        var games = await _dbContext.Games.ToListAsync(cancellationToken);
        foreach (var game in games)
        {
            if (positions.TryGetValue(game.Id, out var position))
            {
                game.SortOrder = position;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Saves changes made directly to tracked library games (favorite, status, notes, ...).</summary>
    public Task SaveAsync(CancellationToken cancellationToken = default) => _dbContext.SaveChangesAsync(cancellationToken);

    public async Task RemoveGameAsync(int gameId, CancellationToken cancellationToken = default)
    {
        _log.Log($"Kaldır: gameId={gameId} başlatıldı.");

        var game = await _dbContext.Games.FindAsync([gameId], cancellationToken);
        if (game is null)
        {
            _log.Log($"Kaldır: gameId={gameId} bulunamadı, işlem iptal.");
            return;
        }

        SteamMediaCache.DeleteFor(gameId);
        OptimizationLog.RemoveEverywhere(gameId);
        _dbContext.PlaySessions.RemoveRange(_dbContext.PlaySessions.Where(s => s.GameId == gameId));
        _dbContext.Games.Remove(game);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _log.Log($"Kaldır: '{game.DisplayName}' (gameId={gameId}) veritabanından silindi.");

        // The cover and background, in whatever form they are stored in now (original, WebP, or a kept original).
        MediaPaths.DeleteAllForms(game.CoverImagePath);
        MediaPaths.DeleteAllForms(game.BackgroundImagePath);
        _log.Log($"Kaldır: kapak ve arka plan dosyaları silindi (gameId={gameId}).");
    }

    /// <summary>
    /// Searches SteamGridDB for candidate matches for a library game's current display name,
    /// without applying anything. Lets the caller show the user a picker instead of blindly
    /// trusting the top-scoring result, which can be wrong for ambiguous or generic names.
    /// </summary>
    public async Task<IReadOnlyList<MatchCandidate>> SearchSteamGridDbCandidatesAsync(
        int gameId, CancellationToken cancellationToken = default)
    {
        _log.Log($"SteamGridDB arama: gameId={gameId} başlatıldı.");

        var game = await _dbContext.Games.FindAsync([gameId], cancellationToken);
        if (game is null)
        {
            _log.Log($"SteamGridDB arama: gameId={gameId} bulunamadı.");
            return [];
        }

        var settings = _settingsService.Load();
        if (string.IsNullOrWhiteSpace(settings.SteamGridDbApiKey))
        {
            _log.Log("SteamGridDB arama: SteamGridDB API key ayarlanmamış, işlem iptal.");
            return [];
        }

        var searchTerm = SearchQueryBuilder.BuildSearchTerm(game.DisplayName);
        _log.Log($"SteamGridDB arama: '{game.DisplayName}' -> arama terimi '{searchTerm}' ile SteamGridDB'de arama yapılıyor.");
        var matches = await _steamGridDbClient.SearchGamesAsync(searchTerm, settings.SteamGridDbApiKey, cancellationToken);
        _log.Log($"SteamGridDB arama: {matches.Count} aday bulundu.");

        return matches
            .Select(match => new MatchCandidate
            {
                Source = MatchSource.SteamGridDb,
                ExternalId = match.GridDbGameId,
                Name = match.Name,
                CoverImageUrl = match.CoverImageUrl,
                BackgroundImageUrl = match.BackgroundImageUrl,
                ReleaseDateUtc = match.ReleaseDateUtc,
            })
            .ToList();
    }

    /// <summary>
    /// Searches IGDB for candidate matches for a library game's current display name, without
    /// applying anything.
    /// </summary>
    public async Task<IReadOnlyList<MatchCandidate>> SearchIgdbCandidatesAsync(
        int gameId, CancellationToken cancellationToken = default)
    {
        _log.Log($"IGDB arama: gameId={gameId} başlatıldı.");

        var game = await _dbContext.Games.FindAsync([gameId], cancellationToken);
        if (game is null)
        {
            _log.Log($"IGDB arama: gameId={gameId} bulunamadı.");
            return [];
        }

        var settings = _settingsService.Load();
        if (string.IsNullOrWhiteSpace(settings.IgdbClientId) || string.IsNullOrWhiteSpace(settings.IgdbClientSecret))
        {
            _log.Log("IGDB arama: IGDB (Twitch) Client ID/Secret ayarlanmamış, işlem iptal.");
            return [];
        }

        var searchTerm = SearchQueryBuilder.BuildSearchTerm(game.DisplayName);
        _log.Log($"IGDB arama: '{game.DisplayName}' -> arama terimi '{searchTerm}' ile IGDB'de arama yapılıyor.");
        var matches = await _igdbClient.SearchGamesAsync(
            searchTerm, settings.IgdbClientId, settings.IgdbClientSecret, cancellationToken: cancellationToken);
        _log.Log($"IGDB arama: {matches.Count} aday bulundu.");

        return matches
            .Select(match => new MatchCandidate
            {
                Source = MatchSource.Igdb,
                ExternalId = match.IgdbId,
                Name = match.Name,
                Description = match.Summary,
                CoverImageUrl = match.CoverImageUrl,
                BackgroundImageUrl = match.BackgroundImageUrl,
                Genres = match.Genres,
                CommunityRating = match.CommunityRating,
                ReleaseDateUtc = match.ReleaseDateUtc,
            })
            .ToList();
    }

    /// <summary>
    /// Applies a user-picked SteamGridDB candidate to a library game: fills in name/cover.
    /// SteamGridDB has no game description, so the description is left untouched.
    /// </summary>
    public async Task ApplySteamGridDbMatchAsync(int gameId, MatchCandidate candidate, CancellationToken cancellationToken = default)
    {
        _log.Log($"SteamGridDB uygula: gameId={gameId} GridDbGameId={candidate.ExternalId} başlatıldı.");

        var game = await _dbContext.Games.FindAsync([gameId], cancellationToken);
        if (game is null)
        {
            _log.Log($"SteamGridDB uygula: gameId={gameId} bulunamadı.");
            return;
        }

        game.SteamGridDbId = candidate.ExternalId;
        game.DisplayName = candidate.Name;
        game.ReleaseDateUtc = candidate.ReleaseDateUtc;

        if (!string.IsNullOrWhiteSpace(candidate.CoverImageUrl))
        {
            _log.Log($"SteamGridDB uygula: kapak resmi indiriliyor ({candidate.CoverImageUrl}).");
            game.CoverImagePath = await _imageCacheService.DownloadAndCacheAsync(
                game.Id, candidate.CoverImageUrl, cancellationToken);
            game.CoverImageUrl = candidate.CoverImageUrl;
        }

        if (!string.IsNullOrWhiteSpace(candidate.BackgroundImageUrl))
        {
            _log.Log($"SteamGridDB uygula: arka plan görseli indiriliyor ({candidate.BackgroundImageUrl}).");
            game.BackgroundImagePath = await _imageCacheService.DownloadAndCacheAsync(
                game.Id, candidate.BackgroundImageUrl, cancellationToken, fileNameSuffix: "background");
            game.BackgroundImageUrl = candidate.BackgroundImageUrl;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _log.Log($"SteamGridDB uygula: '{game.DisplayName}' (gameId={gameId}) başarıyla güncellendi.");
    }

    /// <summary>
    /// Resolves full field data for a SteamGridDB candidate without applying anything. SteamGridDB
    /// candidates already carry everything needed from the search step, so this just re-wraps it.
    /// </summary>
    public static MatchPreview ResolveSteamGridDbPreview(MatchCandidate candidate)
    {
        return new MatchPreview
        {
            Source = MatchSource.SteamGridDb,
            ExternalId = candidate.ExternalId,
            Name = candidate.Name,
            Description = candidate.Description,
            CoverImageUrl = candidate.CoverImageUrl,
            BackgroundImageUrl = candidate.BackgroundImageUrl,
            Genres = candidate.Genres,
            CommunityRating = candidate.CommunityRating,
            ReleaseDateUtc = candidate.ReleaseDateUtc,
        };
    }

    /// <summary>
    /// Resolves full field data for an IGDB candidate without applying anything. IGDB candidates
    /// already carry everything needed from the search step, so this just re-wraps it.
    /// </summary>
    public static MatchPreview ResolveIgdbPreview(MatchCandidate candidate)
    {
        return new MatchPreview
        {
            Source = MatchSource.Igdb,
            ExternalId = candidate.ExternalId,
            Name = candidate.Name,
            Description = candidate.Description,
            CoverImageUrl = candidate.CoverImageUrl,
            BackgroundImageUrl = candidate.BackgroundImageUrl,
            Genres = candidate.Genres,
            CommunityRating = candidate.CommunityRating,
            ReleaseDateUtc = candidate.ReleaseDateUtc,
        };
    }

    /// <summary>
    /// Applies a resolved match preview to a library game field-by-field, per the user's picks in
    /// the old-vs-new comparison screen. Fields left "old" (false) are untouched.
    /// </summary>
    public async Task ApplyMatchPreviewAsync(
        int gameId,
        MatchPreview preview,
        bool useNewName,
        bool useNewCover,
        bool useNewBackground,
        bool useNewDescription,
        bool useNewGenres,
        bool useNewCommunityRating,
        bool useNewReleaseDate,
        CancellationToken cancellationToken = default)
    {
        _log.Log($"Karşılaştırma uygula: gameId={gameId} Source={preview.Source} başlatıldı.");

        var game = await _dbContext.Games.FindAsync([gameId], cancellationToken);
        if (game is null)
        {
            _log.Log($"Karşılaştırma uygula: gameId={gameId} bulunamadı.");
            return;
        }

        switch (preview.Source)
        {
            case MatchSource.SteamGridDb:
                game.SteamGridDbId = preview.ExternalId;
                break;
            case MatchSource.Steam:
                game.SteamAppId = preview.ExternalId;
                ApplyExtraDetails(game, preview.Extra);
                break;
            default:
                game.IgdbId = preview.ExternalId;
                break;
        }

        if (useNewName)
        {
            game.DisplayName = preview.Name;
        }

        if (useNewDescription)
        {
            game.Description = preview.Description;
            game.DescriptionRich = preview.Extra?.RichDescription;
        }

        if (useNewGenres)
        {
            game.Genres = preview.Genres;
        }

        if (useNewCommunityRating)
        {
            game.CommunityRating = preview.CommunityRating;
        }

        if (useNewReleaseDate)
        {
            game.ReleaseDateUtc = preview.ReleaseDateUtc;
        }

        if (useNewCover && !string.IsNullOrWhiteSpace(preview.CoverImageUrl))
        {
            _log.Log($"Karşılaştırma uygula: kapak resmi indiriliyor ({preview.CoverImageUrl}).");
            game.CoverImagePath = await _imageCacheService.DownloadAndCacheAsync(
                game.Id, preview.CoverImageUrl, cancellationToken);
            game.CoverImageUrl = preview.CoverImageUrl;
        }

        if (useNewBackground && !string.IsNullOrWhiteSpace(preview.BackgroundImageUrl))
        {
            _log.Log($"Karşılaştırma uygula: arka plan görseli indiriliyor ({preview.BackgroundImageUrl}).");
            game.BackgroundImagePath = await _imageCacheService.DownloadAndCacheAsync(
                game.Id, preview.BackgroundImageUrl, cancellationToken, fileNameSuffix: "background");
            game.BackgroundImageUrl = preview.BackgroundImageUrl;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        if (preview.Source == MatchSource.Steam && preview.Extra is not null)
        {
            await _steamMediaCache.SyncAsync(game.Id, preview.Extra, cancellationToken);
        }

        _log.Log($"Karşılaştırma uygula: '{game.DisplayName}' (gameId={gameId}) başarıyla güncellendi.");
    }

    private static void ApplyExtraDetails(LibraryGame game, GameExtraDetails? extra)
    {
        if (extra is null)
        {
            return;
        }

        game.Developers = extra.Developers;
        game.Publishers = extra.Publishers;
        game.Modes = extra.Modes;
        game.Languages = extra.Languages;
        game.AgeRating = extra.AgeRating;
        game.Website = extra.Website;
        game.ScreenshotUrls = extra.ScreenshotUrls.Count > 0 ? string.Join("\n", extra.ScreenshotUrls) : null;
        game.TrailerUrl = extra.TrailerUrl;
        game.TrailerThumbnailUrl = extra.TrailerThumbnailUrl;
    }

    /// <summary>
    /// Searches the Steam store for candidate matches for a library game's current display name,
    /// without applying anything. Needs no API key. Candidates carry the store description, genres,
    /// release date and extra metadata; the official cover/hero artwork is resolved later, only for
    /// the candidate that gets chosen (see <see cref="ResolveSteamPreviewAsync"/>).
    /// </summary>
    public async Task<IReadOnlyList<MatchCandidate>> SearchSteamCandidatesAsync(
        int gameId, CancellationToken cancellationToken = default)
    {
        _log.Log($"Steam arama: gameId={gameId} başlatıldı.");

        var game = await _dbContext.Games.FindAsync([gameId], cancellationToken);
        if (game is null)
        {
            _log.Log($"Steam arama: gameId={gameId} bulunamadı.");
            return [];
        }

        var searchTerm = SearchQueryBuilder.BuildSearchTerm(game.DisplayName);
        _log.Log($"Steam arama: '{game.DisplayName}' -> arama terimi '{searchTerm}' ile Steam mağazasında arama yapılıyor.");
        var results = await _steamStoreClient.SearchAsync(searchTerm, cancellationToken);

        var candidates = new List<MatchCandidate>();
        foreach (var result in results.Take(5))
        {
            var details = await _steamStoreClient.GetDetailsAsync(result.AppId, cancellationToken);
            if (details is null)
            {
                continue;
            }

            candidates.Add(new MatchCandidate
            {
                Source = MatchSource.Steam,
                ExternalId = details.AppId,
                Name = details.Name,
                Description = details.FullDescription ?? details.ShortDescription,
                CoverImageUrl = details.HeaderImageUrl,
                Genres = details.Genres,
                CommunityRating = details.MetacriticScore,
                ReleaseDateUtc = details.ReleaseDateUtc,
                Extra = details.Extra,
            });
        }

        _log.Log($"Steam arama: {candidates.Count} aday bulundu.");
        return candidates;
    }

    /// <summary>
    /// Turns a Steam candidate into a preview with Steam's official portrait cover and hero artwork
    /// resolved (falling back to the store header image when no library artwork exists).
    /// </summary>
    public async Task<MatchPreview> ResolveSteamPreviewAsync(MatchCandidate candidate, CancellationToken cancellationToken = default)
    {
        var cover = await _steamStoreClient.ResolveCoverUrlAsync(candidate.ExternalId, cancellationToken);
        var hero = await _steamStoreClient.ResolveHeroUrlAsync(candidate.ExternalId, cancellationToken);

        return new MatchPreview
        {
            Source = MatchSource.Steam,
            ExternalId = candidate.ExternalId,
            Name = candidate.Name,
            Description = candidate.Description,
            CoverImageUrl = cover ?? candidate.CoverImageUrl,
            BackgroundImageUrl = hero ?? candidate.Extra?.ScreenshotUrls.FirstOrDefault(),
            Genres = candidate.Genres,
            CommunityRating = candidate.CommunityRating,
            ReleaseDateUtc = candidate.ReleaseDateUtc,
            Extra = candidate.Extra,
        };
    }

    /// <summary>
    /// Re-reads the Steam store data of a game that is already linked to a Steam app (no search, no
    /// picker): description, genres, release date, rating and the extra metadata are refreshed;
    /// the name and artwork are left alone. Returns false if the game has no Steam link or Steam
    /// did not answer.
    /// </summary>
    public async Task<bool> RefreshSteamDetailsAsync(int gameId, CancellationToken cancellationToken = default)
    {
        var game = await _dbContext.Games.FindAsync([gameId], cancellationToken);
        if (game?.SteamAppId is not { } appId)
        {
            return false;
        }

        var details = await _steamStoreClient.GetDetailsAsync(appId, cancellationToken);
        if (details is null)
        {
            return false;
        }

        game.Description = details.FullDescription ?? details.ShortDescription ?? game.Description;
        game.DescriptionRich = details.Extra.RichDescription ?? game.DescriptionRich;
        game.Genres = details.Genres ?? game.Genres;
        game.CommunityRating = details.MetacriticScore ?? game.CommunityRating;
        game.ReleaseDateUtc = details.ReleaseDateUtc ?? game.ReleaseDateUtc;
        ApplyExtraDetails(game, details.Extra);

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _steamMediaCache.SyncAsync(game.Id, details.Extra, cancellationToken);
        _log.Log($"Steam yenileme: '{game.DisplayName}' (gameId={gameId}) Steam verileriyle güncellendi.");
        return true;
    }

    /// <summary>
    /// Matches a library game against Steam automatically (bulk flow): takes the best-scoring
    /// candidate, but only when its name is close enough to the game's name to be trusted, then
    /// applies every field. Returns false when nothing trustworthy was found.
    /// </summary>
    public async Task<bool> MatchWithSteamAsync(int gameId, CancellationToken cancellationToken = default)
    {
        var game = await _dbContext.Games.FindAsync([gameId], cancellationToken);
        if (game is null)
        {
            return false;
        }

        var candidates = await SearchSteamCandidatesAsync(gameId, cancellationToken);
        var best = candidates
            .Select(c => (Candidate: c, Score: NameSimilarity.Score(SearchQueryBuilder.BuildSearchTerm(game.DisplayName), c.Name)))
            .OrderByDescending(x => x.Score)
            .FirstOrDefault();

        if (best.Candidate is null || best.Score < 0.55)
        {
            _log.Log($"Steam eşleştirme: '{game.DisplayName}' için yeterince benzer bir sonuç yok (en iyi skor {best.Score:F2}).");
            return false;
        }

        var preview = await ResolveSteamPreviewAsync(best.Candidate, cancellationToken);
        await ApplyMatchPreviewAsync(gameId, preview, true, true, true, true, true, true, true, cancellationToken);
        return true;
    }

    /// <summary>
    /// Applies a user-picked IGDB candidate to a library game: fills in name/description/cover.
    /// </summary>
    public async Task ApplyIgdbMatchAsync(int gameId, MatchCandidate candidate, CancellationToken cancellationToken = default)
    {
        _log.Log($"IGDB uygula: gameId={gameId} IgdbId={candidate.ExternalId} başlatıldı.");

        var game = await _dbContext.Games.FindAsync([gameId], cancellationToken);
        if (game is null)
        {
            _log.Log($"IGDB uygula: gameId={gameId} bulunamadı.");
            return;
        }

        game.IgdbId = candidate.ExternalId;
        game.DisplayName = candidate.Name;
        game.Description = candidate.Description;
        game.DescriptionRich = null;
        game.Genres = candidate.Genres;
        game.CommunityRating = candidate.CommunityRating;
        game.ReleaseDateUtc = candidate.ReleaseDateUtc;

        if (!string.IsNullOrWhiteSpace(candidate.CoverImageUrl))
        {
            _log.Log($"IGDB uygula: kapak resmi indiriliyor ({candidate.CoverImageUrl}).");
            game.CoverImagePath = await _imageCacheService.DownloadAndCacheAsync(
                game.Id, candidate.CoverImageUrl, cancellationToken);
            game.CoverImageUrl = candidate.CoverImageUrl;
        }

        if (!string.IsNullOrWhiteSpace(candidate.BackgroundImageUrl))
        {
            _log.Log($"IGDB uygula: arka plan görseli indiriliyor ({candidate.BackgroundImageUrl}).");
            game.BackgroundImagePath = await _imageCacheService.DownloadAndCacheAsync(
                game.Id, candidate.BackgroundImageUrl, cancellationToken, fileNameSuffix: "background");
            game.BackgroundImageUrl = candidate.BackgroundImageUrl;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _log.Log($"IGDB uygula: '{game.DisplayName}' (gameId={gameId}) başarıyla güncellendi.");
    }

    /// <summary>
    /// Looks up a library game on SteamGridDB by its current display name and, if found, updates
    /// its name and cover image using the top-scoring match. Used by the bulk "match all" flow,
    /// where prompting per game isn't practical.
    /// </summary>
    public async Task<bool> MatchWithSteamGridDbAsync(int gameId, CancellationToken cancellationToken = default)
    {
        var candidates = await SearchSteamGridDbCandidatesAsync(gameId, cancellationToken);
        var best = candidates.FirstOrDefault();
        if (best is null)
        {
            return false;
        }

        await ApplySteamGridDbMatchAsync(gameId, best, cancellationToken);
        return true;
    }

    /// <summary>
    /// Looks up a library game by its current display name, trying SteamGridDB first and falling
    /// back to IGDB when no SteamGridDB match is found. Used by the bulk "match all" flow, where
    /// prompting per game isn't practical.
    /// </summary>
    public async Task<bool> MatchGameAsync(int gameId, CancellationToken cancellationToken = default)
    {
        _log.Log($"Genel eşleştirme: gameId={gameId} başlatıldı (önce Steam, sonra SteamGridDB, sonra IGDB denenecek).");

        if (await MatchWithSteamAsync(gameId, cancellationToken))
        {
            return true;
        }

        if (await MatchWithSteamGridDbAsync(gameId, cancellationToken))
        {
            return true;
        }

        _log.Log($"Genel eşleştirme: SteamGridDB'de bulunamadı, IGDB'ye düşülüyor (gameId={gameId}).");
        return await MatchWithIgdbAsync(gameId, cancellationToken);
    }

    /// <summary>
    /// Looks up a library game on IGDB by its current display name and, if found, updates its
    /// display name, description and cover image using the top-scoring match. Used by the bulk
    /// "match all" flow, where prompting per game isn't practical.
    /// </summary>
    public async Task<bool> MatchWithIgdbAsync(int gameId, CancellationToken cancellationToken = default)
    {
        var candidates = await SearchIgdbCandidatesAsync(gameId, cancellationToken);
        var best = candidates.FirstOrDefault();
        if (best is null)
        {
            return false;
        }

        await ApplyIgdbMatchAsync(gameId, best, cancellationToken);
        return true;
    }

    /// <summary>
    /// Checks IGDB for changes to a library game's listing since the last check (IGDB's
    /// "updated_at" timestamp moving forward, e.g. because a new release/version was added).
    /// This is not a build/version comparison against the locally installed copy — IGDB doesn't
    /// track that — just a signal that IGDB's data about the game changed. Returns null if no
    /// IGDB credentials are configured or no match was found.
    /// </summary>
    public async Task<IgdbUpdateCheckResult?> CheckForUpdateAsync(int gameId, CancellationToken cancellationToken = default)
    {
        _log.Log($"Güncelleme kontrolü: gameId={gameId} başlatıldı.");

        var game = await _dbContext.Games.FindAsync([gameId], cancellationToken);
        if (game is null)
        {
            _log.Log($"Güncelleme kontrolü: gameId={gameId} bulunamadı.");
            return null;
        }

        var settings = _settingsService.Load();
        if (string.IsNullOrWhiteSpace(settings.IgdbClientId) || string.IsNullOrWhiteSpace(settings.IgdbClientSecret))
        {
            _log.Log("Güncelleme kontrolü: IGDB (Twitch) Client ID/Secret ayarlanmamış, işlem iptal.");
            return null;
        }

        var result = await _igdbClient.CheckForUpdateAsync(
            game.DisplayName, settings.IgdbClientId, settings.IgdbClientSecret, game.IgdbLastUpdatedAtUtc, cancellationToken);

        if (result is null)
        {
            _log.Log($"Güncelleme kontrolü: '{game.DisplayName}' için IGDB'de eşleşme bulunamadı.");
            return null;
        }

        _log.Log(result.HasChanged
            ? $"Güncelleme kontrolü: '{game.DisplayName}' için IGDB kaydı değişmiş! Yeni updated_at={result.UpdatedAtUtc:u}."
            : $"Güncelleme kontrolü: '{game.DisplayName}' için değişiklik yok (updated_at={result.UpdatedAtUtc:u}).");

        game.IgdbId = result.IgdbId;
        game.IgdbLastUpdatedAtUtc = result.UpdatedAtUtc;
        game.LastUpdateCheckUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return result;
    }

}
