using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lancir.App.Services;
using Lancir.Core.Services;

namespace Lancir.App.ViewModels;

/// <summary>The "Hepsini eşleştir" job as Settings shows it: its state and progress, and the buttons to start and cancel it.</summary>
public sealed partial class MatchJobViewModel : ObservableObject
{
    private readonly MatchAllJob _job;
    private readonly MatchAllCoordinator _coordinator;
    private int _refreshQueued;

    public MatchJobViewModel(MatchAllJob job, MatchAllCoordinator coordinator)
    {
        _job = job;
        _coordinator = coordinator;
        _job.Changed += () =>
        {
            // one refresh waits for the UI thread at a time: the job may report faster than the page can follow
            if (Interlocked.Exchange(ref _refreshQueued, 1) == 0)
            {
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    Interlocked.Exchange(ref _refreshQueued, 0);
                    Refresh();
                });
            }
        };
        Refresh();
    }

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private double _progressMax = 1;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private string? _currentGameText;

    [ObservableProperty]
    private string? _detailText;

    public void Refresh()
    {
        var s = _job.Snapshot;
        IsRunning = s.IsRunning;
        ProgressMax = Math.Max(1, s.Total);
        ProgressValue = s.Processed;
        CurrentGameText = s.IsRunning && s.CurrentGame is { } game ? Loc.F("Şu an: {0}", game) : null;
        var counts = Loc.F("{0} eşleşti, {1} bulunamadı, {2} hata", s.Matched, s.NotFound, s.Errors);
        var skipped = s.Skipped > 0 ? " · " + Loc.F("{0} oyun atlandı (son 1 saatte denenmiş)", s.Skipped) : "";
        StatusText = s.Status switch
        {
            MatchJobStatus.Running => Loc.F("Çalışıyor: {0} / {1}", s.Processed, s.Total),
            MatchJobStatus.Completed => Loc.F("Tamamlandı ({0}): {1} oyun işlendi", Time(s.FinishedUtc), s.Processed),
            MatchJobStatus.Cancelled => Loc.F("İptal edildi ({0}): {1} / {2} oyun işlendi", Time(s.FinishedUtc), s.Processed, s.Total),
            MatchJobStatus.Interrupted => Loc.F("Yarıda kaldı, uygulama kapanmış: {0} / {1} oyun işlendi", s.Processed, s.Total),
            _ => Loc.T("Henüz bir eşleştirme işi çalıştırılmadı."),
        };
        DetailText = s.Status == MatchJobStatus.None ? null : counts + skipped;
        CancelCommand.NotifyCanExecuteChanged();
    }

    private static string Time(DateTime? utc) => utc?.ToLocalTime().ToString("g", AppLanguage.Culture) ?? "";

    [RelayCommand]
    private async Task StartAsync()
    {
        await _coordinator.StartAsync();
        Refresh();
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private async Task CancelAsync()
    {
        await _job.CancelAsync();
        Refresh();
    }

    private bool CanCancel() => _job.IsRunning;
}
