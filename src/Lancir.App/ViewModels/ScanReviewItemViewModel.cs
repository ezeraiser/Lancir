using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Lancir.Core.Services;

namespace Lancir.App.ViewModels;

public sealed partial class ScanReviewItemViewModel : ObservableObject
{
    public ScanReviewItemViewModel(ScanResult scanResult)
    {
        ScanResult = scanResult;
        SuggestedName = scanResult.Entry.CleanedName;

        var suggested = scanResult.Entry.SuggestedExecutablePath;
        foreach (var candidatePath in scanResult.Entry.ExecutableCandidates)
        {
            var relativePath = Path.GetRelativePath(scanResult.Entry.FolderPath, candidatePath);
            var candidate = new ExecutableCandidateViewModel(
                candidatePath, relativePath, isSelected: candidatePath == suggested);
            candidate.PropertyChanged += OnExecutableCandidatePropertyChanged;
            ExecutableCandidates.Add(candidate);
        }
    }

    public ScanResult ScanResult { get; }

    public string RawFolderName => ScanResult.Entry.RawFolderName;

    public ObservableCollection<ExecutableCandidateViewModel> ExecutableCandidates { get; } = [];

    public bool HasNoExecutable => ExecutableCandidates.Count == 0;

    public string? SelectedExecutablePath => ExecutableCandidates.FirstOrDefault(c => c.IsSelected)?.FullPath;

    [ObservableProperty]
    private string _suggestedName;

    [ObservableProperty]
    private bool _isConfirmed;

    [ObservableProperty]
    private bool _isSkipped;

    private void OnExecutableCandidatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ExecutableCandidateViewModel.IsSelected) ||
            sender is not ExecutableCandidateViewModel changed || !changed.IsSelected)
        {
            return;
        }

        // Only one executable can be "the" launcher for a game; picking one deselects the rest.
        foreach (var candidate in ExecutableCandidates)
        {
            if (!ReferenceEquals(candidate, changed))
            {
                candidate.IsSelected = false;
            }
        }
    }

    public void RemoveExecutableCandidate(ExecutableCandidateViewModel candidate)
    {
        candidate.PropertyChanged -= OnExecutableCandidatePropertyChanged;
        ExecutableCandidates.Remove(candidate);

        if (candidate.IsSelected && ExecutableCandidates.Count > 0)
        {
            ExecutableCandidates[0].IsSelected = true;
        }
    }
}
