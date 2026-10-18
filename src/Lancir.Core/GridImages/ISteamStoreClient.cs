using Lancir.Core.Models;

namespace Lancir.Core.GridImages;

public sealed record SteamSearchResult(int AppId, string Name);

/// <summary>Store data for one Steam app, as returned by Steam's public storefront API.</summary>
public sealed class SteamAppDetails
{
    public required int AppId { get; init; }

    public required string Name { get; init; }

    public string? ShortDescription { get; init; }

    /// <summary>The store page's "about this game" text as plain text: headings as "# ...", list items as "- ...", paragraphs separated by blank lines.</summary>
    public string? FullDescription { get; init; }

    public string? HeaderImageUrl { get; init; }

    public string? Genres { get; init; }

    public DateTime? ReleaseDateUtc { get; init; }

    public int? MetacriticScore { get; init; }

    public GameExtraDetails Extra { get; init; } = new();
}

/// <summary>
/// Reads Steam's public storefront endpoints and image CDN. None of this needs an API key; it
/// only works for games that are on Steam.
/// </summary>
public interface ISteamStoreClient
{
    /// <summary>Searches the Steam store by name. Returns an empty list on any failure.</summary>
    Task<IReadOnlyList<SteamSearchResult>> SearchAsync(string term, CancellationToken cancellationToken = default);

    /// <summary>Returns the store details of a game, or null if the app is unknown, not a game, or the request fails.</summary>
    Task<SteamAppDetails?> GetDetailsAsync(int appId, CancellationToken cancellationToken = default);

    /// <summary>Finds the first official portrait cover that exists on Steam's CDN; null if none.</summary>
    Task<string?> ResolveCoverUrlAsync(int appId, CancellationToken cancellationToken = default);

    /// <summary>Finds the official wide hero artwork on Steam's CDN; null if none.</summary>
    Task<string?> ResolveHeroUrlAsync(int appId, CancellationToken cancellationToken = default);
}
