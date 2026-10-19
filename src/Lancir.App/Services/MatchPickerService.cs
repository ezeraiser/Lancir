using Lancir.App.ViewModels;
using Lancir.App.Views;
using Lancir.Core.Models;
using Lancir.Core.Services;

namespace Lancir.App.Services;

public sealed class MatchPickerService : IMatchPickerService
{
    private readonly LibraryService _libraryService;

    public MatchPickerService(LibraryService libraryService)
    {
        _libraryService = libraryService;
    }

    public MatchCandidate? Pick(string gameDisplayName, IReadOnlyList<MatchCandidate> candidates)
    {
        var viewModel = new MatchPickerViewModel(gameDisplayName, candidates);
        var window = new MatchPickerWindow(viewModel)
        {
            Owner = System.Windows.Application.Current.MainWindow,
        };

        var accepted = window.ShowDialog();
        return accepted == true ? window.Result : null;
    }

    public MatchFieldChoices? Compare(LibraryGame currentGame, MatchPreview preview)
    {
        var viewModel = new MatchCompareViewModel(
            currentGame.DisplayName,
            MediaPaths.Resolve(currentGame.CoverImagePath),
            MediaPaths.Resolve(currentGame.BackgroundImagePath),
            currentGame.Description,
            currentGame.Genres,
            currentGame.CommunityRating,
            currentGame.ReleaseDateUtc,
            preview);
        var window = new MatchCompareWindow(viewModel)
        {
            Owner = System.Windows.Application.Current.MainWindow,
        };

        var accepted = window.ShowDialog();
        if (accepted != true || !window.Applied)
        {
            return null;
        }

        return new MatchFieldChoices(
            viewModel.NameField.UseNew,
            viewModel.CoverField.UseNew,
            viewModel.BackgroundField.UseNew,
            viewModel.DescriptionField.UseNew,
            viewModel.GenresField.UseNew,
            viewModel.CommunityRatingField.UseNew,
            viewModel.ReleaseDateField.UseNew);
    }

    public void ShowSteamImport(SteamImportViewModel viewModel)
    {
        var window = new SteamImportWindow(viewModel)
        {
            Owner = System.Windows.Application.Current.MainWindow,
        };

        window.ShowDialog();
    }

    public GameDetailsViewModel? PromptNewGame()
    {
        var viewModel = new GameDetailsViewModel(new LibraryGame { FolderPath = "", DisplayName = "" });
        var window = new GameDetailsWindow(viewModel)
        {
            Owner = System.Windows.Application.Current.MainWindow,
        };

        var accepted = window.ShowDialog();
        return accepted == true && window.Saved ? viewModel : null;
    }

    public async Task<bool> EditDetailsAsync(LibraryGame game)
    {
        var viewModel = new GameDetailsViewModel(game);
        var window = new GameDetailsWindow(viewModel)
        {
            Owner = System.Windows.Application.Current.MainWindow,
        };

        var accepted = window.ShowDialog();
        if (accepted != true || !window.Saved)
        {
            return false;
        }

        // awaited, not waited for: blocking the UI thread on an async call can deadlock as soon as one step really goes async
        await _libraryService.UpdateGameDetailsAsync(
            game.Id, viewModel.DisplayName, viewModel.Description, viewModel.Genres, viewModel.ExecutablePath,
            viewModel.SteamAppId, viewModel.LaunchSettings);

        return true;
    }
}
