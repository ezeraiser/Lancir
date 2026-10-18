using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lancir.Core.Services;

namespace Lancir.App.ViewModels;

public enum AppTab
{
    Library,
    ScanReview,
    Settings,
    DebugConsole,
}

public sealed partial class MainViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;

    public MainViewModel(
        LibraryViewModel libraryViewModel,
        ScanReviewViewModel scanReviewViewModel,
        SettingsViewModel settingsViewModel,
        DebugConsoleViewModel debugConsoleViewModel,
        SettingsService settingsService,
        PlayHistoryService playHistoryService)
    {
        _settingsService = settingsService;
        _isNavCollapsed = settingsService.Load().IsNavCollapsed;

        LibraryViewModel = libraryViewModel;
        GamePanelViewModel = new GamePanelViewModel(libraryViewModel, settingsService, playHistoryService);
        ScanReviewViewModel = scanReviewViewModel;
        SettingsViewModel = settingsViewModel;
        DebugConsoleViewModel = debugConsoleViewModel;

        LibraryViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LibraryViewModel.IsListView))
            {
                ApplyLayout();
            }
        };
        LibraryViewModel.LayoutResetRequested += ResetLayout;
        ApplyLayout();

        ScanReviewViewModel.ReviewCompleted += async (_, _) =>
        {
            SelectedTab = AppTab.Library;
            await LibraryViewModel.LoadCommand.ExecuteAsync(null);
        };
    }

    public LibraryViewModel LibraryViewModel { get; }

    public GamePanelViewModel GamePanelViewModel { get; }

    public ScanReviewViewModel ScanReviewViewModel { get; }

    public SettingsViewModel SettingsViewModel { get; }

    public DebugConsoleViewModel DebugConsoleViewModel { get; }

    [ObservableProperty]
    private AppTab _selectedTab = AppTab.Library;

    [ObservableProperty]
    private bool _isNavCollapsed;

    /// <summary>
    /// Width of the game details side panel, user-resizable via a GridSplitter. Kept here (not
    /// in LibraryViewModel) so it persists across tab switches for the lifetime of the window.
    /// </summary>
    [ObservableProperty]
    private GridLength _detailsPaneWidth = new(400);

    private const double DefaultGridFraction = 0.30;
    private const double DefaultListFraction = 0.60;
    private const double MinDetailsWidth = 320;
    private const double MinLibraryWidth = 360;
    private const double SplitterWidth = 6;

    /// <summary>Width shared by the library column, splitter and details pane (window minus nav pane), set by the window.</summary>
    private double _availableWidth;

    public void UpdateAvailableWidth(double width)
    {
        _availableWidth = width;
        ApplyLayout();
    }

    /// <summary>
    /// Sets the details pane width for the current library view as the user's saved share of the
    /// available width (or the default share), so the layout scales as the window is resized or maximized.
    /// </summary>
    public void ApplyLayout()
    {
        var settings = _settingsService.Load();
        var listView = LibraryViewModel.IsListView;
        var fraction = (listView ? settings.DetailsPaneFractionList : settings.DetailsPaneFractionGrid)
                       ?? (listView ? DefaultListFraction : DefaultGridFraction);

        var available = _availableWidth > 0 ? _availableWidth : 900;
        var maxWidth = Math.Max(MinDetailsWidth, available - SplitterWidth - MinLibraryWidth);
        try
        {
            DetailsPaneWidth = new GridLength(Math.Clamp(fraction * available, MinDetailsWidth, maxWidth));
        }
        finally
        {
        }
    }

    /// <summary>Remembers the pane width the user just dragged to, as a share of the available width.</summary>
    public void SaveUserPaneWidth(double width)
    {
        if (_availableWidth <= 0 || width < 1)
        {
            return;
        }

        var fraction = Math.Clamp(width / _availableWidth, 0.05, 0.95);
        var settings = _settingsService.Load();
        if (LibraryViewModel.IsListView)
        {
            settings.DetailsPaneFractionList = fraction;
        }
        else
        {
            settings.DetailsPaneFractionGrid = fraction;
        }

        _settingsService.Save(settings);
    }

    private void ResetLayout(bool listView)
    {
        var settings = _settingsService.Load();
        if (listView)
        {
            settings.DetailsPaneFractionList = null;
        }
        else
        {
            settings.DetailsPaneFractionGrid = null;
        }

        _settingsService.Save(settings);

        if (listView == LibraryViewModel.IsListView)
        {
            ApplyLayout();
        }
    }

    [RelayCommand]
    private void ToggleNav() => IsNavCollapsed = !IsNavCollapsed;

    partial void OnIsNavCollapsedChanged(bool value)
    {
        var settings = _settingsService.Load();
        settings.IsNavCollapsed = value;
        _settingsService.Save(settings);
    }

    [RelayCommand]
    private async Task GoToLibraryAsync()
    {
        SelectedTab = AppTab.Library;
        await LibraryViewModel.LoadCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task GoToScanReviewAsync()
    {
        SelectedTab = AppTab.ScanReview;
        await ScanReviewViewModel.ScanCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task GoToSettingsAsync()
    {
        SelectedTab = AppTab.Settings;
        await SettingsViewModel.LoadCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private void GoToDebugConsole()
    {
        SelectedTab = AppTab.DebugConsole;
    }
}
