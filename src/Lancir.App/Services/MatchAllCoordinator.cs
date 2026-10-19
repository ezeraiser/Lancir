using System.Windows;
using Lancir.App.Views;
using Lancir.Core.Data;
using Lancir.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lancir.App.Services;

/// <summary>
/// Starts the match-all job for the library menu and for Settings, asking what is needed first: a job that is still running has to be
/// cancelled before a new one may start, and when games were tried within the last hour the user chooses between continuing (those are
/// skipped) and starting over (all are tried again).
/// </summary>
public sealed class MatchAllCoordinator(MatchAllJob job, IServiceScopeFactory scopes)
{
    /// <summary>Returns true when a job was started.</summary>
    public async Task<bool> StartAsync()
    {
        var title = Loc.T("Eşleştirme işi");

        var running = job.Snapshot;
        if (running.IsRunning)
        {
            var choice = ChoiceDialogWindow.Ask(
                title,
                Loc.F("Bir eşleştirme işi zaten çalışıyor ({0} / {1}). Yenisini başlatmak için önce bunun iptal edilmesi gerekir.", running.Processed, running.Total),
                Loc.T("İptal et ve yeni iş başlat"), Loc.T("Çalışmaya devam etsin"));
            if (choice != 0)
            {
                return false;
            }

            await job.CancelAsync();
        }

        var unmatched = await LoadUnmatchedAsync();
        if (unmatched.Count == 0)
        {
            MessageBox.Show(Application.Current.MainWindow, Loc.T("Eşleşmeyen oyun yok."), title, MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        var skipRecent = false;
        var recent = job.CountRecentlyTried(unmatched.Select(g => g.Id));
        if (recent > 0)
        {
            var left = unmatched.Count - recent;
            var message = Loc.F("Son 1 saat içinde {0} oyun zaten denenmiş (eşleşme bulunamamış ya da iş yarıda kalmış).", recent)
                          + " " + (left > 0
                              ? Loc.F("Kaldığın yerden devam edersen bunlar atlanır ve {0} oyun işlenir. Baştan başlarsan {1} oyunun hepsi yeniden denenir.", left, unmatched.Count)
                              : Loc.F("Kalan oyun yok; baştan başlarsan {0} oyunun hepsi yeniden denenir.", unmatched.Count));
            var continueLabel = Loc.F("Kaldığın yerden devam et ({0} oyun)", left);
            var restartLabel = Loc.F("Baştan başla ({0} oyun)", unmatched.Count);
            var cancelLabel = Loc.T("Vazgeç");

            // 0 = continue (skip what was tried), 1 = start over, -1 = give up
            var picked = left > 0
                ? ChoiceDialogWindow.Ask(title, message, continueLabel, restartLabel, cancelLabel) switch { 0 => 0, 1 => 1, _ => -1 }
                : ChoiceDialogWindow.Ask(title, message, restartLabel, cancelLabel) switch { 0 => 1, _ => -1 };
            if (picked < 0)
            {
                return false;
            }

            skipRecent = picked == 0;
        }

        return job.TryStart(unmatched, skipRecent);
    }

    private async Task<List<MatchJobGame>> LoadUnmatchedAsync()
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        var rows = await db.Games.AsNoTracking()
            .Where(g => g.SteamGridDbId == null && g.IgdbId == null)
            .OrderBy(g => g.Id)
            .Select(g => new { g.Id, g.DisplayName })
            .ToListAsync();
        return rows.Select(g => new MatchJobGame(g.Id, g.DisplayName)).ToList();
    }
}
