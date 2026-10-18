namespace Lancir.Core.Models;

/// <summary>The launch-related settings of a game, edited together in the details window.</summary>
public sealed record GameLaunchSettings(
    bool LaunchViaSteam,
    string? Arguments,
    string? WorkingDirectory,
    string? TrackedProcessName,
    bool RunAsAdmin = false,
    string? LaunchUri = null)
{
    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
