using System.Collections.ObjectModel;
using System.Threading;

namespace Lancir.Core.Services;

/// <summary>
/// Collects debug log lines from anywhere in the app (matching, scanning, launching, etc.) so
/// they can be shown live in the Debug Console view. Singleton: one shared, ever-growing log for
/// the lifetime of the app session.
/// </summary>
public sealed class DebugLogService
{
    private const int MaxLines = 2000;

    // Captured on construction (registered as a singleton, resolved on the UI thread during app
    // startup), so posts from background/async work land back on the UI thread that owns Lines.
    private readonly SynchronizationContext? _uiContext = SynchronizationContext.Current;

    public ObservableCollection<string> Lines { get; } = [];

    public void Log(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";

        if (_uiContext is not null)
        {
            _uiContext.Post(_ => AppendLine(line), null);
        }
        else
        {
            AppendLine(line);
        }
    }

    private void AppendLine(string line)
    {
        Lines.Add(line);
        while (Lines.Count > MaxLines)
        {
            Lines.RemoveAt(0);
        }
    }

    public void Clear() => Lines.Clear();
}
