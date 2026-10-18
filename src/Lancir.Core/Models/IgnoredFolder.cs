namespace Lancir.Core.Models;

/// <summary>
/// A folder the user has explicitly marked as "not a game" during scan review, so it stops
/// being suggested as a candidate in future scans entirely.
/// </summary>
public sealed class IgnoredFolder
{
    public int Id { get; set; }

    public required string FolderPath { get; set; }
}
