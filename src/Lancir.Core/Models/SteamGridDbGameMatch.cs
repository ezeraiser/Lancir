namespace Lancir.Core.Models;

/// <summary>A game found on SteamGridDB by name search.</summary>
public sealed class SteamGridDbGameMatch
{
    public required int GridDbGameId { get; init; }

    public required string Name { get; init; }

    public string? CoverImageUrl { get; init; }

    public string? BackgroundImageUrl { get; init; }

    public DateTime? ReleaseDateUtc { get; init; }
}
