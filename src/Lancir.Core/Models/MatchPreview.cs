namespace Lancir.Core.Models;

/// <summary>
/// Full field-by-field data for a chosen match candidate, resolved and ready to compare against
/// the library game's current values (e.g. in a Playnite-style old/new diff screen).
/// </summary>
public sealed class MatchPreview
{
    public required MatchSource Source { get; init; }

    public required int ExternalId { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    /// <summary>Remote URL for the new cover image; downloaded only if the user picks it.</summary>
    public string? CoverImageUrl { get; init; }

    /// <summary>Remote URL for the new background/artwork image; downloaded only if picked.</summary>
    public string? BackgroundImageUrl { get; init; }

    public string? Genres { get; init; }

    public double? CommunityRating { get; init; }

    public DateTime? ReleaseDateUtc { get; init; }

    public GameExtraDetails? Extra { get; init; }
}
