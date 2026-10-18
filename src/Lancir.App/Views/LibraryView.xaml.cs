using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Lancir.App.ViewModels;

namespace Lancir.App.Views;

public partial class LibraryView : UserControl
{
    private Point _dragStart;

    public LibraryView()
    {
        InitializeComponent();
    }

    /// <summary>Opens the add menu (Steam game / by hand) under the button.</summary>
    private void OnAddGameClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    private void OnCardClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is LibraryViewModel viewModel &&
            sender is FrameworkElement { DataContext: LibraryGameViewModel gameViewModel })
        {
            viewModel.SelectGame(gameViewModel);
        }
    }

    private void OnItemMouseDown(object sender, MouseButtonEventArgs e) => _dragStart = e.GetPosition(null);

    private void OnItemMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed ||
            sender is not FrameworkElement { DataContext: LibraryGameViewModel gameViewModel } element)
        {
            return;
        }

        var delta = e.GetPosition(null) - _dragStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        DragDrop.DoDragDrop(element, new DataObject(typeof(LibraryGameViewModel), gameViewModel), DragDropEffects.Move);
    }

    private void OnItemDragOver(object sender, DragEventArgs e)
    {
        var canDrop = e.Data.GetDataPresent(typeof(LibraryGameViewModel));
        e.Effects = canDrop ? DragDropEffects.Move : DragDropEffects.None;

        if (canDrop && sender is FrameworkElement { DataContext: LibraryGameViewModel target })
        {
            target.IsDropTarget = !ReferenceEquals(target, e.Data.GetData(typeof(LibraryGameViewModel)));
        }

        e.Handled = true;
    }

    private void OnItemDragLeave(object sender, DragEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LibraryGameViewModel target })
        {
            target.IsDropTarget = false;
        }
    }

    private async void OnItemDrop(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: LibraryGameViewModel target })
        {
            return;
        }

        target.IsDropTarget = false;

        if (DataContext is LibraryViewModel viewModel &&
            e.Data.GetData(typeof(LibraryGameViewModel)) is LibraryGameViewModel dragged)
        {
            await viewModel.MoveGameAsync(dragged, target);
        }

        e.Handled = true;
    }
}
