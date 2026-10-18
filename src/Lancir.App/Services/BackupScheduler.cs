using System.IO;
using Lancir.Core.Services;

namespace Lancir.App.Services;

/// <summary>
/// Takes the automatic backups: a little after start and then every half hour it asks whether one is due (daily, weekly, monthly, as
/// set in Settings). A backup is not made while a game runs, so the disk work never competes with a game; it is tried again later.
/// </summary>
public sealed class BackupScheduler(BackupService backups, PlaytimeTracker tracker, DebugLogService log)
{
    private static readonly TimeSpan FirstCheck = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(30);

    public void Start() => _ = Task.Run(RunAsync);

    private async Task RunAsync()
    {
        await Task.Delay(FirstCheck);
        while (true)
        {
            try
            {
                if (!tracker.AnyRunning && await backups.RunDueBackupsAsync())
                {
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => BackupDone?.Invoke());
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                           or Microsoft.Data.Sqlite.SqliteException or System.IO.InvalidDataException)
            {
                log.Log($"Otomatik yedekleme başarısız: {ex.Message}");
            }

            await Task.Delay(CheckInterval);
        }
    }

    /// <summary>Raised (on the UI thread) after an automatic backup was taken, so Settings can refresh its list.</summary>
    public static event Action? BackupDone;
}
