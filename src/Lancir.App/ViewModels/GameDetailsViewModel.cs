using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Lancir.Core.Models;
using Lancir.Core.Services;

namespace Lancir.App.ViewModels;

public sealed partial class GameDetailsViewModel : ObservableObject
{
    public GameDetailsViewModel(LibraryGame game)
    {
        Game = game;
        _displayName = game.DisplayName;
        _description = game.Description;
        _genres = game.Genres;
        _executablePath = game.ExecutablePath;
        _steamAppIdText = game.SteamAppId?.ToString(CultureInfo.InvariantCulture) ?? "";
        _launchViaSteam = game.LaunchViaSteam;
        _launchArguments = game.LaunchArguments;
        _workingDirectory = game.WorkingDirectory;
        _trackedProcessName = game.TrackedProcessName;
        _runAsAdmin = game.RunAsAdmin;
        _launchUri = game.LaunchUri;
    }

    public LibraryGame Game { get; }

    public string FolderPath => Game.FolderPath;

    public double? CommunityRating => Game.CommunityRating;

    public DateTime? ReleaseDateUtc => Game.ReleaseDateUtc;

    [ObservableProperty]
    private string _displayName;

    [ObservableProperty]
    private string? _description;

    [ObservableProperty]
    private string? _genres;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LacksPlaytimeTracking))]
    private string? _executablePath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSteamAppIdInvalid))]
    [NotifyPropertyChangedFor(nameof(LacksPlaytimeTracking))]
    [NotifyPropertyChangedFor(nameof(CanFindSteamFolder))]
    private string _steamAppIdText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LacksPlaytimeTracking))]
    private bool _launchViaSteam;

    [ObservableProperty]
    private string? _launchArguments;

    [ObservableProperty]
    private string? _workingDirectory;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LacksPlaytimeTracking))]
    private string? _trackedProcessName;

    [ObservableProperty]
    private bool _runAsAdmin;

    /// <summary>Result of the last "find the exe in Steam's folder" attempt; null = nothing to show.</summary>
    [ObservableProperty]
    private string? _steamFindMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLaunchUriInvalid))]
    [NotifyPropertyChangedFor(nameof(LacksPlaytimeTracking))]
    private string? _launchUri;

    public GameLaunchSettings LaunchSettings =>
        new(LaunchViaSteam, LaunchArguments, WorkingDirectory, TrackedProcessName, RunAsAdmin, LaunchUri);

    /// <summary>The entered Steam app id; null when the field is empty or not a positive number.</summary>
    public int? SteamAppId =>
        int.TryParse(SteamAppIdText?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 ? id : null;

    /// <summary>True when something is typed that is not a valid app id (empty is fine: no Steam id).</summary>
    public bool IsSteamAppIdInvalid => !string.IsNullOrWhiteSpace(SteamAppIdText) && SteamAppId is null;

    /// <summary>True when something is typed that is not a usable launch address (empty is fine).</summary>
    public bool IsLaunchUriInvalid =>
        !string.IsNullOrWhiteSpace(LaunchUri) && !GameLaunchService.IsValidLaunchUri(LaunchUri);

    /// <summary>The "find the exe in Steam's install folder" button needs a valid Steam id.</summary>
    public bool CanFindSteamFolder => SteamAppId is not null;

    /// <summary>The game will be started by address but nothing tells the tracker what to watch.</summary>
    public bool LacksPlaytimeTracking => GameLaunchService.LacksPlaytimeTracking(
        GameLaunchService.IsValidLaunchUri(LaunchUri) || (LaunchViaSteam && SteamAppId is not null),
        ExecutablePath,
        TrackedProcessName);
}
