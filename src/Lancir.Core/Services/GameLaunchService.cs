using System.ComponentModel;
using System.Diagnostics;
using Lancir.Core.Models;

namespace Lancir.Core.Services;

public sealed class GameLaunchService
{
    private const int ErrorCancelled = 1223; // the user declined the UAC prompt

    public bool CanLaunch(LibraryGame game) =>
        BuildLaunchUri(game) is not null || (!string.IsNullOrWhiteSpace(game.ExecutablePath) && File.Exists(game.ExecutablePath));

    public static bool UsesSteam(LibraryGame game) => game.LaunchViaSteam && game.SteamAppId is > 0;

    /// <summary>
    /// True when <paramref name="value"/> looks like a launch address ("scheme:..."): a custom protocol such as
    /// steam://, com.epicgames.launcher:// or shell:AppsFolder\..., but not a file path ("C:\...") or file: URL.
    /// </summary>
    public static bool IsValidLaunchUri(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();
        var colon = text.IndexOf(':');
        if (colon < 2)
        {
            return false; // no scheme, or a single letter = drive path
        }

        var scheme = text[..colon];
        if (!char.IsLetter(scheme[0]) || !scheme.All(c => char.IsLetterOrDigit(c) || c is '+' or '-' or '.'))
        {
            return false;
        }

        return !scheme.Equals("file", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The address that starts the game instead of its executable, or null when it is started by exe: the custom
    /// <see cref="LibraryGame.LaunchUri"/>, else the Steam protocol (with the launch arguments passed through).
    /// </summary>
    public static string? BuildLaunchUri(LibraryGame game)
    {
        if (IsValidLaunchUri(game.LaunchUri))
        {
            return game.LaunchUri!.Trim();
        }

        if (!UsesSteam(game))
        {
            return null;
        }

        // steam://run/<id>//<arguments>/ starts the game with those arguments; the plain form has none.
        return string.IsNullOrWhiteSpace(game.LaunchArguments)
            ? $"steam://rungameid/{game.SteamAppId}"
            : $"steam://run/{game.SteamAppId}//{Uri.EscapeDataString(game.LaunchArguments.Trim())}/";
    }

    /// <summary>
    /// True when the game is started by address or has no exe, yet nothing tells the playtime tracker what
    /// to watch (no exe path and no process name), so its playtime would silently not be counted.
    /// </summary>
    public static bool LacksPlaytimeTracking(bool startedByAddress, string? executablePath, string? trackedProcessName) =>
        startedByAddress && string.IsNullOrWhiteSpace(executablePath) && string.IsNullOrWhiteSpace(trackedProcessName);

    public void Launch(LibraryGame game)
    {
        if (!CanLaunch(game))
        {
            throw new InvalidOperationException(
                $"No launchable executable found for '{game.DisplayName}'.");
        }

        var uri = BuildLaunchUri(game);
        if (uri is not null)
        {
            // Windows hands the address to the program registered for its protocol (Steam, Epic, ...).
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = game.ExecutablePath,
            WorkingDirectory = !string.IsNullOrWhiteSpace(game.WorkingDirectory) && Directory.Exists(game.WorkingDirectory)
                ? game.WorkingDirectory
                : Path.GetDirectoryName(game.ExecutablePath),
            UseShellExecute = true,
        };

        if (!string.IsNullOrWhiteSpace(game.LaunchArguments))
        {
            startInfo.Arguments = game.LaunchArguments;
        }

        if (game.RunAsAdmin)
        {
            startInfo.Verb = "runas";
        }

        try
        {
            Process.Start(startInfo);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            // The user declined the administrator prompt: nothing to report.
        }
    }
}
