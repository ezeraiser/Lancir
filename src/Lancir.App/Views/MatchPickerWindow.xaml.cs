using System.Windows;
using System.Windows.Controls;
using Lancir.App.ViewModels;
using Lancir.Core.Models;
using Wpf.Ui.Controls;

namespace Lancir.App.Views;

public partial class MatchPickerWindow : FluentWindow
{
    public MatchPickerWindow(MatchPickerViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    /// <summary>The candidate the user picked, or null if they cancelled.</summary>
    public MatchCandidate? Result { get; private set; }

    private void OnCandidateClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: MatchCandidate candidate })
        {
            Result = candidate;
            DialogResult = true;
            Close();
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Result = null;
        DialogResult = false;
        Close();
    }
}
