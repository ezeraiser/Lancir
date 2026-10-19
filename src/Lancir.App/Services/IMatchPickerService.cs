using Lancir.Core.Models;

namespace Lancir.App.Services;

/// <summary>
/// Shows the user a picker over a list of match candidates (SteamGridDB/IGDB search results), and a
/// Playnite-style old-vs-new comparison screen to choose which fields to apply. Kept as an
/// interface so LibraryViewModel doesn't directly construct WPF windows.
/// </summary>
public interface IMatchPickerService
{
    MatchCandidate? Pick(string gameDisplayName, IReadOnlyList<MatchCandidate> candidates);

    /// <summary>
    /// Shows the old-vs-new comparison for a resolved match preview against a library game's
    /// current field values. Returns the field choices the user made, or null if they cancelled.
    /// </summary>
    MatchFieldChoices? Compare(LibraryGame currentGame, MatchPreview preview);

    /// <summary>
    /// Shows the details editor (name, description, genres, executable path, plus read-only
    /// background/rating/release date) for a game. Returns true if the user saved changes.
    /// </summary>
    Task<bool> EditDetailsAsync(LibraryGame game);

    /// <summary>
    /// Shows the details editor, empty, to add a game by hand. Returns the entered values, or null
    /// if the user cancelled.
    /// </summary>
    Lancir.App.ViewModels.GameDetailsViewModel? PromptNewGame();

    /// <summary>Shows the "import from Steam" window and returns when the user closes it.</summary>
    void ShowSteamImport(Lancir.App.ViewModels.SteamImportViewModel viewModel);
}

public sealed record MatchFieldChoices(
    bool UseNewName,
    bool UseNewCover,
    bool UseNewBackground,
    bool UseNewDescription,
    bool UseNewGenres,
    bool UseNewCommunityRating,
    bool UseNewReleaseDate);
