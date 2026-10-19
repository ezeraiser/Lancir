using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lancir.App.Services;
using Lancir.Core.Models;
using Lancir.Core.Services;

namespace Lancir.App.ViewModels;

public enum LibrarySortMode
{
    Custom,
    Name,
    AddedDate,
    ReleaseDate,
}

public sealed record SortOption(string Label, LibrarySortMode Mode);

public sealed partial class LibraryViewModel : ViewModelBase
{
    private readonly LibraryService _libraryService;
    private readonly GameLaunchService _gameLaunchService;
    private readonly IMatchPickerService _matchPickerService;
    private readonly DebugLogService _log;
    private readonly SettingsService _settingsService;
    private readonly PlaytimeTracker _playtimeTracker;
    private bool _detailsDismissed;
    private readonly MatchAllJob _matchJob;
    private readonly MatchAllCoordinator _matchCoordinator;

    public LibraryViewModel(
        LibraryService libraryService,
        GameLaunchService gameLaunchService,
        IMatchPickerService matchPickerService,
        DebugLogService log,
        PlaytimeTracker playtimeTracker,
        SettingsService settingsService,
        MatchAllJob matchJob,
        MatchAllCoordinator matchCoordinator)
    {
        _matchJob = matchJob;
        _matchCoordinator = matchCoordinator;
        _matchJob.GameStarted += id => OnUi(() =>
        {
            if (Games.FirstOrDefault(g => g.Id == id) is { } game)
            {
                game.IsMatching = true;
            }
        });
        _matchJob.GameFinished += (id, matched) => OnUi(async () => await ApplyJobResultAsync(id, matched));
        _matchJob.Changed += () => OnUi(UpdateMatchJobText);
        _settingsService = settingsService;
        _isListView = settingsService.Load().IsLibraryListView;
        _selectedSortOption = SortOptions.FirstOrDefault(
                                  o => string.Equals(o.Mode.ToString(), settingsService.Load().LibrarySortMode,
                                      StringComparison.OrdinalIgnoreCase))
                              ?? SortOptions[0];
        _libraryService = libraryService;
        _gameLaunchService = gameLaunchService;
        _matchPickerService = matchPickerService;
        _log = log;

        Games.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasUnmatchedGames));

        // Covers, backgrounds and store pictures were replaced behind the view's back (restoring originals): show the new files.
        MediaEvents.Changed += () => System.Windows.Application.Current.Dispatcher.BeginInvoke(async () => await LoadAsync());

        // LibraryViewModel lives for the whole app lifetime (held by MainViewModel), so no unsubscribe is needed.
        playtimeTracker.PlaytimeChanged += (gameId, seconds) =>
            System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
            {
                var game = Games.FirstOrDefault(g => g.Id == gameId);
                if (game is not null)
                {
                    game.PlaytimeSeconds = seconds;
                }
            });

        playtimeTracker.RunningChanged += (gameId, running) =>
            System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
            {
                var game = Games.FirstOrDefault(g => g.Id == gameId);
                if (game is not null)
                {
                    game.IsRunning = running;
                }
            });

        playtimeTracker.SessionCompleted += info =>
            System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
            {
                var game = Games.FirstOrDefault(g => g.Id == info.GameId);
                if (game is not null)
                {
                    game.Game.PlayCount = info.PlayCount;
                    game.Game.LastPlayedUtc = info.EndedAtUtc;
                    game.RefreshPlayStats();
                }

                SessionCompleted?.Invoke(info.GameId);
            });

        // Games that are already running when Lancir starts.
        _playtimeTracker = playtimeTracker;
    }

    public ObservableCollection<LibraryGameViewModel> Games { get; } = [];

    /// <summary>The service whose context tracks the games shown here; user-data edits must save through it.</summary>
    public LibraryService Service => _libraryService;

    /// <summary>Raised (on the UI thread) after a play session of a game was saved.</summary>
    public event Action<int>? SessionCompleted;

    /// <summary>True when at least one game has no SteamGridDB/IGDB match; drives the "Eşleştir" button.</summary>
    public bool HasUnmatchedGames => Games.Any(g => !g.IsMatched);

    public IReadOnlyList<SortOption> SortOptions { get; } =
    [
        new(Loc.T("Özel sıralama"), LibrarySortMode.Custom),
        new("A-Z", LibrarySortMode.Name),
        new(Loc.T("Eklenme tarihi"), LibrarySortMode.AddedDate),
        new(Loc.T("Yayınlanma tarihi"), LibrarySortMode.ReleaseDate),
    ];

    [ObservableProperty]
    private SortOption _selectedSortOption;

    partial void OnSelectedSortOptionChanged(SortOption value)
    {
        var settings = _settingsService.Load();
        settings.LibrarySortMode = value.Mode.ToString();
        _settingsService.Save(settings);
        ApplySort();
    }

    private List<LibraryGameViewModel> Sort(IEnumerable<LibraryGameViewModel> games) => SelectedSortOption.Mode switch
    {
        LibrarySortMode.Name => games.OrderBy(g => g.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList(),
        LibrarySortMode.AddedDate => games.OrderByDescending(g => g.Game.AddedAtUtc).ToList(),
        // Games without a release date go last.
        LibrarySortMode.ReleaseDate => games
            .OrderBy(g => g.ReleaseDateUtc is null)
            .ThenByDescending(g => g.ReleaseDateUtc)
            .ToList(),
        _ => games.OrderBy(g => g.Game.SortOrder).ThenByDescending(g => g.Game.AddedAtUtc).ToList(),
    };

    /// <summary>Re-sorts the displayed games in place, so item instances (and selection) are kept.</summary>
    private void ApplySort()
    {
        var sorted = Sort(Games);
        for (var i = 0; i < sorted.Count; i++)
        {
            var current = Games.IndexOf(sorted[i]);
            if (current != i)
            {
                Games.Move(current, i);
            }
        }
    }

    /// <summary>
    /// Handles a drag-and-drop reorder: moves <paramref name="dragged"/> to <paramref name="target"/>'s
    /// position, saves the result as the custom order and switches the sort mode to custom.
    /// </summary>
    public async Task MoveGameAsync(LibraryGameViewModel dragged, LibraryGameViewModel target)
    {
        var from = Games.IndexOf(dragged);
        var to = Games.IndexOf(target);
        if (from < 0 || to < 0 || from == to)
        {
            return;
        }

        Games.Move(from, to);

        try
        {
            await _libraryService.SaveCustomOrderAsync(Games.Select(g => g.Id).ToList());
            SelectedSortOption = SortOptions.First(o => o.Mode == LibrarySortMode.Custom);
        }
        catch (Exception ex)
        {
            ErrorMessage = Loc.F("Sıralama kaydedilemedi: {0}", ex.Message);
        }
    }

    /// <summary>Selects a game and shows its details pane.</summary>
    public void SelectGame(LibraryGameViewModel? game)
    {
        foreach (var g in Games)
        {
            g.IsSelected = ReferenceEquals(g, game);
        }

        SelectedGame = game;
        if (game is not null)
        {
            _detailsDismissed = false;

            var settings = _settingsService.Load();
            if (settings.LastSelectedGameId != game.Id)
            {
                settings.LastSelectedGameId = game.Id;
                _settingsService.Save(settings);
            }
        }
    }

    /// <summary>Closes the details pane; it stays closed until the user selects a game again.</summary>
    public void CloseDetails()
    {
        SelectGame(null);
        _detailsDismissed = true;
    }

    private void ReplaceGame(LibraryGameViewModel old, LibraryGame updated)
    {
        var index = Games.IndexOf(old);
        if (index < 0)
        {
            return;
        }

        var replacement = new LibraryGameViewModel(updated);
        Games[index] = replacement;
        if (old.IsSelected)
        {
            SelectGame(replacement);
        }

        ApplySort();
    }

    /// <summary>True for the list layout, false for the cover grid. Remembered across runs.</summary>
    [ObservableProperty]
    private bool _isListView;

    partial void OnIsListViewChanged(bool value)
    {
        var settings = _settingsService.Load();
        settings.IsLibraryListView = value;
        _settingsService.Save(settings);
    }

    /// <summary>Raised when the user asks to restore a view's default layout (true = list view).</summary>
    public event Action<bool>? LayoutResetRequested;

    [RelayCommand]
    private void ResetLayout(string? isList) =>
        LayoutResetRequested?.Invoke(bool.TryParse(isList, out var v) && v);

    [RelayCommand]
    private void SetListView(string? isList) => IsListView = bool.TryParse(isList, out var v) && v;

    /// <summary>Switches between the tile view and the list view (the nav strip's view button).</summary>
    [RelayCommand]
    private void ToggleListView() => IsListView = !IsListView;

    [RelayCommand]
    private void SelectSort(SortOption? option)
    {
        if (option is not null)
        {
            SelectedSortOption = option;
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedGame))]
    private LibraryGameViewModel? _selectedGame;

    /// <summary>True when a game is selected and its details panel should be shown.</summary>
    public bool HasSelectedGame => SelectedGame is not null;

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var previouslySelectedId = SelectedGame?.Id ?? _settingsService.Load().LastSelectedGameId;
            var games = await _libraryService.GetLibraryAsync();
            Games.Clear();
            foreach (var game in Sort(games.Select(g => new LibraryGameViewModel(g))))
            {
                Games.Add(game);
            }

            // Keep the previous selection; with none (and the user hasn't closed the pane), show the first game.
            var toSelect = Games.FirstOrDefault(g => g.Id == previouslySelectedId)
                           ?? (_detailsDismissed ? null : Games.FirstOrDefault());
            foreach (var g in Games)
            {
                g.IsRunning = _playtimeTracker.IsRunning(g.Id);
            }

            EnsureScopes();
            GamesView.Refresh();
            UpdateHasVisibleGames();
            SelectGame(toSelect is not null && !Shown(toSelect) ? Games.FirstOrDefault(Shown) : toSelect);
            _ = RunStartupSteamSyncAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = Loc.F("Kütüphane yüklenemedi: {0}", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void LaunchGame(LibraryGameViewModel? gameViewModel)
    {
        if (gameViewModel is null)
        {
            return;
        }

        try
        {
            _gameLaunchService.Launch(gameViewModel.Game);
        }
        catch (Exception ex)
        {
            ErrorMessage = Loc.F("Oyun başlatılamadı: {0}", ex.Message);
        }
    }

    private bool _startupSyncStarted;

    /// <summary>Once per run, when enabled in Settings: refreshes the Steam playtime of games already in the library.</summary>
    private async Task RunStartupSteamSyncAsync()
    {
        if (_startupSyncStarted)
        {
            return;
        }

        _startupSyncStarted = true;
        try
        {
            if (!_settingsService.Load().SyncSteamPlaytimeOnStart || !_libraryService.IsSteamAccountConfigured)
            {
                return;
            }

            if (await _libraryService.SyncSteamPlaytimeAsync() is > 0)
            {
                foreach (var game in Games)
                {
                    game.PlaytimeSeconds = game.Game.PlaytimeSeconds;
                    game.RefreshPlayStats();
                }
            }
        }
        catch (Exception ex)
        {
            _log.Log($"Steam süre senkronu: HATA - {ex.Message}");
        }
    }

    /// <summary>Opens the Steam import window: lists the account's games and imports the chosen ones with their playtime.</summary>
    [RelayCommand]
    private async Task ImportFromSteamAsync()
    {
        var importer = new SteamImportViewModel(_libraryService, _log);
        _matchPickerService.ShowSteamImport(importer);

        if (importer.Imported)
        {
            await LoadAsync();
        }
    }

    /// <summary>Adds a game by hand: name, optional exe, description and genres.</summary>
    [RelayCommand]
    private async Task AddGameAsync() => await AddManualAsync(viaSteam: false);

    /// <summary>
    /// Adds a Steam game: the user types the name, picks the game from Steam's search results, and the game
    /// is then set to start through the Steam protocol (an exe is optional but gives playtime tracking).
    /// </summary>
    [RelayCommand]
    private async Task AddSteamGameAsync() => await AddManualAsync(viaSteam: true);

    private async Task AddManualAsync(bool viaSteam)
    {
        var entered = _matchPickerService.PromptNewGame();
        if (entered is null)
        {
            return;
        }

        try
        {
            var game = await _libraryService.AddManualGameAsync(
                entered.DisplayName, entered.ExecutablePath, entered.Description, entered.Genres, entered.SteamAppId,
                entered.LaunchSettings);

            await LoadAsync();
            var added = Games.FirstOrDefault(g => g.Id == game.Id);
            if (added is null)
            {
                return;
            }

            SelectGame(added);

            if (viaSteam && entered.SteamAppId is not null)
            {
                // The id was typed in: no search needed, just start via Steam and pull the store data.
                added.LaunchViaSteam = true;
                await _libraryService.SaveAsync();
                await RunMatchAsync(added, "Steam", _libraryService.RefreshSteamDetailsAsync);
            }
            else if (viaSteam)
            {
                await RunPickerMatchAsync(
                    added,
                    "Steam",
                    _libraryService.SearchSteamCandidatesAsync,
                    _libraryService.ResolveSteamPreviewAsync);

                var matched = Games.FirstOrDefault(g => g.Id == game.Id);
                if (matched is { SteamAppId: not null })
                {
                    matched.LaunchViaSteam = true;
                    await _libraryService.SaveAsync();
                }
            }
        }
        catch (Exception ex)
        {
            _log.Log($"Oyun ekle: HATA - {ex}");
            ErrorMessage = Loc.F("Oyun eklenemedi: {0}", ex.Message);
        }
    }

    [RelayCommand]
    private async Task RemoveGameAsync(LibraryGameViewModel? gameViewModel)
    {
        if (gameViewModel is null)
        {
            _log.Log("Kaldır: komut çağrıldı ama CommandParameter null geldi (binding sorunu olabilir).");
            return;
        }

        try
        {
            await _libraryService.RemoveGameAsync(gameViewModel.Id);
            Games.Remove(gameViewModel);
        }
        catch (Exception ex)
        {
            _log.Log($"Kaldır: HATA - {ex}");
            ErrorMessage = Loc.F("Oyun kaldırılamadı: {0}", ex.Message);
        }
    }

    /// <summary>
    /// Opens the details editor for a game (name, description, genres, executable path). The
    /// only place these fields can be edited — the library grid itself is read-only.
    /// </summary>
    [RelayCommand]
    private async Task OpenDetailsAsync(LibraryGameViewModel? gameViewModel)
    {
        if (gameViewModel is null)
        {
            return;
        }

        var edited = await _matchPickerService.EditDetailsAsync(gameViewModel.Game);
        if (!edited)
        {
            return;
        }

        var updatedGames = await _libraryService.GetLibraryAsync();
        var updated = updatedGames.FirstOrDefault(g => g.Id == gameViewModel.Id);
        if (updated is not null)
        {
            ReplaceGame(gameViewModel, updated);
        }
    }

    /// <summary>
    /// Looks the game up using its current display name and fills in name/description/cover.
    /// Tries SteamGridDB first, then falls back to IGDB.
    /// </summary>
    [RelayCommand]
    private async Task MatchGameAsync(LibraryGameViewModel? gameViewModel)
    {
        await RunMatchAsync(gameViewModel, Loc.T("Steam, SteamGridDB veya IGDB"), _libraryService.MatchGameAsync);
    }

    /// <summary>Refreshes the store data of a game that is already linked to Steam (no picker).</summary>
    [RelayCommand]
    private async Task RefreshFromSteamAsync(LibraryGameViewModel? gameViewModel)
    {
        if (gameViewModel?.Game.SteamAppId is null)
        {
            ErrorMessage = Loc.T("Bu oyun henüz Steam ile eşleşmemiş. Önce \"Steam'den\" ile eşleştirin.");
            return;
        }

        await RunMatchAsync(gameViewModel, "Steam", _libraryService.RefreshSteamDetailsAsync);
    }

    /// <summary>
    /// Searches the Steam store (no API key needed) and lets the user pick the right candidate; the
    /// chosen game gets Steam's official artwork and store metadata.
    /// </summary>
    [RelayCommand]
    private async Task MatchWithSteamAsync(LibraryGameViewModel? gameViewModel)
    {
        await RunPickerMatchAsync(
            gameViewModel,
            "Steam",
            _libraryService.SearchSteamCandidatesAsync,
            _libraryService.ResolveSteamPreviewAsync);
    }

    /// <summary>
    /// Searches SteamGridDB for candidates and lets the user pick the right one before applying
    /// it — unlike the bulk flow, a single game is worth a picker since an ambiguous name can
    /// match the wrong game.
    /// </summary>
    [RelayCommand]
    private async Task MatchWithSteamGridDbAsync(LibraryGameViewModel? gameViewModel)
    {
        await RunPickerMatchAsync(
            gameViewModel,
            "SteamGridDB",
            _libraryService.SearchSteamGridDbCandidatesAsync,
            (candidate, _) => Task.FromResult(LibraryService.ResolveSteamGridDbPreview(candidate)));
    }

    /// <summary>Searches IGDB for candidates and lets the user pick the right one before applying it.</summary>
    [RelayCommand]
    private async Task MatchWithIgdbAsync(LibraryGameViewModel? gameViewModel)
    {
        await RunPickerMatchAsync(
            gameViewModel,
            "IGDB",
            _libraryService.SearchIgdbCandidatesAsync,
            (candidate, _) => Task.FromResult(LibraryService.ResolveIgdbPreview(candidate)));
    }

    /// <summary>
    /// Shared flow for a single-game match: search for candidates, let the user pick one, resolve
    /// its full field data, show the old-vs-new comparison, then apply only the fields the user
    /// chose to update.
    /// </summary>
    private async Task RunPickerMatchAsync(
        LibraryGameViewModel? gameViewModel,
        string sourceLabel,
        Func<int, CancellationToken, Task<IReadOnlyList<MatchCandidate>>> search,
        Func<MatchCandidate, CancellationToken, Task<MatchPreview>> resolvePreview)
    {
        if (gameViewModel is null)
        {
            _log.Log($"{sourceLabel} eşleştirme: komut çağrıldı ama CommandParameter null geldi (binding sorunu olabilir).");
            return;
        }

        gameViewModel.IsMatching = true;
        try
        {
            var candidates = await search(gameViewModel.Id, CancellationToken.None);
            if (candidates.Count == 0)
            {
                ErrorMessage = Loc.F("'{0}' için {1}'de eşleşme bulunamadı.", gameViewModel.DisplayName, sourceLabel);
                return;
            }

            // Newest release first, so the most likely (current) match is at the top.
            var sortedCandidates = candidates.OrderByDescending(c => c.ReleaseDateUtc).ToList();
            var picked = _matchPickerService.Pick(gameViewModel.DisplayName, sortedCandidates);
            if (picked is null)
            {
                _log.Log($"{sourceLabel} eşleştirme: kullanıcı seçim penceresini iptal etti.");
                return;
            }

            var preview = await resolvePreview(picked, CancellationToken.None);

            var choices = _matchPickerService.Compare(gameViewModel.Game, preview);
            if (choices is null)
            {
                _log.Log($"{sourceLabel} eşleştirme: kullanıcı karşılaştırma penceresini iptal etti.");
                return;
            }

            await _libraryService.ApplyMatchPreviewAsync(
                gameViewModel.Id,
                preview,
                choices.UseNewName,
                choices.UseNewCover,
                choices.UseNewBackground,
                choices.UseNewDescription,
                choices.UseNewGenres,
                choices.UseNewCommunityRating,
                choices.UseNewReleaseDate,
                CancellationToken.None);

            var updatedGames = await _libraryService.GetLibraryAsync();
            var updated = updatedGames.FirstOrDefault(g => g.Id == gameViewModel.Id);
            if (updated is not null)
            {
                ReplaceGame(gameViewModel, updated);
            }
        }
        catch (Exception ex)
        {
            _log.Log($"{sourceLabel} eşleştirme: HATA - {ex}");
            ErrorMessage = Loc.F("Eşleştirme başarısız: {0}", ex.Message);
        }
        finally
        {
            gameViewModel.IsMatching = false;
        }
    }

    private async Task RunMatchAsync(
        LibraryGameViewModel? gameViewModel, string sourceLabel, Func<int, CancellationToken, Task<bool>> match)
    {
        if (gameViewModel is null)
        {
            _log.Log($"{sourceLabel} eşleştirme: komut çağrıldı ama CommandParameter null geldi (binding sorunu olabilir).");
            return;
        }

        if (gameViewModel.IsMatching)
        {
            return; // already being matched (by the match-all job, say)
        }

        gameViewModel.IsMatching = true;
        try
        {
            var found = await match(gameViewModel.Id, CancellationToken.None);
            if (!found)
            {
                ErrorMessage = Loc.F("'{0}' için {1}'de eşleşme bulunamadı.", gameViewModel.DisplayName, sourceLabel);
                return;
            }

            var updatedGames = await _libraryService.GetLibraryAsync();
            var updated = updatedGames.FirstOrDefault(g => g.Id == gameViewModel.Id);
            if (updated is not null)
            {
                ReplaceGame(gameViewModel, updated);
            }
        }
        catch (Exception ex)
        {
            _log.Log($"{sourceLabel} eşleştirme: HATA - {ex}");
            ErrorMessage = Loc.F("Eşleştirme başarısız: {0}", ex.Message);
        }
        finally
        {
            gameViewModel.IsMatching = false;
        }
    }

    /// <summary>
    /// Runs the match (SteamGridDB, then IGDB fallback) for every game that doesn't already have one.
    /// </summary>
    [RelayCommand]
    private async Task MatchAllAsync() => await _matchCoordinator.StartAsync();

    private static void OnUi(Action action) => System.Windows.Application.Current?.Dispatcher.BeginInvoke(action);

    /// <summary>Shown in the library header while the match-all job runs ("12 / 40"); null otherwise.</summary>
    [ObservableProperty]
    private string? _matchJobText;

    [ObservableProperty]
    private bool _isMatchJobRunning;

    private void UpdateMatchJobText()
    {
        var s = _matchJob.Snapshot;
        IsMatchJobRunning = s.IsRunning;
        MatchJobText = s.IsRunning ? Loc.F("Eşleştiriliyor: {0} / {1}", s.Processed, s.Total) : null;
    }

    /// <summary>The job finished a game: its spinner stops and, when it was matched, the game is read again (the job wrote it through a context of its own).</summary>
    private async Task ApplyJobResultAsync(int gameId, bool matched)
    {
        if (Games.FirstOrDefault(g => g.Id == gameId) is not { } game)
        {
            return;
        }

        game.IsMatching = false;
        if (!matched)
        {
            return;
        }

        if (await _libraryService.ReloadGameAsync(gameId) is { } fresh)
        {
            ReplaceGame(game, fresh);
        }
    }
}
