using System.ComponentModel;
using System.Windows;
using Lancir.Core.Services;

namespace Lancir.App.Services;

/// <summary>
/// How the main window behaves around minimizing, closing and starting, driven by Settings (read live, so a changed option
/// applies at once): minimize to the notification area instead of the taskbar, close (X) to the notification area, and start
/// minimized. The notification-area icon exists only while one of the first two is on.
/// </summary>
public sealed class WindowBehavior : IDisposable
{
    private readonly Window _window;
    private readonly SettingsService _settings;
    private TrayIcon? _tray;
    private WindowState _lastShownState;

    public WindowBehavior(Window window, SettingsService settings)
    {
        _window = window;
        _settings = settings;
        _lastShownState = window.WindowState == WindowState.Minimized ? WindowState.Normal : window.WindowState;
        Current = this;

        window.StateChanged += (_, _) => OnStateChanged();
        Application.Current.SessionEnding += (_, _) => IsExiting = true; // never block Windows from signing out or shutting down
        Refresh();
    }

    /// <summary>The behavior of the running main window (so the Settings page and a second launch can reach it).</summary>
    public static WindowBehavior? Current { get; private set; }

    /// <summary>True once the app is really quitting, so closing the window is no longer turned into hiding it.</summary>
    public static bool IsExiting { get; private set; }

    /// <summary>Applies the current settings: shows or hides the notification-area icon.</summary>
    public void Refresh()
    {
        var settings = _settings.Load();
        var wantTray = settings.MinimizeToTray || settings.CloseToTray;
        if (wantTray && _tray is null)
        {
            _tray = new TrayIcon("Lancir", Loc.T("Lancir'i aç"), Loc.T("Çıkış"));
            _tray.Activated += Restore;
            _tray.ExitRequested += Exit;
        }

        if (_tray is not null)
        {
            _tray.IsVisible = wantTray;
        }
    }

    /// <summary>Applies "start minimized" after the window was first shown.</summary>
    public void ApplyStartState()
    {
        var settings = _settings.Load();
        var minimized = settings.StartMinimized || (App.LaunchedByWindowsStartup && settings.StartMinimizedAtWindowsStartup);
        if (!minimized)
        {
            return;
        }

        _window.WindowState = WindowState.Minimized; // StateChanged moves it to the notification area if that is on
        if (_settings.Load().MinimizeToTray)
        {
            HideToTray();
        }
    }

    /// <summary>Brings the window back from the taskbar or the notification area and to the front.</summary>
    public void Restore()
    {
        if (!_window.IsVisible)
        {
            _window.Show();
        }

        _window.WindowState = _lastShownState;
        _window.Activate();
    }

    /// <summary>Called from the window's Closing: with "close to tray" on, hides the window instead of quitting.</summary>
    public void HandleClosing(CancelEventArgs e)
    {
        if (IsExiting || !_settings.Load().CloseToTray)
        {
            return;
        }

        e.Cancel = true;
        HideToTray();
    }

    public void Exit()
    {
        IsExiting = true;
        Application.Current.Shutdown();
    }

    public void Dispose()
    {
        _tray?.Dispose();
        _tray = null;
    }

    private void OnStateChanged()
    {
        if (_window.WindowState != WindowState.Minimized)
        {
            _lastShownState = _window.WindowState;
        }
        else if (_settings.Load().MinimizeToTray)
        {
            HideToTray();
        }
    }

    private void HideToTray()
    {
        if (_tray is null)
        {
            Refresh();
        }

        _window.Hide(); // hidden windows leave the taskbar too
    }
}
