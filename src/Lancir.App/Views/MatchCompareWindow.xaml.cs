using System.Windows;
using System.Windows.Controls;
using Lancir.App.ViewModels;
using Wpf.Ui.Controls;

namespace Lancir.App.Views;

public partial class MatchCompareWindow : FluentWindow
{
    public MatchCompareWindow(MatchCompareViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    /// <summary>True if the user clicked Apply; false if they cancelled or closed the window.</summary>
    public bool Applied { get; private set; }

    private void OnUseOldClick(object sender, RoutedEventArgs e) => SetField(sender, useNew: false);

    private void OnUseNewClick(object sender, RoutedEventArgs e) => SetField(sender, useNew: true);

    private void SetField(object sender, bool useNew)
    {
        if (sender is not Border { DataContext: MatchCompareFieldViewModel field })
        {
            return;
        }

        // Picking the "new" side of an empty value would silently blank out the field on apply -
        // not a real choice, so ignore the click.
        if (useNew && !field.HasNewValue)
        {
            return;
        }

        field.UseNew = useNew;
    }

    private void OnApplyClick(object sender, RoutedEventArgs e)
    {
        Applied = true;
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Applied = false;
        DialogResult = false;
        Close();
    }
}
