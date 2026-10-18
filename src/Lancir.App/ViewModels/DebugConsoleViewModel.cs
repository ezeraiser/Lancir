using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Lancir.Core.Services;

namespace Lancir.App.ViewModels;

public sealed partial class DebugConsoleViewModel : ViewModelBase
{
    private readonly DebugLogService _log;

    public DebugConsoleViewModel(DebugLogService log)
    {
        _log = log;
    }

    public ObservableCollection<string> Lines => _log.Lines;

    [RelayCommand]
    private void Clear() => _log.Clear();
}
