using Lancir.Core.Models;

namespace Lancir.Core.GridImages;

public interface ISteamGridDbClient
{
    /// <summary>
    /// Searches SteamGridDB by name and returns candidate games with a cover image each (the
    /// best-scoring "grid" image per candidate). No Steam app id needed - this is SteamGridDB's
    /// own name search. Returns an empty list if nothing matches or the request fails (e.g.
    /// missing/invalid API key).
    /// </summary>
    Task<IReadOnlyList<SteamGridDbGameMatch>> SearchGamesAsync(
        string gameName, string apiKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches candidate cover ("grid") images for a SteamGridDB game id (as returned by
    /// <see cref="SearchGamesAsync"/>). Returns an empty list if none are found or the request
    /// fails.
    /// </summary>
    Task<IReadOnlyList<GridImage>> GetGridImagesAsync(
        int gridDbGameId, string apiKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches candidate "hero" (wide banner/background) images for a SteamGridDB game id. Used
    /// as the library detail panel's background artwork. Returns an empty list if none are found
    /// or the request fails.
    /// </summary>
    Task<IReadOnlyList<GridImage>> GetHeroImagesAsync(
        int gridDbGameId, string apiKey, CancellationToken cancellationToken = default);
}
