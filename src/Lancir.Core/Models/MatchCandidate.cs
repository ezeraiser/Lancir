namespace Lancir.Core.Models;

public enum MatchSource
{
    SteamGridDb,
    Igdb,
    Steam,
}

/// <summary>
/// A single candidate result from a Steam, SteamGridDB or IGDB search, shown to the user so they can
/// pick the right game instead of the app silently applying whatever scored highest.
/// </summary>
public sealed class MatchCandidate
{
    public required MatchSource Source { get; init; }

    /// <summary>Steam app id, SteamGridDB game id or IGDB game id, depending on <see cref="Source"/>.</summary>
    public required int ExternalId { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public string? CoverImageUrl { get; init; }

    public string? BackgroundImageUrl { get; init; }

    /// <summary>Comma-separated genre/tag list, e.g. "Shooter, Adventure".</summary>
    public string? Genres { get; init; }

    /// <summary>Community rating in [0, 100], if the source provides one.</summary>
    public double? CommunityRating { get; init; }

    public DateTime? ReleaseDateUtc { get; init; }

    /// <summary>Store metadata that comes along with the match (Steam only).</summary>
    public GameExtraDetails? Extra { get; init; }
}
