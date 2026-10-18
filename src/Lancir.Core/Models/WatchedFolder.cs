namespace Lancir.Core.Models;

/// <summary>
/// A root folder the user has added; each immediate subfolder is treated as a game candidate.
/// </summary>
public sealed class WatchedFolder
{
    public int Id { get; set; }

    public required string Path { get; set; }
}
