using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Lancir.App.Services;
using Lancir.App.ViewModels;
using Lancir.Core.Services;
using Wpf.Ui.Controls;

namespace Lancir.App;

public partial class MainWindow : FluentWindow
{
    private readonly SettingsService _settingsService;

    public MainWindow(MainViewModel viewModel, SettingsService settingsService)
    {
        InitializeComponent();
        _settingsService = settingsService;
        RestoreWindowPlacement();
        _ = new MemoryTrimmer(this, settingsService);
        DataContext = viewModel;
        viewModel.PropertyChanged += (_, e) =>
        {
            // The nav pane width changes with this; measure again once layout has caught up.
            if (e.PropertyName == nameof(MainViewModel.IsNavCollapsed))
            {
                Dispatcher.BeginInvoke(UpdateAvailableWidth, System.Windows.Threading.DispatcherPriority.Loaded);
            }
        };
        Loaded += async (_, _) =>
        {
            UpdateAvailableWidth(); // window size is known now
            await viewModel.GoToLibraryCommand.ExecuteAsync(null);
        };
    }

    /// <summary>Restores the size, position and maximized state from the last run, if they still fit on a screen.</summary>
    private void RestoreWindowPlacement()
    {
        var settings = _settingsService.Load();

        if (settings.WindowWidth is >= 300 and var width && settings.WindowHeight is >= 300 and var height)
        {
            Width = Math.Max(MinWidth, width);
            Height = Math.Max(MinHeight, height);
        }

        if (settings.WindowLeft is { } left && settings.WindowTop is { } top)
        {
            // Ignore a saved position that is now off-screen (e.g. a monitor was disconnected).
            var visible = new Rect(left, top, Width, Height);
            visible.Intersect(new Rect(
                SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight));
            if (visible is { Width: > 100, Height: > 100 })
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = left;
                Top = top;
            }
        }

        if (settings.WindowMaximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);

        // While maximized or minimized, RestoreBounds is the normal-state rectangle.
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        var settings = _settingsService.Load();
        settings.WindowLeft = bounds.Left;
        settings.WindowTop = bounds.Top;
        settings.WindowWidth = bounds.Width;
        settings.WindowHeight = bounds.Height;
        settings.WindowMaximized = WindowState == WindowState.Maximized;
        _settingsService.Save(settings);
    }

    /// <summary>Tells the view model how much width the library + details panes share, so the saved layout can scale.</summary>
    private void UpdateAvailableWidth() =>
        ((MainViewModel)DataContext).UpdateAvailableWidth(ContentGrid.ActualWidth - NavColumn.ActualWidth);

    private void OnContentGridSizeChanged(object sender, SizeChangedEventArgs e) => UpdateAvailableWidth();

    private void OnSplitterDragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e) =>
        ((MainViewModel)DataContext).SaveUserPaneWidth(DetailsPaneColumn.ActualWidth);

    private void OnCloseDetailsClick(object sender, RoutedEventArgs e)
    {
        ((MainViewModel)DataContext).LibraryViewModel.CloseDetails();
    }

    /// <summary>WPF doesn't clip a Border's children to its rounded corners, so the header image
    /// would poke out; clip the whole panel to a rounded rectangle instead.</summary>
    private void OnDetailsPaneSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is Border border)
        {
            border.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 12, 12);
        }
    }
}
