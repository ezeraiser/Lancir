using System.Diagnostics;
using Lancir.Core.Data;
using Lancir.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lancir.Core.Services;

/// <summary>Raised when a play session ends and has been saved.</summary>
public sealed record SessionInfo(int GameId, DateTime EndedAtUtc, int PlayCount, long DurationSeconds);

/// <summary>
/// Counts playtime for every library game by watching for its executable in the running process
/// list, so time is recorded no matter how the game was started (from Lancir, a shortcut, Steam...).
/// Elapsed time is flushed to the database periodically; when the game exits the session is
/// recorded (start, duration) and the game's play count and last-played time are updated.
/// </summary>
public sealed class PlaytimeTracker : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    /// <summary>While the app sleeps (window hidden or minimized) nothing is checked more often than this, a running game included.</summary>
    private static readonly TimeSpan SleepingInterval = TimeSpan.FromSeconds(30);

    private volatile bool _powerSaving;
    private int _pollNow;
    private readonly SemaphoreSlim _wake = new(0, 1);
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan GamesCacheLifetime = TimeSpan.FromSeconds(30);

    // Asleep, nobody can edit a game (the window is hidden), so the list is read from the database once and kept: a check then
    // costs one look at the process list and nothing else.
    private static readonly TimeSpan SleepingGamesCacheLifetime = TimeSpan.FromMinutes(15);
    private const long MinSessionSeconds = 20;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DebugLogService _log;
    private readonly Dictionary<int, Session> _sessions = [];
    private readonly HashSet<int> _running = [];
    private readonly object _runningLock = new();
    private CancellationTokenSource? _cts;

    public PlaytimeTracker(IServiceScopeFactory scopeFactory, DebugLogService log, SettingsService settingsService)
    {
        _scopeFactory = scopeFactory;
        _log = log;
        _settingsService = settingsService;
    }

    public const int MinCheckIntervalSeconds = 5;

    public const int MaxCheckIntervalSeconds = 600;

    // The loop sleeps in slices of at most this, so a changed setting takes effect within half a minute.
    private static readonly TimeSpan MaxSleep = TimeSpan.FromSeconds(30);

    private readonly SettingsService _settingsService;

    /// <summary>How often to look for a newly started game while nothing is running (Settings, 5-600 seconds; at least 30 while sleeping).</summary>
    private TimeSpan IdleInterval
    {
        get
        {
            var interval = TimeSpan.FromSeconds(
                Math.Clamp(_settingsService.Load().PlaytimeCheckIntervalSeconds, MinCheckIntervalSeconds, MaxCheckIntervalSeconds));
            return _powerSaving && interval < SleepingInterval ? SleepingInterval : interval;
        }
    }

    /// <summary>How often a running game is looked at: every few seconds, or every 30 while the app sleeps.</summary>
    private TimeSpan ActiveInterval => _powerSaving ? SleepingInterval : PollInterval;

    /// <summary>
    /// True while the app sleeps (its window is hidden or minimized): the checks are spread out. Switching it off checks at once, so
    /// what the window shows is up to date the moment it opens.
    /// </summary>
    public bool PowerSaving
    {
        get => _powerSaving;
        set
        {
            if (_powerSaving == value)
            {
                return;
            }

            _powerSaving = value;
            _gamesCacheExpiresUtc = DateTime.MinValue; // read the list again at the next check, whichever way the mode changed
            if (!value)
            {
                PollNow();
            }
        }
    }

    /// <summary>Asks for a check right away instead of waiting out the interval.</summary>
    public void PollNow()
    {
        Volatile.Write(ref _pollNow, 1);
        try
        {
            _wake.Release();
        }
        catch (SemaphoreFullException)
        {
            // a wake-up is already waiting
        }
    }

    /// <summary>Raised after a game's persisted playtime changed: (gameId, total seconds).</summary>
    public event Action<int, long>? PlaytimeChanged;

    /// <summary>Raised when a game's executable starts (true) or stops (false) running.</summary>
    public event Action<int, bool>? RunningChanged;

    /// <summary>Raised after a finished session has been saved.</summary>
    public event Action<SessionInfo>? SessionCompleted;

    /// <summary>True while any game of the library is running (so background work can wait for the games).</summary>
    public bool AnyRunning
    {
        get
        {
            lock (_runningLock)
            {
                return _running.Count > 0;
            }
        }
    }

    public bool IsRunning(int gameId)
    {
        lock (_runningLock)
        {
            return _running.Contains(gameId);
        }
    }

    public void Start()
    {
        if (_cts is not null)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _ = Task.Run(() => RunAsync(_cts.Token));
    }

    /// <summary>
    /// While a game is running it is checked every few seconds (so its end is noticed promptly); while nothing runs the
    /// check is only as frequent as the Settings interval, since all it can find is a game that just started.
    /// </summary>
    private async Task RunAsync(CancellationToken token)
    {
        var lastPoll = DateTime.MinValue; // the first poll happens at once: a game may already be running at startup
        try
        {
            while (!token.IsCancellationRequested)
            {
                var interval = _sessions.Count > 0 ? ActiveInterval : IdleInterval;
                var wait = lastPoll + interval - DateTime.UtcNow;
                if (Interlocked.Exchange(ref _pollNow, 0) == 1)
                {
                    wait = TimeSpan.Zero;
                }

                if (wait > TimeSpan.Zero)
                {
                    // asleep, the loop wakes only for the next check (a changed setting is noticed then); awake, at least every half minute
                    var cap = _powerSaving ? SleepingInterval * 2 : MaxSleep;
                    await _wake.WaitAsync(wait < cap ? wait : cap, token);
                    continue;
                }

                lastPoll = DateTime.UtcNow;
                try
                {
                    await PollAsync();
                }
                catch (Exception ex)
                {
                    _log.Log($"Oynama süresi takibi: HATA - {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private sealed record TrackedGame(int Id, string? ExecutablePath, IReadOnlyList<string> TrackedNames);

    private IReadOnlyList<TrackedGame>? _gamesCache;
    private DateTime _gamesCacheExpiresUtc;

    /// <summary>
    /// The games to watch. The list only changes when the user edits a game, so it is read from the database at most
    /// every <see cref="GamesCacheLifetime"/> instead of on every poll (a new database context per 5 seconds, all day).
    /// </summary>
    private async Task<IReadOnlyList<TrackedGame>> GetTrackedGamesAsync()
    {
        if (_gamesCache is not null && DateTime.UtcNow < _gamesCacheExpiresUtc)
        {
            return _gamesCache;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        var rows = await db.Games
            .Where(g => g.ExecutablePath != null || g.TrackedProcessName != null)
            .Select(g => new { g.Id, g.ExecutablePath, g.TrackedProcessName })
            .ToListAsync();

        _gamesCache = rows
            .Select(g => new TrackedGame(g.Id, g.ExecutablePath, ParseProcessNames(g.TrackedProcessName)))
            .Where(g => g.TrackedNames.Count > 0 || g.ExecutablePath is not null)
            .ToList();
        _gamesCacheExpiresUtc = DateTime.UtcNow + (_powerSaving ? SleepingGamesCacheLifetime : GamesCacheLifetime);
        return _gamesCache;
    }

    private async Task PollAsync()
    {
        var games = await GetTrackedGamesAsync();
        if (games.Count == 0 && _sessions.Count == 0)
        {
            return; // nothing to watch: no process listing, no database
        }

        // One listing of the running processes serves every game (looking each game's name up separately
        // listed all processes once per game, every poll).
        var processes = SnapshotProcesses();

        // Games with a tracked process name (launcher -> real game exe) are matched by that process name;
        // all others by the full path of their executable.
        var running = GetRunningExecutablePaths(games.Where(g => g.TrackedNames.Count == 0).Select(g => g.ExecutablePath!), processes);
        var runningNames = GetRunningProcessNames(games.SelectMany(g => g.TrackedNames), processes);
        var now = DateTime.UtcNow;
        var flushes = new List<(int Id, long Seconds)>();
        var ended = new List<(int Id, Session Session)>();
        var started = new List<int>();

        foreach (var game in games)
        {
            var isRunning = game.TrackedNames.Count > 0
                ? game.TrackedNames.Any(runningNames.Contains)
                : running.Contains(Path.GetFullPath(game.ExecutablePath!));

            if (_sessions.TryGetValue(game.Id, out var session))
            {
                // Count up to this poll even when the game just exited, so the last interval isn't lost.
                var elapsed = (long)(now - session.LastTick).TotalSeconds;

                // A game that is gone ended somewhere between the previous check and this one. With checks a few seconds apart it does not
                // matter; with checks 30 seconds apart (sleep mode) counting the whole gap would overstate the session by 15 seconds on
                // average, so half of it is counted.
                if (!isRunning && elapsed > 10)
                {
                    elapsed /= 2;
                }

                session.Pending += elapsed;
                session.TotalSeconds += elapsed;
                session.LastTick = now;

                if (!isRunning || now - session.LastFlush >= FlushInterval)
                {
                    if (session.Pending > 0)
                    {
                        flushes.Add((game.Id, session.Pending));
                        session.Pending = 0;
                    }

                    session.LastFlush = now;
                }

                if (!isRunning)
                {
                    _sessions.Remove(game.Id);
                    ended.Add((game.Id, session));
                }
            }
            else if (isRunning)
            {
                // The poll may have noticed the game some time after it started (up to the idle interval): credit that
                // time from the process's own start time, so a long interval does not lose the beginning of a session.
                var credit = EstimateCreditSeconds(game, processes, now);
                _sessions[game.Id] = new Session
                {
                    StartedAtUtc = now - TimeSpan.FromSeconds(credit),
                    LastTick = now,
                    LastFlush = now,
                    Pending = credit,
                    TotalSeconds = credit,
                };
                started.Add(game.Id);
            }
        }

        foreach (var id in started)
        {
            lock (_runningLock)
            {
                _running.Add(id);
            }

            RunningChanged?.Invoke(id, true);
        }

        if (flushes.Count == 0 && ended.Count == 0)
        {
            return; // the usual poll: nothing to write, so no database context is created at all
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();

        foreach (var (id, seconds) in flushes)
        {
            var entity = await db.Games.FindAsync(id);
            if (entity is null)
            {
                continue;
            }

            entity.PlaytimeSeconds += seconds;
            await db.SaveChangesAsync();
            PlaytimeChanged?.Invoke(id, entity.PlaytimeSeconds);
        }

        foreach (var (id, session) in ended)
        {
            lock (_runningLock)
            {
                _running.Remove(id);
            }

            var entity = await db.Games.FindAsync(id);
            if (entity is not null && session.TotalSeconds >= MinSessionSeconds)
            {
                entity.PlayCount++;
                entity.LastPlayedUtc = now;
                db.PlaySessions.Add(new PlaySession
                {
                    GameId = id,
                    StartedAtUtc = session.StartedAtUtc,
                    DurationSeconds = session.TotalSeconds,
                });
                await db.SaveChangesAsync();
                SessionCompleted?.Invoke(new SessionInfo(id, now, entity.PlayCount, session.TotalSeconds));
            }

            RunningChanged?.Invoke(id, false);
        }
    }

    /// <summary>
    /// Seconds a just-noticed game has already been running, from its processes' start times, capped at what a poll could
    /// have missed (idle interval plus one active interval) so a game that was already open when Lancir started is not
    /// credited with hours it ran before Lancir was watching.
    /// </summary>
    private long EstimateCreditSeconds(TrackedGame game, Dictionary<string, List<int>> processes, DateTime now)
    {
        var maxCredit = (long)(IdleInterval + ActiveInterval).TotalSeconds;
        IEnumerable<string> names = game.TrackedNames.Count > 0
            ? game.TrackedNames
            : [Path.GetFileNameWithoutExtension(game.ExecutablePath!)];

        DateTime? earliest = null;
        foreach (var name in names)
        {
            if (!processes.TryGetValue(name, out var ids))
            {
                continue;
            }

            foreach (var id in ids)
            {
                try
                {
                    using var process = Process.GetProcessById(id);
                    var start = process.StartTime.ToUniversalTime();
                    if (earliest is null || start < earliest)
                    {
                        earliest = start;
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException)
                {
                    // exited meanwhile, or not allowed to look (elevated process): no credit from this one
                }
            }
        }

        return earliest is { } startUtc ? Math.Clamp((long)(now - startUtc).TotalSeconds, 0, maxCredit) : 0;
    }

    /// <summary>Process ids of everything running, grouped by process name (case-insensitive), from a single listing.</summary>
    private static Dictionary<string, List<int>> SnapshotProcesses()
    {
        var map = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (!map.TryGetValue(process.ProcessName, out var ids))
                {
                    map[process.ProcessName] = ids = [];
                }

                ids.Add(process.Id);
            }
            finally
            {
                process.Dispose();
            }
        }

        return map;
    }

    /// <summary>
    /// The full paths of running processes whose name matches one of the library's executables. Only those few
    /// processes are asked for their path; reading the module list of every process on the machine every few
    /// seconds once made the app's memory balloon.
    /// </summary>
    private static HashSet<string> GetRunningExecutablePaths(IEnumerable<string> executablePaths, Dictionary<string, List<int>> processes)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = executablePaths
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .Where(name => !string.IsNullOrEmpty(name))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var name in names)
        {
            if (!processes.TryGetValue(name, out var ids))
            {
                continue;
            }

            foreach (var id in ids)
            {
                var path = QueryImagePath(id);
                if (!string.IsNullOrEmpty(path))
                {
                    paths.Add(path);
                }
            }
        }

        return paths;
    }

    /// <summary>
    /// Splits a list of process names (separated by commas, semicolons or new lines) into distinct names without
    /// ".exe": "Game-Win64-Shipping.exe, Helper" gives "Game-Win64-Shipping" and "Helper".
    /// </summary>
    public static IReadOnlyList<string> ParseProcessNames(string? names)
    {
        if (string.IsNullOrWhiteSpace(names))
        {
            return [];
        }

        var result = new List<string>();
        foreach (var part in names.Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var name = part.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? part[..^4].TrimEnd() : part;
            if (name.Length > 0 && !result.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(name);
            }
        }

        return result;
    }

    /// <summary>The subset of <paramref name="names"/> that has at least one running process.</summary>
    private static HashSet<string> GetRunningProcessNames(IEnumerable<string> names, Dictionary<string, List<int>> processes) =>
        new(names.Where(processes.ContainsKey), StringComparer.OrdinalIgnoreCase);

    private const uint ProcessQueryLimitedInformation = 0x1000;

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(IntPtr process, int flags, System.Text.StringBuilder path, ref int size);

    private static string? QueryImagePath(int processId)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (handle == IntPtr.Zero)
        {
            return null; // access denied (elevated) or the process already exited
        }

        try
        {
            var buffer = new System.Text.StringBuilder(1024);
            var size = buffer.Capacity;
            return QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString() : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }

    private sealed class Session
    {
        public DateTime StartedAtUtc { get; set; }

        public DateTime LastTick { get; set; }

        public DateTime LastFlush { get; set; }

        public long Pending { get; set; }

        public long TotalSeconds { get; set; }
    }
}
