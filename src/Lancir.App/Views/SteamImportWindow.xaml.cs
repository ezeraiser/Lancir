using System.Windows;
using Lancir.App.ViewModels;
using Wpf.Ui.Controls;

namespace Lancir.App.Views;

public partial class SteamImportWindow : FluentWindow
{
    public SteamImportWindow(SteamImportViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private SteamImportViewModel ViewModel => (SteamImportViewModel)DataContext;

    private async void OnLoaded(object sender, RoutedEventArgs e) => await ViewModel.LoadCommand.ExecuteAsync(null);

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
