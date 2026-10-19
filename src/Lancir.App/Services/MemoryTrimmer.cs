using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Lancir.Core.Services;

namespace Lancir.App.Services;

/// <summary>What one memory clean-up achieved (working set in bytes).</summary>
public sealed record TrimResult(long Before, long After);

/// <summary>
/// Keeps the app's memory in check, driven by Settings (read live, so changes apply at once):
/// <list type="bullet">
/// <item>Once the working set passes the chosen limit (default 300 MB), memory is cleaned: the picture cache is emptied, a full
/// garbage collection runs and unused pages are given back to Windows.</item>
/// <item>The check itself is one cheap number read every 30 seconds. If a clean-up cannot get below the limit (the app simply
/// needs that much), the next one waits longer and longer, up to 30 minutes, instead of collecting garbage over and over.</item>
/// <item>Optionally, a clean-up also runs shortly after the window goes to the background (off by default: trimming a working
/// set makes Windows page it back in on next use, which costs CPU).</item>
/// </list>
/// </summary>
public sealed class MemoryTrimmer
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan BackgroundDelay = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan MinCooldown = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan MaxCooldown = TimeSpan.FromMinutes(30);

    private readonly SettingsService _settings;
    private readonly DispatcherTimer _checkTimer;
    private readonly DispatcherTimer _backgroundTimer;
    private DateTime _nextAllowedUtc = DateTime.MinValue;
    private TimeSpan _cooldown = MinCooldown;
    private bool _trimming;

    public MemoryTrimmer(Window window, SettingsService settings)
    {
        _settings = settings;

        _checkTimer = new DispatcherTimer { Interval = CheckInterval };
        _checkTimer.Tick += (_, _) => CheckThreshold();

        _backgroundTimer = new DispatcherTimer { Interval = BackgroundDelay };
        _backgroundTimer.Tick += (_, _) =>
        {
            _backgroundTimer.Stop();
            _ = TrimInBackgroundAsync();
        };

        // Asleep, the app does nothing on its own: no memory checks (sleep mode trims once when it starts).
        BackgroundMode.Changed += asleep =>
        {
            if (asleep)
            {
                _checkTimer.Stop();
                _backgroundTimer.Stop();
            }
            else
            {
                _checkTimer.Start();
            }
        };

        window.Deactivated += (_, _) => ScheduleBackgroundTrim();
        window.StateChanged += (_, _) =>
        {
            if (window.WindowState == WindowState.Minimized)
            {
                ScheduleBackgroundTrim();
            }
        };
        window.Activated += (_, _) => _backgroundTimer.Stop(); // back in use: do not trim a moment after the user returned

        // The first check comes a little after startup, when loading is done.
        window.Loaded += (_, _) =>
        {
            ApplyCacheBudget(_settings.Load());
            _checkTimer.Start();
        };
    }

    /// <summary>The working set of this process (what Task Manager's Memory column is close to), in bytes.</summary>
    public static long CurrentBytes => Environment.WorkingSet;

    private void CheckThreshold()
    {
        var settings = _settings.Load();
        ApplyCacheBudget(settings);
        if (!settings.MemoryTrimEnabled || _trimming || DateTime.UtcNow < _nextAllowedUtc)
        {
            return;
        }

        if (CurrentBytes < LimitBytes(settings))
        {
            _cooldown = MinCooldown; // below the limit again: the next time it is exceeded, react quickly
            return;
        }

        _ = TrimThenBackOffAsync(settings);
    }

    private async Task TrimThenBackOffAsync(AppSettings settings)
    {
        var result = await TrimInBackgroundAsync();
        if (result is null)
        {
            return;
        }

        // Still over the limit after cleaning: the app really uses that much. Wait longer before trying again.
        if (result.After >= LimitBytes(settings))
        {
            _cooldown = TimeSpan.FromTicks(Math.Min(_cooldown.Ticks * 2, MaxCooldown.Ticks));
        }
        else
        {
            _cooldown = MinCooldown;
        }

        _nextAllowedUtc = DateTime.UtcNow + _cooldown;
    }

    private void ScheduleBackgroundTrim()
    {
        var settings = _settings.Load();
        if (!settings.MemoryTrimEnabled || !settings.MemoryTrimOnBackground || settings.BackgroundSleepEnabled)
        {
            return; // with sleep mode on, falling asleep does the trimming
        }

        _backgroundTimer.Stop();
        _backgroundTimer.Start();
    }

    private async Task<TrimResult?> TrimInBackgroundAsync()
    {
        if (_trimming)
        {
            return null;
        }

        _trimming = true;
        try
        {
            return await Task.Run(Trim);
        }
        finally
        {
            _trimming = false;
        }
    }

    /// <summary>The picture cache follows the memory limit: 256 MB at most, but never more than 60% of the limit (so it cannot trigger the clean-up by itself).</summary>
    private static void ApplyCacheBudget(AppSettings settings) =>
        ImageLoader.BudgetBytes = settings.MemoryTrimEnabled
            ? Math.Min(ImageLoader.MaxBudgetBytes, LimitBytes(settings) * 6 / 10)
            : ImageLoader.MaxBudgetBytes;

    private static long LimitBytes(AppSettings settings) => Math.Max(50, settings.MemoryTrimThresholdMb) * 1024L * 1024;

    /// <summary>
    /// Cleans memory now: empties the picture cache, runs one full compacting garbage collection and gives unused pages back
    /// to Windows. Safe to call from any thread (the Settings page's "Şimdi temizle" uses it).
    /// </summary>
    public static TrimResult Trim()
    {
        var before = CurrentBytes;
        ImageLoader.Clear(); // only a speed-up: pictures on screen stay alive through their controls
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        EmptyWorkingSet(Process.GetCurrentProcess().Handle);
        return new TrimResult(before, CurrentBytes);
    }

    [DllImport("psapi.dll")]
    private static extern bool EmptyWorkingSet(IntPtr process);
}
