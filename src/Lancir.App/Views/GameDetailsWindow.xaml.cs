using System.Windows;
using Lancir.App.ViewModels;
using Microsoft.Win32;
using Wpf.Ui.Controls;
using Lancir.App.Services;
using Lancir.Core.Scanning;
using Lancir.Core.Services;

namespace Lancir.App.Views;

public partial class GameDetailsWindow : FluentWindow
{
    private GameDetailsViewModel ViewModel => (GameDetailsViewModel)DataContext;

    public GameDetailsWindow(GameDetailsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    /// <summary>True if the user clicked Save.</summary>
    public bool Saved { get; private set; }

    private void OnBrowseExecutableClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = Loc.T("Çalıştırılabilir dosyalar (*.exe)|*.exe|Tüm dosyalar (*.*)|*.*"),
            InitialDirectory = ViewModel.FolderPath,
        };

        if (dialog.ShowDialog(this) == true)
        {
            ViewModel.ExecutablePath = dialog.FileName;
        }
    }

    private void OnBrowseWorkingDirectoryClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { InitialDirectory = ViewModel.FolderPath };
        if (dialog.ShowDialog(this) == true)
        {
            ViewModel.WorkingDirectory = dialog.FolderName;
        }
    }

    /// <summary>Picks the real game exe and adds its file name to the processes to track.</summary>
    private void OnBrowseTrackedProcessClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = Loc.T("Çalıştırılabilir dosyalar (*.exe)|*.exe|Tüm dosyalar (*.*)|*.*"),
            InitialDirectory = ViewModel.FolderPath,
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var fileName = System.IO.Path.GetFileName(dialog.FileName);
        var existing = PlaytimeTracker.ParseProcessNames(ViewModel.TrackedProcessName);
        var newName = PlaytimeTracker.ParseProcessNames(fileName)[0];
        if (!existing.Contains(newName, StringComparer.OrdinalIgnoreCase))
        {
            ViewModel.TrackedProcessName = string.IsNullOrWhiteSpace(ViewModel.TrackedProcessName)
                ? fileName
                : $"{ViewModel.TrackedProcessName.Trim().TrimEnd(',', ';')}, {fileName}";
        }
    }

    /// <summary>Looks up the game's install folder in Steam's library manifests and picks its main exe.</summary>
    private void OnFindSteamExeClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SteamAppId is not { } appId)
        {
            return;
        }

        var folder = SteamLocator.FindInstallFolder(appId);
        if (folder is null)
        {
            ViewModel.SteamFindMessage = Loc.T("Bu oyun Steam'in kurulu oyunları arasında bulunamadı (kurulu değil ya da Steam bulunamadı).");
            return;
        }

        var candidates = ExecutableFinder.FindCandidates(folder, new HashSet<string>());
        if (candidates.Count == 0)
        {
            ViewModel.SteamFindMessage = Loc.F("Klasör bulundu ama içinde exe bulunamadı: {0}", folder);
            return;
        }

        ViewModel.ExecutablePath = candidates[0];
        ViewModel.SteamFindMessage = candidates.Count == 1
            ? Loc.F("Bulundu: {0}", candidates[0])
            : Loc.F("{0} aday bulundu, ilki seçildi: {1}. Yanlışsa Gözat ile değiştirin.", candidates.Count, candidates[0]);
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ViewModel.DisplayName))
        {
            return;
        }

        if (ViewModel.IsSteamAppIdInvalid || ViewModel.IsLaunchUriInvalid)
        {
            return;
        }

        Saved = true;
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Saved = false;
        DialogResult = false;
        Close();
    }
}
