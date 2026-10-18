using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace Lancir.App.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private bool _syncingKey;

    protected override void OnPropertyChanged(System.Windows.DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == DataContextProperty)
        {
            if (e.OldValue is ViewModels.SettingsViewModel old)
            {
                old.PropertyChanged -= OnViewModelPropertyChanged;
            }

            if (e.NewValue is ViewModels.SettingsViewModel current)
            {
                current.PropertyChanged += OnViewModelPropertyChanged;
                ShowKey(current.SteamApiKey);
            }
        }
    }

    // A PasswordBox cannot be data-bound, so the key is copied between it and the view model by hand.
    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModels.SettingsViewModel.SteamApiKey) && sender is ViewModels.SettingsViewModel vm)
        {
            ShowKey(vm.SteamApiKey);
        }
    }

    private void ShowKey(string? key)
    {
        if (SteamKeyBox is null || SteamKeyBox.Password == (key ?? ""))
        {
            return;
        }

        _syncingKey = true;
        SteamKeyBox.Password = key ?? "";
        _syncingKey = false;
    }

    private void OnSteamKeyChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (!_syncingKey && DataContext is ViewModels.SettingsViewModel vm)
        {
            vm.SteamApiKey = SteamKeyBox.Password;
        }
    }

    private void OnOpenLink(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
