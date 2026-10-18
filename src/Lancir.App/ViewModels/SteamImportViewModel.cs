using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lancir.App.Services;
using Lancir.Core.Services;
using Lancir.Core.Steam;

namespace Lancir.App.ViewModels;

/// <summary>One Steam game in the import list, with the checkbox that decides whether it is imported.</summary>
public sealed partial class SteamImportItemViewModel : ObservableObject
{
    public SteamImportItemViewModel(SteamImportRow row, bool isSelected)
    {
        Row = row;
        _isSelected = isSelected;
    }

    public SteamImportRow Row { get; }

    public string Name => Row.Game.Name;

    public long PlaytimeSeconds => Row.Game.PlaytimeSeconds;

    public string PlaytimeText => Row.Game.PlaytimeSeconds > 0 ? TimeText.Duration(Row.Game.PlaytimeSeconds) : "—";

    public string StateText => Row.State switch
    {
        SteamImportState.InLibrary => Loc.T("Kütüphanede (süre güncellenir)"),
        SteamImportState.MatchedByName => Loc.T("Kütüphanede, ada göre eşleşti"),
        _ => Loc.T("Yeni"),
    };

    public bool IsNew => Row.State == SteamImportState.New;

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>
/// The "import from Steam" window: lists the account's games (from the Steam Web API), lets the user choose which
/// to import, then adds the new ones and brings the Steam playtime of the others into the library.
/// </summary>
public sealed partial class SteamImportViewModel : ObservableObject
{
    private static readonly TimeSpan StoreDataDelay = TimeSpan.FromMilliseconds(1200);

    private readonly LibraryService _libraryService;
    private readonly DebugLogService _log;
    private readonly List<SteamImportItemViewModel> _all = [];

    public SteamImportViewModel(LibraryService libraryService, DebugLogService log)
    {
        _libraryService = libraryService;
        _log = log;
    }

    /// <summary>The rows shown (after the "only played" filter).</summary>
    public ObservableCollection<SteamImportItemViewModel> Items { get; } = [];

    /// <summary>True once something was imported, so the library should be reloaded.</summary>
    public bool Imported { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanImport))]
    private bool _isBusy;

    [ObservableProperty]
    private string? _message;

    [ObservableProperty]
    private bool _onlyPlayed = true;

    /// <summary>Also fetch each new game's description, genres and cover from the Steam store (one request per game).</summary>
    [ObservableProperty]
    private bool _fetchStoreData = true;

    public int SelectedCount => _all.Count(i => i.IsSelected);

    public string SummaryText => Loc.F("{0} oyun seçili", SelectedCount);

    public bool CanImport => !IsBusy && SelectedCount > 0;

    partial void OnOnlyPlayedChanged(bool value) => Rebuild();

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        Message = Loc.T("Steam kütüphanesi okunuyor...");
        try
        {
            var plan = await _libraryService.PlanSteamImportAsync();
            if (plan.NotConfigured)
            {
                Message = Loc.T("Önce Ayarlar'dan Steam profilinizi ve API anahtarınızı girin.");
                return;
            }

            if (plan.Status != SteamFetchStatus.Ok)
            {
                Message = SteamMessages.For(plan.Status);
                return;
            }

            foreach (var item in _all)
            {
                item.PropertyChanged -= OnItemPropertyChanged;
            }

            _all.Clear();
            foreach (var row in plan.Rows.OrderByDescending(r => r.Game.PlaytimeSeconds).ThenBy(r => r.Game.Name))
            {
                var item = new SteamImportItemViewModel(row, isSelected: row.Game.PlaytimeSeconds > 0);
                item.PropertyChanged += OnItemPropertyChanged;
                _all.Add(item);
            }

            Rebuild();
            Message = plan.Rows.Count == 0
                ? Loc.T("Steam hesabında oyun bulunamadı.")
                : Loc.F("{0} oyun bulundu.", plan.Rows.Count);
        }
        catch (Exception ex)
        {
            _log.Log($"Steam içe aktarma: HATA - {ex.Message}");
            Message = Loc.F("Steam kütüphanesi okunamadı: {0}", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var item in Items)
        {
            item.IsSelected = true;
        }
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var item in Items)
        {
            item.IsSelected = false;
        }
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        var rows = _all.Where(i => i.IsSelected).Select(i => i.Row).ToList();
        if (rows.Count == 0)
        {
            return;
        }

        IsBusy = true;
        try
        {
            Message = Loc.T("Oyunlar içe aktarılıyor...");
            var result = await _libraryService.ApplySteamImportAsync(rows);
            Imported = true;

            if (FetchStoreData && result.NewGameIds.Count > 0)
            {
                await FetchStoreDataAsync(result.NewGameIds);
            }

            Message = Loc.F("Tamamlandı: {0} oyun eklendi, {1} oyun güncellendi.", result.Added, result.Updated);

            // Imported rows now live in the library: show them as such (and nothing left to add by accident).
            foreach (var item in _all.Where(i => i.IsSelected).ToList())
            {
                item.IsSelected = false;
            }
        }
        catch (Exception ex)
        {
            _log.Log($"Steam içe aktarma: HATA - {ex}");
            Message = Loc.F("İçe aktarma başarısız: {0}", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Fills description, genres and artwork from the Steam store, one game at a time to stay polite to Steam.</summary>
    private async Task FetchStoreDataAsync(IReadOnlyList<int> gameIds)
    {
        for (var i = 0; i < gameIds.Count; i++)
        {
            Message = Loc.F("Mağaza verileri getiriliyor ({0}/{1})...", i + 1, gameIds.Count);
            try
            {
                await _libraryService.RefreshSteamDetailsAsync(gameIds[i]);
            }
            catch (Exception ex)
            {
                _log.Log($"Steam içe aktarma: mağaza verisi alınamadı (gameId={gameIds[i]}) - {ex.Message}");
            }

            if (i < gameIds.Count - 1)
            {
                await Task.Delay(StoreDataDelay);
            }
        }
    }

    private void Rebuild()
    {
        Items.Clear();
        foreach (var item in _all.Where(i => !OnlyPlayed || i.PlaytimeSeconds > 0 || i.IsSelected))
        {
            Items.Add(item);
        }

        NotifySelection();
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SteamImportItemViewModel.IsSelected))
        {
            NotifySelection();
        }
    }

    private void NotifySelection()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(CanImport));
    }
}
