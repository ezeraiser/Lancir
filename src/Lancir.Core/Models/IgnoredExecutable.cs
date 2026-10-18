namespace Lancir.Core.Models;

/// <summary>
/// An executable the user has explicitly marked as "not the game" during scan review,
/// so it stops being suggested as a launch candidate in future scans.
/// </summary>
public sealed class IgnoredExecutable
{
    public int Id { get; set; }

    public required string ExecutablePath { get; set; }
}
