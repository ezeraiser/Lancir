using System.Windows;
using System.Windows.Threading;
using Lancir.Core.Services;

namespace Lancir.App.Services;

/// <summary>
/// Sleep mode. While the main window is minimized or hidden in the notification area nobody is looking at the app, so it does as
/// little as it can: the video player and the clips are closed and libVLC released, memory is given back to Windows once, the process
/// goes into efficiency mode (lowest priority, power throttling, lowest memory priority) and its timers stop. What is left is the
/// game check, which then runs at most every 30 seconds (see <see cref="PlaytimeTracker.PowerSaving"/>). Showing the window wakes
/// everything up again at once. Controlled by "BackgroundSleepEnabled" in Settings.
/// </summary>
public sealed class BackgroundMode
{
    // A moment out of sight is not yet "asleep": a minimize followed by a click on the tray icon must not cost a trim.
    private static readonly TimeSpan EnterDelay = TimeSpan.FromSeconds(3);

    // Time for the players that were just closed to finish stopping before libVLC is released.
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(1500);

    private readonly Window _window;
    private readonly SettingsService _settings;
    private readonly PlaytimeTracker _tracker;
    private readonly DispatcherTimer _enterTimer;
    private readonly object _gate = new();
    private int _generation;

    public BackgroundMode(Window window, SettingsService settings, PlaytimeTracker tracker)
    {
        _window = window;
        _settings = settings;
        _tracker = tracker;

        _enterTimer = new DispatcherTimer { Interval = EnterDelay };
        _enterTimer.Tick += (_, _) =>
        {
            _enterTimer.Stop();
            if (OutOfSight && !IsSleeping && _settings.Load().BackgroundSleepEnabled)
            {
                Sleep();
            }
        };

        window.IsVisibleChanged += (_, _) => Evaluate();
        window.StateChanged += (_, _) => Evaluate();
        Evaluate();
    }

    /// <summary>True while the app sleeps.</summary>
    public static bool IsSleeping { get; private set; }

    /// <summary>Raised on the UI thread when the app falls asleep (true) or wakes up (false).</summary>
    public static event Action<bool>? Changed;

    private bool OutOfSight => !_window.IsVisible || _window.WindowState == WindowState.Minimized;

    private void Evaluate()
    {
        if (OutOfSight)
        {
            if (!IsSleeping && !_enterTimer.IsEnabled)
            {
                _enterTimer.Start();
            }
        }
        else
        {
            _enterTimer.Stop();
            if (IsSleeping)
            {
                Wake();
            }
        }
    }

    private void Sleep()
    {
        IsSleeping = true;
        var generation = Interlocked.Increment(ref _generation);
        _tracker.PowerSaving = true;
        Changed?.Invoke(true); // the video viewer closes, timers stop, pending saves are written

        _ = Task.Run(async () =>
        {
            await Task.Delay(SettleDelay);
            if (Volatile.Read(ref _generation) != generation)
            {
                return; // woken up meanwhile
            }

            ClipStream.ReleaseIfIdle();
            VideoStream.ReleaseIfIdle();

            // a network stream can take a few seconds to stop: libVLC is only released once no player is left, so try once more
            await Task.Delay(SettleDelay * 3);
            if (Volatile.Read(ref _generation) != generation)
            {
                return;
            }

            ClipStream.ReleaseIfIdle();
            VideoStream.ReleaseIfIdle();
            MemoryTrimmer.Trim();

            lock (_gate)
            {
                if (_generation == generation)
                {
                    ProcessEfficiency.Enter();
                }
            }
        });
    }

    private void Wake()
    {
        lock (_gate)
        {
            Interlocked.Increment(ref _generation); // cancels a sleep that is still being set up
            ProcessEfficiency.Leave();             // first: the window must not open at the lowest priority
        }

        IsSleeping = false;
        _tracker.PowerSaving = false;
        Changed?.Invoke(false);
    }
}
