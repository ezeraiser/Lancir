namespace Lancir.Core.Models;

/// <summary>
/// A candidate cover/grid image returned by SteamGridDB for a matched game.
/// </summary>
public sealed class GridImage
{
    public required GridImageType Type { get; init; }

    public required string Url { get; init; }

    public string? ThumbnailUrl { get; init; }
}
