namespace Lancir.Core.Models;

/// <summary>
/// Additional store metadata that comes with a match (currently from Steam): shown in the details
/// panel but not part of the old-vs-new comparison, so it is applied whenever a match is applied.
/// </summary>
public sealed class GameExtraDetails
{
    /// <summary>The store description including media lines (see LibraryGame.DescriptionRich).</summary>
    public string? RichDescription { get; init; }

    public string? Developers { get; init; }

    public string? Publishers { get; init; }

    /// <summary>Comma-separated play modes, e.g. "Tek oyunculu, Co-op".</summary>
    public string? Modes { get; init; }

    /// <summary>Comma-separated supported interface languages.</summary>
    public string? Languages { get; init; }

    /// <summary>e.g. "PEGI 18".</summary>
    public string? AgeRating { get; init; }

    public string? Website { get; init; }

    public IReadOnlyList<string> ScreenshotUrls { get; init; } = [];

    public string? TrailerUrl { get; init; }

    public string? TrailerThumbnailUrl { get; init; }
}
