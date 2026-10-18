namespace Lancir.Core.Models;

/// <summary>
/// A candidate game discovered by scanning a watched folder, not yet confirmed by the user.
/// </summary>
public sealed class GameEntry
{
    public required string FolderPath { get; init; }

    public required string RawFolderName { get; init; }

    public required string CleanedName { get; init; }

    /// <summary>All plausible "main" executables found in the folder, excluding ignored ones.</summary>
    public IReadOnlyList<string> ExecutableCandidates { get; init; } = [];

    /// <summary>The executable the scanner considers most likely to be the right one.</summary>
    public string? SuggestedExecutablePath =>
        ExecutableCandidates.Count > 0 ? ExecutableCandidates[0] : null;
}
