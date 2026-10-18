using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lancir.App.Services;
using Lancir.Core.Services;
using Lancir.Core.Steam;

namespace Lancir.App.ViewModels;

/// <summary>One row of the achievements card.</summary>
public sealed record AchievementItem(
    string Name,
    string Description,
    string? IconUrl,
    bool IsAchieved,
    string DateText,
    string RarityText,
    bool IsRare,
    string Tooltip);

/// <summary>The "Başarımlar" card: Steam achievements of the selected game (cached locally, refreshed on demand).</summary>
public sealed partial class GamePanelViewModel
{
    /// <summary>How many rows the card shows until "show all" is clicked.</summary>
    private const int CollapsedAchievementCount = 8;

    /// <summary>A global unlock rate below this (percent) marks an achievement as rare.</summary>
    private const double RareThreshold = 10;

    private IReadOnlyList<SteamAchievement> _achievements = [];

    /// <summary>The rows currently shown (filtered, and cut to a few unless expanded).</summary>
    public ObservableCollection<AchievementItem> VisibleAchievements { get; } = [];

    [ObservableProperty]
    private string _achievementsSummary = "";

    /// <summary>Share of unlocked achievements, 0-100 (drives the progress bar).</summary>
    [ObservableProperty]
    private double _achievementsPercent;

    /// <summary>Explains why there is nothing to show (no key, private profile, no achievements...); null when the list is shown.</summary>
    [ObservableProperty]
    private string? _achievementsMessage;

    [ObservableProperty]
    private string? _achievementsUpdatedText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRefreshAchievements))]
    private bool _isAchievementsBusy;

    [ObservableProperty]
    private bool _hasAchievements;

    [ObservableProperty]
    private bool _showOnlyLockedAchievements;

    [ObservableProperty]
    private bool _showAllAchievements;

    /// <summary>The "show all (N)" / "show less" toggle is only needed when the list is longer than the collapsed size.</summary>
    [ObservableProperty]
    private bool _canExpandAchievements;

    [ObservableProperty]
    private string _expandAchievementsText = "";

    public bool CanRefreshAchievements => !IsAchievementsBusy;

    partial void OnShowOnlyLockedAchievementsChanged(bool value) => RebuildVisibleAchievements();

    partial void OnShowAllAchievementsChanged(bool value) => RebuildVisibleAchievements();

    [RelayCommand]
    private Task RefreshAchievementsAsync() => LoadAchievementsAsync(refresh: true);

    [RelayCommand]
    private void ToggleAllAchievements() => ShowAllAchievements = !ShowAllAchievements;

    /// <summary>Loads the card's data: the local cache first (no network), Steam when there is none or <paramref name="refresh"/>.</summary>
    private async Task LoadAchievementsAsync(bool refresh)
    {
        var game = _game;
        if (game is null)
        {
            ApplyAchievements(AchievementsView.Empty(AchievementsStatus.NoSteamId));
            return;
        }

        IsAchievementsBusy = true;
        try
        {
            var view = await Library.Service.GetAchievementsAsync(game.SteamAppId, game.DisplayName, refresh);
            if (_game?.Id != game.Id)
            {
                return; // the user switched games meanwhile
            }

            ApplyAchievements(view);
        }
        catch (Exception ex)
        {
            if (_game?.Id == game.Id)
            {
                _achievements = [];
                AchievementsMessage = Loc.F("Başarımlar alınamadı: {0}", ex.Message);
                RebuildVisibleAchievements();
            }
        }
        finally
        {
            IsAchievementsBusy = false;
        }
    }

    private void ApplyAchievements(AchievementsView view)
    {
        _achievements = view.Items;
        HasAchievements = view.Items.Count > 0;

        var unlocked = view.Items.Count(a => a.Achieved);
        AchievementsSummary = view.Items.Count > 0
            ? Loc.F("{0} / {1} açıldı", unlocked, view.Items.Count)
            : "";
        AchievementsPercent = view.Items.Count > 0 ? 100.0 * unlocked / view.Items.Count : 0;

        // Shown above the list when Steam could not be asked but an older copy exists.
        var staleNote = view.FromCache && view.Status is not (AchievementsStatus.Ok or AchievementsStatus.NoAchievements)
            ? AchievementsMessageFor(view.Status)
            : null;
        AchievementsMessage = view.Status is AchievementsStatus.Ok ? staleNote : AchievementsMessageFor(view.Status);
        AchievementsUpdatedText = view.FetchedUtc is { } fetched
            ? Loc.F("Güncelleme: {0}", TimeText.Relative(fetched))
            : null;

        RebuildVisibleAchievements();
    }

    private void RebuildVisibleAchievements()
    {
        IEnumerable<SteamAchievement> source = _achievements;
        if (ShowOnlyLockedAchievements)
        {
            source = source.Where(a => !a.Achieved);
        }

        var all = source.ToList();
        CanExpandAchievements = all.Count > CollapsedAchievementCount;
        ExpandAchievementsText = ShowAllAchievements
            ? Loc.T("Daha az göster")
            : Loc.F("Tümünü göster ({0})", all.Count);

        VisibleAchievements.Clear();
        foreach (var achievement in ShowAllAchievements ? all : all.Take(CollapsedAchievementCount))
        {
            VisibleAchievements.Add(ToItem(achievement));
        }
    }

    private static AchievementItem ToItem(SteamAchievement a)
    {
        // Steam gives no text for a hidden achievement until it is unlocked.
        var hiddenLocked = a.Hidden && !a.Achieved && string.IsNullOrWhiteSpace(a.Description);
        var description = hiddenLocked ? Loc.T("Gizli başarım") : a.Description;
        var rarity = a.GlobalPercent is { } p ? Loc.F("%{0}", p.ToString("0.#", AppLanguage.Culture)) : "";
        var date = a.UnlockedUtc is { } unlocked ? TimeText.Date(unlocked) : "";
        var tooltip = a.GlobalPercent is { } percent
            ? Loc.F("Oyuncuların %{0}'i bu başarımı açtı", percent.ToString("0.#", AppLanguage.Culture))
            : a.Name;

        return new AchievementItem(
            a.Name,
            description,
            a.Achieved ? a.IconUrl : a.IconGrayUrl ?? a.IconUrl,
            a.Achieved,
            date,
            rarity,
            a.GlobalPercent is < RareThreshold,
            tooltip);
    }

    private static string? AchievementsMessageFor(AchievementsStatus status) => status switch
    {
        AchievementsStatus.NotConfigured => Loc.T("Başarımlar için Ayarlar'dan Steam profilinizi ve API anahtarınızı girin."),
        AchievementsStatus.NoSteamId => Loc.T("Bu oyun Steam ile eşleşmemiş. Düzenle penceresinden Steam kimliğini girin veya \"Steam'den\" ile eşleştirin."),
        AchievementsStatus.NoAchievements => Loc.T("Bu oyunda Steam başarımı yok."),
        AchievementsStatus.Private => SteamMessages.For(SteamFetchStatus.Private),
        AchievementsStatus.InvalidKey => SteamMessages.For(SteamFetchStatus.InvalidKey),
        AchievementsStatus.ProfileNotFound => SteamMessages.For(SteamFetchStatus.ProfileNotFound),
        AchievementsStatus.Failed => SteamMessages.For(SteamFetchStatus.Failed),
        _ => null,
    };
}
