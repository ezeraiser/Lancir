using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Lancir.App.Controls;
using Lancir.App.ViewModels;
using Lancir.App.Services;

namespace Lancir.App.Views;

public partial class GamePanelView : UserControl
{
    private const int MaxPlayingClips = 3;

    private readonly System.Windows.Threading.DispatcherTimer _clipTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly System.Windows.Threading.DispatcherTimer _hoverTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private Point _gripStart;

    public GamePanelView()
    {
        InitializeComponent();
        _hoverTimer.Tick += (_, _) =>
        {
            _hoverTimer.Stop();
            Panel?.ReplayClips();
            UpdateClips();
        };
        _clipTimer.Tick += (_, _) =>
        {
            _clipTimer.Stop();
            UpdateClips();
        };
        Loaded += (_, _) =>
        {
            if (Window.GetWindow(this) is { } window)
            {
                window.Activated += (_, _) => ScheduleClipUpdate();
                window.Deactivated += (_, _) => ScheduleClipUpdate();
                window.StateChanged += (_, _) => ScheduleClipUpdate();
            }
        };
        Unloaded += (_, _) => Panel?.PlayClips([]);
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is GamePanelViewModel panel)
            {
                panel.AboutBlocks.CollectionChanged += (_, _) => ScheduleClipUpdate();
                panel.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(GamePanelViewModel.IsViewerOpen) && panel.IsViewerOpen)
                    {
                        Dispatcher.BeginInvoke(() => Viewer.Focus(), System.Windows.Threading.DispatcherPriority.Input);
                    }
                };
            }
        };
    }

    private GamePanelViewModel? Panel => DataContext as GamePanelViewModel;

    /// <summary>
    /// Whatever control takes focus by itself (e.g. a text box far down the cards when the window
    /// activates) must not make the panel scroll away from the top; the user scrolls manually.
    /// </summary>
    private void OnCardsRequestBringIntoView(object sender, RequestBringIntoViewEventArgs e) => e.Handled = true;

    // ---- description pictures and clips: opened from their right-click menu ----

    private void OnOpenClipMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AboutClip clip })
        {
            Panel?.OpenClipCommand.Execute(clip);
        }
    }

    private void OnInstallAvifClick(object sender, RoutedEventArgs e) => Panel?.InstallAvifExtensionCommand.Execute(null);

    private void OnOpenImageMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AboutImage image })
        {
            Panel?.OpenFileCommand.Execute(image.DisplayUrl);
        }
    }

    // ---- clips: only the ones on screen play ----

    // The pointer resting on the description for a moment plays its clips again for another round.
    private void OnAboutMouseEnter(object sender, MouseEventArgs e)
    {
        _hoverTimer.Stop();
        _hoverTimer.Start();
    }

    private void OnAboutMouseLeave(object sender, MouseEventArgs e) => _hoverTimer.Stop();

    private void OnCardsScrollChanged(object sender, ScrollChangedEventArgs e) => ScheduleClipUpdate();

    private void OnCardsHostSizeChanged(object sender, SizeChangedEventArgs e) => ScheduleClipUpdate();

    private void ScheduleClipUpdate()
    {
        _clipTimer.Stop();
        _clipTimer.Start();
    }

    /// <summary>
    /// Starts the clips whose tiles are mostly inside the cards viewport (at most a few at a time) and stops
    /// the others, so scrolling away or switching to another window also stops the decoding.
    /// </summary>
    private void UpdateClips()
    {
        if (Panel is null)
        {
            return;
        }

        var window = Window.GetWindow(this);
        var visible = new List<AboutClip>();
        if (window is { IsActive: true, WindowState: not WindowState.Minimized } && IsVisible)
        {
            var viewport = new Rect(0, 0, CardsScroll.ViewportWidth, CardsScroll.ViewportHeight);
            foreach (var tile in FindChildren<FrameworkElement>(CardsHost))
            {
                if (tile.Name != "ClipRoot" || tile.DataContext is not AboutClip clip || tile.ActualHeight <= 0)
                {
                    continue;
                }

                var bounds = tile.TransformToAncestor(CardsScroll).TransformBounds(new Rect(tile.RenderSize));
                var shown = Rect.Intersect(bounds, viewport);
                if (!shown.IsEmpty && shown.Height >= bounds.Height * 0.4)
                {
                    visible.Add(clip);
                }
            }
        }

        Panel.PlayClips(visible.Take(MaxPlayingClips).ToList());
    }

    private static IEnumerable<T> FindChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in FindChildren<T>(child))
            {
                yield return nested;
            }
        }
    }

    // ---- header ----

    private void OnMatchClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    private void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if (Panel is not { Game: { } game } panel)
        {
            return;
        }

        var answer = MessageBox.Show(
            Loc.F("'{0}' kütüphaneden kaldırılsın mı? Oyunun dosyalarına dokunulmaz.", game.DisplayName),
            Loc.T("Kaldır"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (answer == MessageBoxResult.Yes)
        {
            panel.Library.RemoveGameCommand.Execute(game);
        }
    }

    // ---- card menu / resize ----

    private void OnCardSizeMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag, DataContext: DetailCardViewModel card } &&
            Enum.TryParse<CardSize>(tag, out var size))
        {
            Panel?.SetCardSize(card, size);
        }
    }

    private void OnResetLayoutMenuClick(object sender, RoutedEventArgs e) => Panel?.ResetLayout();

    private void OnResizeClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DetailCardViewModel card })
        {
            Panel?.ToggleCardSize(card);
        }
    }

    // ---- drag and drop of cards ----

    private void OnGripMouseDown(object sender, MouseButtonEventArgs e) => _gripStart = e.GetPosition(null);

    private void OnGripMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed ||
            sender is not FrameworkElement { DataContext: DetailCardViewModel card } element)
        {
            return;
        }

        var delta = e.GetPosition(null) - _gripStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        DragDrop.DoDragDrop(element, new DataObject(typeof(DetailCardViewModel), card), DragDropEffects.Move);
    }

    private void OnCardDragOver(object sender, DragEventArgs e)
    {
        var dragged = e.Data.GetData(typeof(DetailCardViewModel)) as DetailCardViewModel;
        e.Effects = dragged is null ? DragDropEffects.None : DragDropEffects.Move;

        if (dragged is not null && sender is FrameworkElement { DataContext: DetailCardViewModel target })
        {
            target.IsDropTarget = !ReferenceEquals(dragged, target);
        }

        e.Handled = true;
    }

    private void OnCardDragLeave(object sender, DragEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DetailCardViewModel target })
        {
            target.IsDropTarget = false;
        }
    }

    private void OnCardDrop(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: DetailCardViewModel target })
        {
            return;
        }

        target.IsDropTarget = false;

        if (e.Data.GetData(typeof(DetailCardViewModel)) is DetailCardViewModel dragged)
        {
            Panel?.MoveCard(dragged, target);
        }

        e.Handled = true;
    }

    private void OnHostDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(DetailCardViewModel)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>A card dropped on empty space: pin it to the column under the pointer.</summary>
    private void OnHostDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(DetailCardViewModel)) is not DetailCardViewModel dragged)
        {
            return;
        }

        var masonry = FindChild<MasonryPanel>(CardsHost);
        if (masonry is not null &&
            masonry.TryGetColumnAt(e.GetPosition(masonry), out var side, out var lastIndex))
        {
            Panel?.DropCardIntoColumn(dragged, side, lastIndex);
        }

        e.Handled = true;
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
            {
                return match;
            }

            if (FindChild<T>(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }
}
