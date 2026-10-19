using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lancir.App.Services;
using Lancir.App.Views;
using Lancir.Core.Models;
using Lancir.Core.Services;
using Wpf.Ui.Controls;

namespace Lancir.App.ViewModels;

/// <summary>The library list of the left strip (all games, favorites, one list per status, the user's own) and the filter chips of the page.</summary>
public sealed partial class LibraryViewModel
{
    private ICollectionView? _gamesView;
    private bool _scopesBuilt;

    /// <summary>Raised when the libraries of the strip are switched on or off in Settings.</summary>
    public static event Action? ScopeSettingsChanged;

    public static void NotifyScopeSettingsChanged() => ScopeSettingsChanged?.Invoke();

    public ObservableCollection<LibraryScopeItem> Scopes { get; } = [];

    /// <summary>The games the page shows: the chosen library, narrowed by the filter chips; the order is the one of <see cref="Games"/>.</summary>
    public ICollectionView GamesView
    {
        get
        {
            if (_gamesView is null)
            {
                _gamesView = CollectionViewSource.GetDefaultView(Games);
                _gamesView.Filter = o => o is LibraryGameViewModel game && Shown(game);
                Games.CollectionChanged += (_, e) =>
                {
                    if (e.NewItems is not null)
                    {
                        foreach (LibraryGameViewModel added in e.NewItems)
                        {
                            added.PropertyChanged += OnGameChangedForScope;
                        }
                    }

                    UpdateHasVisibleGames();
                };
                ScopeSettingsChanged += () => System.Windows.Application.Current.Dispatcher.BeginInvoke(RebuildScopes);
            }

            return _gamesView;
        }
    }

    [ObservableProperty]
    private LibraryScopeItem? _activeScope;

    [ObservableProperty]
    private bool _hasVisibleGames;

    public string ScopeTitle => ActiveScope?.Title ?? Loc.T("Kütüphane");

    /// <summary>True when the library has games but the chosen list (with the filters) shows none.</summary>
    public bool IsScopeEmpty => Games.Count > 0 && !HasVisibleGames;

    partial void OnActiveScopeChanged(LibraryScopeItem? value)
    {
        OnPropertyChanged(nameof(ScopeTitle));
        OnPropertyChanged(nameof(IsScopeEmpty));
    }

    partial void OnHasVisibleGamesChanged(bool value) => OnPropertyChanged(nameof(IsScopeEmpty));

    private void OnGameChangedForScope(object? sender, PropertyChangedEventArgs e)
    {
        // a game whose status or favorite mark changed leaves (or joins) a list at once
        if (e.PropertyName is nameof(LibraryGameViewModel.Status) or nameof(LibraryGameViewModel.IsFavorite))
        {
            GamesView.Refresh();
            UpdateHasVisibleGames();
        }
    }

    // ---- the filter chips (several at once: the statuses add up, favorites narrows them) ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilter))]
    [NotifyPropertyChangedFor(nameof(FilterSummary))]
    private bool _filterPlaying;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilter))]
    [NotifyPropertyChangedFor(nameof(FilterSummary))]
    private bool _filterFinished;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilter))]
    [NotifyPropertyChangedFor(nameof(FilterSummary))]
    private bool _filterDropped;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilter))]
    [NotifyPropertyChangedFor(nameof(FilterSummary))]
    private bool _filterBacklog;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilter))]
    [NotifyPropertyChangedFor(nameof(FilterSummary))]
    private bool _filterFavorites;

    public bool HasActiveFilter => FilterPlaying || FilterFinished || FilterDropped || FilterBacklog || FilterFavorites;

    /// <summary>The filter button's text: just "Filtre", or with how many filters are on.</summary>
    public string FilterSummary
    {
        get
        {
            var count = new[] { FilterPlaying, FilterFinished, FilterDropped, FilterBacklog, FilterFavorites }.Count(on => on);
            return count == 0 ? Lancir.App.Services.Loc.T("Filtre") : Lancir.App.Services.Loc.F("Filtre ({0})", count);
        }
    }

    partial void OnFilterPlayingChanged(bool value) => ApplyFilters();

    partial void OnFilterFinishedChanged(bool value) => ApplyFilters();

    partial void OnFilterDroppedChanged(bool value) => ApplyFilters();

    partial void OnFilterBacklogChanged(bool value) => ApplyFilters();

    partial void OnFilterFavoritesChanged(bool value) => ApplyFilters();

    [RelayCommand]
    private void ClearFilters() => FilterPlaying = FilterFinished = FilterDropped = FilterBacklog = FilterFavorites = false;

    private void ApplyFilters()
    {
        GamesView.Refresh();
        UpdateHasVisibleGames();
        if (SelectedGame is null || !Shown(SelectedGame))
        {
            SelectGame(Games.FirstOrDefault(Shown));
        }
    }

    /// <summary>
    /// The two groups of chips: the statuses add up (a game shows when it is in any chosen status; with none chosen, every status
    /// shows), and "favorites" narrows the result on top (only favorites, whatever the statuses).
    /// </summary>
    private bool PassesFilters(LibraryGameViewModel game)
    {
        if (FilterFavorites && !game.IsFavorite)
        {
            return false;
        }

        var anyStatus = FilterPlaying || FilterFinished || FilterDropped || FilterBacklog;
        return !anyStatus || game.Game.Status switch
        {
            GameStatus.Playing => FilterPlaying,
            GameStatus.Finished => FilterFinished,
            GameStatus.Dropped => FilterDropped,
            _ => FilterBacklog,
        };
    }

    private bool Shown(LibraryGameViewModel game) => InScope(game) && PassesFilters(game);

    private bool InScope(LibraryGameViewModel game) => ActiveScope switch
    {
        { Kind: LibraryScopeKind.Status } scope => game.Game.Status == scope.Status,
        { Kind: LibraryScopeKind.Favorites } => game.IsFavorite,
        { Kind: LibraryScopeKind.Custom, CustomId: { } id } => _settingsService.Load().CustomLibraries
            .FirstOrDefault(l => l.Id == id)?.GameIds.Contains(game.Id) == true,
        _ => true,
    };

    private void UpdateHasVisibleGames() => HasVisibleGames = Games.Any(Shown);

    // ---- the list of the strip ----

    private static LibraryScopeItem CustomItem(CustomLibrarySetting library) => new(
        LibraryScopeKind.Custom, library.Name,
        Enum.TryParse<SymbolRegular>(library.IconName, out var symbol) ? symbol : SymbolRegular.Folder24,
        null, library.Id, library.IconPath is { } path && File.Exists(path) ? path : null);

    /// <summary>Builds the strip's list from the settings (at start, and when libraries, switches or the order change).</summary>
    public void RebuildScopes()
    {
        var previous = ActiveScope;
        var settings = _settingsService.Load();

        var items = new List<LibraryScopeItem> { new(LibraryScopeKind.All, Loc.T("Kütüphane"), SymbolRegular.XboxController24) };
        if (settings.ShowFavoritesLibrary)
        {
            items.Add(new(LibraryScopeKind.Favorites, Loc.T("Favoriler"), SymbolRegular.Star24));
        }

        if (settings.ShowPlayingLibrary)
        {
            items.Add(new(LibraryScopeKind.Status, Loc.T("Oynuyorum"), SymbolRegular.Play24, GameStatus.Playing));
        }

        if (settings.ShowFinishedLibrary)
        {
            items.Add(new(LibraryScopeKind.Status, Loc.T("Bitirdim"), SymbolRegular.CheckmarkCircle24, GameStatus.Finished));
        }

        if (settings.ShowDroppedLibrary)
        {
            items.Add(new(LibraryScopeKind.Status, Loc.T("Bıraktım"), SymbolRegular.DismissCircle24, GameStatus.Dropped));
        }

        if (settings.ShowBacklogLibrary)
        {
            items.Add(new(LibraryScopeKind.Status, Loc.T("Listede"), SymbolRegular.Bookmark24, GameStatus.Backlog));
        }

        items.AddRange(settings.CustomLibraries.Select(CustomItem));

        // the order the user dragged them to; what is not in the saved order keeps its place after the rest
        var order = settings.LibraryOrder;
        items = items.Select((item, index) => (item, index, rank: order.IndexOf(item.Key) is var r && r >= 0 ? r : int.MaxValue))
            .OrderBy(x => x.rank).ThenBy(x => x.index).Select(x => x.item).ToList();

        Scopes.Clear();
        foreach (var item in items)
        {
            Scopes.Add(item);
        }

        // keep the chosen list when it still exists, otherwise go back to all games
        SelectScope(previous is null
            ? Scopes.First(s => s.Kind == LibraryScopeKind.All)
            : Scopes.FirstOrDefault(s => s.Matches(previous)) ?? Scopes.First(s => s.Kind == LibraryScopeKind.All));
        _scopesBuilt = true;
    }

    /// <summary>Shows the games of <paramref name="scope"/>; the details panel follows (a game outside the list is replaced by the first one inside).</summary>
    public void SelectScope(LibraryScopeItem scope)
    {
        _ = GamesView; // make sure the filter is in place
        foreach (var item in Scopes)
        {
            item.IsSelected = ReferenceEquals(item, scope);
        }

        ActiveScope = scope;
        GamesView.Refresh();
        UpdateHasVisibleGames();

        if (SelectedGame is null || !Shown(SelectedGame))
        {
            SelectGame(Games.FirstOrDefault(Shown));
        }
    }

    /// <summary>Makes sure the list exists (the first time the library is shown).</summary>
    public void EnsureScopes()
    {
        if (!_scopesBuilt)
        {
            RebuildScopes();
        }
    }

    /// <summary>Drag and drop in the strip: <paramref name="dragged"/> takes the place of <paramref name="target"/>; the order is remembered.</summary>
    public void MoveScope(LibraryScopeItem dragged, LibraryScopeItem target)
    {
        var from = Scopes.IndexOf(dragged);
        var to = Scopes.IndexOf(target);
        if (from < 0 || to < 0 || from == to)
        {
            return;
        }

        Scopes.Move(from, to);
        var settings = _settingsService.Load();
        var keys = Scopes.Select(s => s.Key).ToList();
        keys.AddRange(settings.LibraryOrder.Where(k => !keys.Contains(k))); // hidden libraries keep their place for later
        settings.LibraryOrder = keys;
        _settingsService.Save(settings);
    }

    // ---- libraries of the user's own ----

    [RelayCommand]
    private void CreateCustomLibrary()
    {
        var name = TextPromptWindow.Ask(Loc.T("Özel kütüphane"), Loc.T("Kütüphanenin adı:"));
        if (name is null)
        {
            return;
        }

        var settings = _settingsService.Load();
        var library = new CustomLibrarySetting { Name = name };
        settings.CustomLibraries.Add(library);
        _settingsService.Save(settings);
        RebuildScopes();
        var item = Scopes.First(s => s.CustomId == library.Id);
        SelectScope(item);
        ChangeCustomLibraryIcon(item); // the icon is asked right away (cancel keeps the folder)
    }

    public void RenameCustomLibrary(LibraryScopeItem item)
    {
        if (item.CustomId is not { } id)
        {
            return;
        }

        var name = TextPromptWindow.Ask(Loc.T("Özel kütüphane"), Loc.T("Kütüphanenin adı:"), item.Title);
        if (name is null)
        {
            return;
        }

        var settings = _settingsService.Load();
        if (settings.CustomLibraries.FirstOrDefault(l => l.Id == id) is { } library)
        {
            library.Name = name;
            _settingsService.Save(settings);
            item.Title = name;
            OnPropertyChanged(nameof(ScopeTitle));
        }
    }

    /// <summary>Asks for a new icon (a built-in symbol or a picture of the user's own) for a custom library.</summary>
    public void ChangeCustomLibraryIcon(LibraryScopeItem item)
    {
        if (item.CustomId is not { } id || IconPickerWindow.Pick() is not { } choice)
        {
            return;
        }

        var settings = _settingsService.Load();
        if (settings.CustomLibraries.FirstOrDefault(l => l.Id == id) is not { } library)
        {
            LibraryIcons.Delete(choice.PicturePath);
            return;
        }

        if (library.IconPath != choice.PicturePath)
        {
            LibraryIcons.Delete(library.IconPath); // the picture it had before is no longer used
        }

        library.IconName = choice.Symbol?.ToString();
        library.IconPath = choice.PicturePath;
        _settingsService.Save(settings);
        item.Symbol = choice.Symbol ?? SymbolRegular.Folder24;
        item.IconPath = choice.PicturePath;
    }

    /// <summary>Removes the library (the games stay in the main library).</summary>
    public void DeleteCustomLibrary(LibraryScopeItem item)
    {
        if (item.CustomId is not { } id)
        {
            return;
        }

        var answer = System.Windows.MessageBox.Show(
            System.Windows.Application.Current.MainWindow,
            Loc.F("\"{0}\" kütüphanesi silinsin mi? Oyunlar ana kütüphanede kalır.", item.Title),
            Loc.T("Özel kütüphane"), System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
        if (answer != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        var settings = _settingsService.Load();
        LibraryIcons.Delete(settings.CustomLibraries.FirstOrDefault(l => l.Id == id)?.IconPath);
        settings.CustomLibraries.RemoveAll(l => l.Id == id);
        _settingsService.Save(settings);
        RebuildScopes();
    }

    /// <summary>The custom libraries a game is in, for the game menu's check marks.</summary>
    public bool IsInLibrary(LibraryScopeItem library, LibraryGameViewModel game) =>
        library.CustomId is { } id
        && _settingsService.Load().CustomLibraries.FirstOrDefault(l => l.Id == id)?.GameIds.Contains(game.Id) == true;

    public void ToggleGameInLibrary(LibraryScopeItem library, LibraryGameViewModel game)
    {
        if (library.CustomId is not { } id)
        {
            return;
        }

        var settings = _settingsService.Load();
        if (settings.CustomLibraries.FirstOrDefault(l => l.Id == id) is not { } target)
        {
            return;
        }

        if (!target.GameIds.Remove(game.Id))
        {
            target.GameIds.Add(game.Id);
        }

        _settingsService.Save(settings);
        if (ActiveScope?.CustomId == id)
        {
            GamesView.Refresh();
            UpdateHasVisibleGames();
        }
    }
}
