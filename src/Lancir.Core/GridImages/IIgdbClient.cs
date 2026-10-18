using Lancir.Core.Models;

namespace Lancir.Core.GridImages;

public interface IIgdbClient
{
    /// <summary>
    /// Searches IGDB by game name and returns a cover image URL if a match is found. Returns
    /// null if no credentials are configured, the search fails, or nothing matches.
    /// </summary>
    Task<IgdbCoverResult?> FindCoverAsync(
        string gameName, string clientId, string clientSecret, CancellationToken cancellationToken = default);

    /// <summary>
    /// General-purpose game lookup by name, used as a fallback match source for games not found
    /// on Steam (e.g. unreleased or console-exclusive titles). Returns null if no credentials are
    /// configured, the search fails, or nothing matches.
    /// </summary>
    Task<IgdbGameMatch?> FindGameAsync(
        string gameName, string clientId, string clientSecret, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches IGDB by name and returns up to <paramref name="limit"/> candidate matches, so the
    /// caller can let the user pick the right one instead of blindly taking the first result
    /// (IGDB's search can return an unrelated game as the top hit for an ambiguous name).
    /// </summary>
    Task<IReadOnlyList<IgdbGameMatch>> SearchGamesAsync(
        string gameName, string clientId, string clientSecret, int limit = 5, CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up a game by name on IGDB and returns its id and "updated_at" timestamp, marking
    /// whether it is newer than <paramref name="previousUpdatedAtUtc"/>. Returns null if no
    /// credentials are configured, the search fails, or nothing matches.
    /// </summary>
    Task<IgdbUpdateCheckResult?> CheckForUpdateAsync(
        string gameName,
        string clientId,
        string clientSecret,
        DateTime? previousUpdatedAtUtc,
        CancellationToken cancellationToken = default);
}
