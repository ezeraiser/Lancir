using System.IO;
using Microsoft.Win32;

namespace Lancir.App.Services;

/// <summary>
/// "Start with Windows": a per-user entry under HKCU\...\CurrentVersion\Run, so it needs no administrator rights and
/// disappears with the user. The same entry appears in Task Manager's Startup apps, where Windows lets the user disable it too.
/// </summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Lancir";

    /// <summary>The command line argument that marks a launch by Windows at sign-in (as opposed to the user opening the app).</summary>
    public const string StartupArgument = "--startup";

    /// <summary>The command Windows runs at sign-in: this exe, quoted, with <see cref="StartupArgument"/>.</summary>
    private static string? Command => Environment.ProcessPath is { } path ? $"\"{path}\" {StartupArgument}" : null;

    /// <summary>True when an entry for Lancir exists.</summary>
    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(ValueName) is string;
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                return false;
            }
        }
    }

    /// <summary>Adds or removes the entry. Returns false when the registry could not be written.</summary>
    public static bool Apply(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (enable)
            {
                if (Command is not { } command)
                {
                    return false;
                }

                key.SetValue(ValueName, command, RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    /// <summary>If the entry exists but points somewhere else (the app was moved or updated into another folder), points it here.</summary>
    public static void RepairPath()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key?.GetValue(ValueName) is string current && Command is { } command &&
                !string.Equals(current, command, StringComparison.OrdinalIgnoreCase))
            {
                key.SetValue(ValueName, command, RegistryValueKind.String);
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // not important enough to report
        }
    }
}
