namespace Lancir.Core.Models;

/// <summary>
/// A game found on IGDB by name search, used as a general-purpose match source alongside
/// SteamGridDB.
/// </summary>
public sealed class IgdbGameMatch
{
    public required int IgdbId { get; init; }

    public required string Name { get; init; }

    public string? Summary { get; init; }

    public string? CoverImageUrl { get; init; }

    public string? BackgroundImageUrl { get; init; }

    /// <summary>Comma-separated genre list, e.g. "Shooter, Adventure".</summary>
    public string? Genres { get; init; }

    /// <summary>Community rating in [0, 100] (IGDB's total_rating: critic + user blended).</summary>
    public double? CommunityRating { get; init; }

    public DateTime? ReleaseDateUtc { get; init; }
}
