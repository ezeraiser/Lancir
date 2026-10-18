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
        _ = new WindowBehavior(this, settingsService);
        DataContext = viewModel;
        ApplyZoom(settingsService.Load().UiZoom, save: false);
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

    private bool _settingZoom;

    /// <summary>Scales the whole interface (not the title bar) between 50% and 100%; the layout inside re-flows to the room it gets.</summary>
    private void ApplyZoom(double zoom, bool save)
    {
        zoom = double.IsNaN(zoom) ? 1 : Math.Round(Math.Clamp(zoom, 0.5, 1) * 20) / 20;
        ZoomTransform.ScaleX = ZoomTransform.ScaleY = zoom;
        ZoomButton.Content = $"{zoom * 100:0}%";
        ZoomValueText.Text = $"{zoom * 100:0}%";
        _settingZoom = true;
        ZoomSlider.Value = zoom * 100;
        _settingZoom = false;
        if (save)
        {
            var settings = _settingsService.Load();
            settings.UiZoom = zoom;
            _settingsService.Save(settings);
        }
    }

    private void OnZoomButtonClick(object sender, RoutedEventArgs e) => ZoomPopup.IsOpen = !ZoomPopup.IsOpen;

    private void OnZoomSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // the slider clamps its value while the window is still being built, before the other named parts exist
        if (!_settingZoom && IsLoaded)
        {
            ApplyZoom(e.NewValue / 100, save: true);
        }
    }

    private void OnZoomResetClick(object sender, RoutedEventArgs e) => ApplyZoom(1, save: true);

    /// <summary>Applies "start minimized" once the window has been shown.</summary>
    public void ApplyStartState() => WindowBehavior.Current?.ApplyStartState();

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

        // With "close to tray" on, the X button only hides the window (and does not forget its saved size yet).
        WindowBehavior.Current?.HandleClosing(e);
        if (e.Cancel)
        {
            return;
        }

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

    /// <summary>Opens the button's menu beside it (the nav strip's sort and add buttons).</summary>
    private void OnNavMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Right;
            menu.IsOpen = true;
        }
    }

    // ---- the libraries of the strip can be dragged into another order (press, hold, move) ----

    private Point _scopeDragStart;

    private void OnScopeMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e) => _scopeDragStart = e.GetPosition(null);

    private void OnScopeMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed || sender is not FrameworkElement { DataContext: LibraryScopeItem item } element)
        {
            return;
        }

        var position = e.GetPosition(null);
        if (Math.Abs(position.X - _scopeDragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(position.Y - _scopeDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        DragDrop.DoDragDrop(element, new DataObject(typeof(LibraryScopeItem), item), DragDropEffects.Move);
    }

    private void OnScopeDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(LibraryScopeItem)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnScopeDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(LibraryScopeItem)) is LibraryScopeItem dragged && sender is FrameworkElement { DataContext: LibraryScopeItem target })
        {
            ((MainViewModel)DataContext).LibraryViewModel.MoveScope(dragged, target);
        }

        e.Handled = true;
    }

    private void OnScopeIconClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LibraryScopeItem item })
        {
            ((MainViewModel)DataContext).LibraryViewModel.ChangeCustomLibraryIcon(item);
        }
    }

    private void OnScopeRenameClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LibraryScopeItem item })
        {
            ((MainViewModel)DataContext).LibraryViewModel.RenameCustomLibrary(item);
        }
    }

    private void OnScopeDeleteClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LibraryScopeItem item })
        {
            ((MainViewModel)DataContext).LibraryViewModel.DeleteCustomLibrary(item);
        }
    }

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
