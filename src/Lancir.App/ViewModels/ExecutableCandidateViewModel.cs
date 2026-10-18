using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Lancir.App.Services;

namespace Lancir.App.ViewModels;

/// <summary>
/// One executable found in a scanned game folder, shown as a checkbox so the user can pick
/// which one actually launches the game (or discard it as not being the game at all).
/// </summary>
public sealed partial class ExecutableCandidateViewModel : ObservableObject
{
    public ExecutableCandidateViewModel(string fullPath, string relativePath, bool isSelected)
    {
        FullPath = fullPath;
        RelativePath = relativePath;
        _isSelected = isSelected;
        IconSource = ExecutableIconExtractor.TryExtractIcon(fullPath);
    }

    public string FullPath { get; }

    public string RelativePath { get; }

    public ImageSource? IconSource { get; }

    [ObservableProperty]
    private bool _isSelected;
}
