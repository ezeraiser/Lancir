using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Lancir.App.Services;
using Lancir.Core.Models;

namespace Lancir.App.ViewModels;

public sealed partial class MatchPickerViewModel : ObservableObject
{
    public MatchPickerViewModel(string gameDisplayName, IReadOnlyList<MatchCandidate> candidates)
    {
        GameDisplayName = gameDisplayName;
        Candidates = new ObservableCollection<MatchCandidate>(candidates);
    }

    public string GameDisplayName { get; }

    public string HeaderText => Loc.F("'{0}' için bulunan sonuçlardan doğru olanı seçin:", GameDisplayName);

    public ObservableCollection<MatchCandidate> Candidates { get; }

    [ObservableProperty]
    private MatchCandidate? _selectedCandidate;
}
