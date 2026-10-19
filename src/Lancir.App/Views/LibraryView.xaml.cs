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

    private void OnFilterButtonClick(object sender, RoutedEventArgs e) => FilterPopup.IsOpen = true;

    /// <summary>Opens the add menu (Steam game / by hand) under the button.</summary>
    /// <summary>Fills the game menu's "Özel kütüphaneye ekle" list with the user's libraries, ticking the ones the game is in.</summary>
    private void OnLibrariesSubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.MenuItem { DataContext: LibraryGameViewModel game } parent || DataContext is not LibraryViewModel library)
        {
            return;
        }

        parent.Items.Clear();
        var custom = library.Scopes.Where(s => s.IsCustom).ToList();
        if (custom.Count == 0)
        {
            parent.Items.Add(new System.Windows.Controls.MenuItem { Header = Lancir.App.Services.Loc.T("Henüz özel kütüphane yok (soldaki + menüsünden oluşturun)"), IsEnabled = false });
            return;
        }

        foreach (var scope in custom)
        {
            var item = new System.Windows.Controls.MenuItem { Header = scope.Title, IsCheckable = true, IsChecked = library.IsInLibrary(scope, game) };
            item.Click += (_, _) => library.ToggleGameInLibrary(scope, game);
            parent.Items.Add(item);
        }
    }

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
