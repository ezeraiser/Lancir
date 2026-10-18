namespace Lancir.Core.Models;

/// <summary>
/// Result of checking a game's IGDB listing for changes since the last known check.
/// </summary>
public sealed class IgdbUpdateCheckResult
{
    public required int IgdbId { get; init; }

    public required string Name { get; init; }

    public required DateTime UpdatedAtUtc { get; init; }

    /// <summary>True when <see cref="UpdatedAtUtc"/> is newer than the previously stored value.</summary>
    public required bool HasChanged { get; init; }
}
