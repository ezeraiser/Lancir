using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Lancir.Core.Services;
using Microsoft.Win32;
using Lancir.App.Services;

namespace Lancir.App.ViewModels;

public sealed partial class ScanReviewViewModel : ViewModelBase
{
    private readonly LibraryService _libraryService;

    public ScanReviewViewModel(LibraryService libraryService)
    {
        _libraryService = libraryService;
    }

    public ObservableCollection<ScanReviewItemViewModel> Items { get; } = [];

    public ObservableCollection<string> WatchedFolders { get; } = [];

    public event EventHandler? ReviewCompleted;

    [RelayCommand]
    private async Task LoadFoldersAsync()
    {
        var folders = await _libraryService.GetWatchedFolderPathsAsync();
        WatchedFolders.Clear();
        foreach (var folder in folders)
        {
            WatchedFolders.Add(folder);
        }
    }

    [RelayCommand]
    private async Task AddFolderAsync()
    {
        var dialog = new OpenFolderDialog
        {
            Title = Loc.T("Oyunların bulunduğu klasörü seçin"),
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await _libraryService.AddWatchedFolderAsync(dialog.FolderName);

        if (!WatchedFolders.Contains(dialog.FolderName))
        {
            WatchedFolders.Add(dialog.FolderName);
        }

        await ScanAsync();
    }

    [RelayCommand]
    private async Task RemoveFolderAsync(string? folderPath)
    {
        if (folderPath is null)
        {
            return;
        }

        await _libraryService.RemoveWatchedFolderByPathAsync(folderPath);
        WatchedFolders.Remove(folderPath);
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await LoadFoldersAsync();
            Items.Clear();
            var results = await _libraryService.ScanForNewGamesAsync();
            foreach (var result in results)
            {
                Items.Add(new ScanReviewItemViewModel(result));
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = Loc.F("Tarama başarısız: {0}", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ConfirmAsync(ScanReviewItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        try
        {
            await _libraryService.ConfirmGameAsync(
                item.ScanResult.Entry,
                item.SuggestedName,
                item.SelectedExecutablePath);

            item.IsConfirmed = true;
            Items.Remove(item);
        }
        catch (Exception ex)
        {
            ErrorMessage = Loc.F("'{0}' eklenemedi: {1}", item.SuggestedName, ex.Message);
        }
    }

    [RelayCommand]
    private void Skip(ScanReviewItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        item.IsSkipped = true;
        Items.Remove(item);
    }

    /// <summary>
    /// Marks a single executable candidate as "not the game" — it's removed from this review
    /// item and permanently excluded from future scan suggestions.
    /// </summary>
    [RelayCommand]
    private async Task IgnoreExecutableAsync(ExecutableCandidateViewModel? candidate)
    {
        if (candidate is null)
        {
            return;
        }

        var owner = Items.FirstOrDefault(item => item.ExecutableCandidates.Contains(candidate));
        if (owner is null)
        {
            return;
        }

        await _libraryService.IgnoreExecutableAsync(candidate.FullPath);
        owner.RemoveExecutableCandidate(candidate);
    }

    /// <summary>
    /// Marks the whole folder for a review item as "not a game" — removes the item entirely and
    /// permanently excludes that folder from future scan suggestions.
    /// </summary>
    [RelayCommand]
    private async Task IgnoreFolderAsync(ScanReviewItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        await _libraryService.IgnoreFolderAsync(item.ScanResult.Entry.FolderPath);
        Items.Remove(item);
    }

    [RelayCommand]
    private void Finish()
    {
        ReviewCompleted?.Invoke(this, EventArgs.Empty);
    }
}
