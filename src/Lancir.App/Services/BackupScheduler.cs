using System.IO;
using Lancir.Core.Services;

namespace Lancir.App.Services;

/// <summary>
/// Takes the automatic backups at a fixed time: every night at 00:00 (<see cref="BackupService.ScheduledTime"/>). There is no polling:
/// one timer sleeps until the next midnight. A backup missed because the app was not running then is taken a little after the next
/// start, and once. A backup is not made while a game runs, so the disk work never competes with a game; it waits for the game to end.
/// A backup is also looked for when one is switched on in Settings.
/// </summary>
public sealed class BackupScheduler(BackupService backups, PlaytimeTracker tracker, DebugLogService log)
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(2); // loading the library comes first
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile bool _waitingForGameToEnd;

    public void Start()
    {
        tracker.RunningChanged += (gameId, running) =>
        {
            if (!running && _waitingForGameToEnd)
            {
                _ = CheckAsync();
            }
        };
        CheckRequested += () => _ = CheckAsync();
        _ = Task.Run(RunAsync);
    }

    private async Task RunAsync()
    {
        await Task.Delay(StartupDelay);
        while (true)
        {
            await CheckAsync();
            var wait = BackupService.NextSlot(DateTime.Now) - DateTime.Now + TimeSpan.FromSeconds(5);
            await Task.Delay(wait);
        }
    }

    private async Task CheckAsync()
    {
        if (!await _gate.WaitAsync(0))
        {
            return; // a backup is being made
        }

        try
        {
            if (tracker.AnyRunning)
            {
                _waitingForGameToEnd = backups.DueKinds().Count > 0;
                return;
            }

            _waitingForGameToEnd = false;
            if (await backups.RunDueBackupsAsync())
            {
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => BackupDone?.Invoke());
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                       or Microsoft.Data.Sqlite.SqliteException or InvalidDataException)
        {
            log.Log($"Otomatik yedekleme başarısız: {ex.Message}"); // tried again at the next midnight or start
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Raised (on the UI thread) after an automatic backup was taken, so Settings can refresh its list.</summary>
    public static event Action? BackupDone;

    /// <summary>Asks for a look at what is due now (a backup kind was just switched on).</summary>
    public static event Action? CheckRequested;

    public static void RequestCheck() => CheckRequested?.Invoke();
}
