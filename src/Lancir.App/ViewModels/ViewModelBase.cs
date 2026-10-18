using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Lancir.App.ViewModels;

public abstract partial class ViewModelBase : ObservableObject
{
    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>
    /// Drives the error bar: open while there is a message. Closing the bar clears the message, so errors that
    /// arrived one after another all go away together and the next error opens the bar again.
    /// </summary>
    public bool IsErrorOpen
    {
        get => ErrorMessage is not null;
        set
        {
            if (!value)
            {
                ErrorMessage = null;
            }
        }
    }

    /// <summary>How long an error stays on screen before it goes away on its own.</summary>
    private static readonly TimeSpan ErrorDuration = TimeSpan.FromSeconds(4);

    private DispatcherTimer? _errorTimer;

    partial void OnErrorMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(IsErrorOpen));

        // Every new message restarts the countdown, so an error that arrives while another is showing is not cut short.
        _errorTimer?.Stop();
        if (value is null)
        {
            return;
        }

        _errorTimer ??= CreateErrorTimer();
        _errorTimer.Start();
    }

    private DispatcherTimer CreateErrorTimer()
    {
        var timer = new DispatcherTimer { Interval = ErrorDuration };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            ErrorMessage = null;
        };
        return timer;
    }
}
