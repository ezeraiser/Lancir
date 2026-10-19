using System.IO;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Lancir.Core.Services;

public enum MatchJobStatus
{
    /// <summary>No match-all job has run yet.</summary>
    None,

    Running,

    Completed,

    /// <summary>Stopped by the user.</summary>
    Cancelled,

    /// <summary>The app was closed while it ran.</summary>
    Interrupted,
}

/// <summary>A game for the match-all job: its id and the name it is looked up by.</summary>
public sealed record MatchJobGame(int Id, string Name);

/// <summary>Matches one library game with the online databases; true when a match was found and applied.</summary>
public interface IGameMatcher
{
    Task<bool> MatchAsync(int gameId, CancellationToken cancellationToken);
}

/// <summary>Matches a game in a scope of its own (its own database context), so the job never shares a context with the library page.</summary>
public sealed class ScopedGameMatcher(IServiceScopeFactory scopes) : IGameMatcher
{
    public async Task<bool> MatchAsync(int gameId, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<LibraryService>().MatchGameAsync(gameId, cancellationToken);
    }
}

/// <summary>What Settings shows about the match-all job.</summary>
public sealed record MatchJobSnapshot(
    MatchJobStatus Status, DateTime? StartedUtc, DateTime? FinishedUtc, int Total, int Processed, int Matched, int NotFound, int Errors,
    int Skipped, string? CurrentGame)
{
    public bool IsRunning => Status == MatchJobStatus.Running;
}

/// <summary>
/// "Match all" as a job of its own: it runs in the background one game after the other, can be followed (progress, counts) and stopped,
/// and only one runs at a time (a second one has to wait for the first to finish or be cancelled, so the same games are never matched
/// by two loops at once). The time each game was last tried is kept on disk, so a new job can skip the games that were tried within the
/// last hour (<see cref="RecentWindow"/>) instead of asking the online services about the same games again. The state survives a
/// restart: a job that was running when the app closed shows as interrupted.
/// </summary>
public sealed class MatchAllJob
{
    public static readonly TimeSpan RecentWindow = TimeSpan.FromHours(1);
    private static readonly TimeSpan KeepAttempts = TimeSpan.FromDays(1);

    private sealed class State
    {
        public MatchJobStatus Status { get; set; }

        public DateTime? StartedUtc { get; set; }

        public DateTime? FinishedUtc { get; set; }

        public int Total { get; set; }

        public int Processed { get; set; }

        public int Matched { get; set; }

        public int NotFound { get; set; }

        public int Errors { get; set; }

        public int Skipped { get; set; }

        /// <summary>When each game was last tried by a match-all job (game id to time).</summary>
        public Dictionary<int, DateTime> Attempts { get; set; } = [];
    }

    private readonly IGameMatcher _matcher;
    private readonly DebugLogService? _log;
    private readonly string _path;
    private readonly Func<DateTime> _now;
    private readonly object _gate = new();
    private readonly object _saveGate = new(); // saves are written one at a time and in the order they were made
    private State _state = new();
    private string? _current;
    private CancellationTokenSource? _cts;
    private Task? _task;

    public MatchAllJob(IGameMatcher matcher, DebugLogService? log, string? stateFile = null, Func<DateTime>? utcNow = null)
    {
        _matcher = matcher;
        _log = log;
        _path = stateFile ?? Path.Combine(AppPaths.AppDataFolder, "cache", "match-all-job.json");
        _now = utcNow ?? (() => DateTime.UtcNow);
        Load();
    }

    /// <summary>Raised (on the job's thread) after the progress changed.</summary>
    public event Action? Changed;

    /// <summary>Raised (on the job's thread) when the job starts on a game.</summary>
    public event Action<int>? GameStarted;

    /// <summary>Raised (on the job's thread) when the job is done with a game: its id and whether a match was found and applied.</summary>
    public event Action<int, bool>? GameFinished;

    public MatchJobSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                return new MatchJobSnapshot(
                    _state.Status, _state.StartedUtc, _state.FinishedUtc, _state.Total, _state.Processed, _state.Matched, _state.NotFound,
                    _state.Errors, _state.Skipped, _current);
            }
        }
    }

    public bool IsRunning => Snapshot.IsRunning;

    /// <summary>How many of the given games were tried by a match-all job within the last hour.</summary>
    public int CountRecentlyTried(IEnumerable<int> gameIds)
    {
        lock (_gate)
        {
            var limit = _now() - RecentWindow;
            return gameIds.Count(id => _state.Attempts.TryGetValue(id, out var at) && at > limit);
        }
    }

    /// <summary>
    /// Starts the job on <paramref name="games"/>. With <paramref name="skipRecent"/> the games tried within the last hour are left out
    /// ("continue where it stopped"); without it all of them are matched ("start over"). Returns false, and does nothing, while a job
    /// is already running: that one has to finish or be cancelled first.
    /// </summary>
    public bool TryStart(IReadOnlyList<MatchJobGame> games, bool skipRecent)
    {
        List<MatchJobGame> todo;
        lock (_gate)
        {
            if (_state.Status == MatchJobStatus.Running)
            {
                return false;
            }

            var limit = _now() - RecentWindow;
            todo = skipRecent
                ? games.Where(g => !(_state.Attempts.TryGetValue(g.Id, out var at) && at > limit)).ToList()
                : games.ToList();
            _state.Status = MatchJobStatus.Running;
            _state.StartedUtc = _now();
            _state.FinishedUtc = null;
            _state.Total = todo.Count;
            _state.Processed = _state.Matched = _state.NotFound = _state.Errors = 0;
            _state.Skipped = games.Count - todo.Count;
            _current = null;
            _cts = new CancellationTokenSource();
            _task = Task.Run(() => RunAsync(todo, _cts.Token));
        }

        Save();
        Changed?.Invoke();
        _log?.Log($"Eşleştirme işi başladı: {todo.Count} oyun, {games.Count - todo.Count} oyun atlandı (son 1 saatte denenmiş).");
        return true;
    }

    /// <summary>Stops the running job after the game it is on (that game's lookup is cancelled) and waits until it has ended.</summary>
    public async Task CancelAsync()
    {
        Task? task;
        lock (_gate)
        {
            task = _task;
            _cts?.Cancel();
        }

        if (task is not null)
        {
            await task;
        }
    }

    private async Task RunAsync(List<MatchJobGame> games, CancellationToken cancellationToken)
    {
        var cancelled = false;
        try
        {
            foreach (var game in games)
            {
                cancellationToken.ThrowIfCancellationRequested();
                lock (_gate)
                {
                    _current = game.Name;
                }

                Changed?.Invoke();
                GameStarted?.Invoke(game.Id);

                var found = false;
                var failed = false;
                var done = false;
                try
                {
                    try
                    {
                        found = await _matcher.MatchAsync(game.Id, cancellationToken);
                        done = true;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        failed = done = true;
                        _log?.Log($"Eşleştirme işi: '{game.Name}' için HATA - {ex.Message}");
                    }
                }
                finally
                {
                    if (done)
                    {
                        lock (_gate)
                        {
                            _state.Processed++;
                            if (failed)
                            {
                                _state.Errors++;
                            }
                            else if (found)
                            {
                                _state.Matched++;
                            }
                            else
                            {
                                _state.NotFound++;
                            }

                            _state.Attempts[game.Id] = _now(); // a game cancelled halfway is not counted as tried
                        }

                        Save();
                    }

                    GameFinished?.Invoke(game.Id, found);
                }

                Changed?.Invoke();
            }
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        finally
        {
            lock (_gate)
            {
                _state.Status = cancelled ? MatchJobStatus.Cancelled : MatchJobStatus.Completed;
                _state.FinishedUtc = _now();
                _current = null;
                _cts?.Dispose();
                _cts = null;
            }

            Save();
            Changed?.Invoke();
            var s = Snapshot;
            _log?.Log($"Eşleştirme işi {(cancelled ? "iptal edildi" : "bitti")}: {s.Processed}/{s.Total} oyun işlendi, {s.Matched} eşleşti, {s.NotFound} bulunamadı, {s.Errors} hata.");
        }
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_path) && JsonSerializer.Deserialize<State>(File.ReadAllText(_path)) is { } loaded)
            {
                _state = loaded;
                if (_state.Status == MatchJobStatus.Running)
                {
                    _state.Status = MatchJobStatus.Interrupted; // nothing can be running right after a start: the app was closed meanwhile
                    Save();
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _state = new State(); // an unreadable file only means the history is lost
        }
    }

    private void Save()
    {
        // The state is turned into text and written to the file inside one lock: two saves made at nearly the same moment (the job and
        // its starter) must not write in the opposite order, or the older state would replace the newer one.
        lock (_saveGate)
        {
            try
            {
                string json;
                lock (_gate)
                {
                    var limit = _now() - KeepAttempts;
                    foreach (var old in _state.Attempts.Where(a => a.Value < limit).Select(a => a.Key).ToList())
                    {
                        _state.Attempts.Remove(old);
                    }

                    json = JsonSerializer.Serialize(_state);
                }

                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                var temp = _path + ".tmp";
                File.WriteAllText(temp, json);
                File.Move(temp, _path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // the state is only a convenience: a failed write must not stop the job
            }
        }
    }
}
