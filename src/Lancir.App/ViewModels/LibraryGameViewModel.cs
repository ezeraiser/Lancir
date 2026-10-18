using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Lancir.App.Services;
using Lancir.Core.Models;
using Lancir.Core.Services;

namespace Lancir.App.ViewModels;

public sealed partial class LibraryGameViewModel : ObservableObject
{
    public LibraryGameViewModel(LibraryGame game)
    {
        Game = game;
        _displayName = game.DisplayName;
        _playtimeSeconds = game.PlaytimeSeconds;
    }

    public LibraryGame Game { get; }

    public int Id => Game.Id;

    [ObservableProperty]
    private string _displayName;

    public string? CoverImagePath => Game.CoverImagePath;

    public string? BackgroundImagePath => Game.BackgroundImagePath;

    public string? Description => Game.Description;

    public string? Genres => Game.Genres;

    public double? CommunityRating => Game.CommunityRating;

    public string? DescriptionRich => Game.DescriptionRich;

    public string? Developers => Game.Developers;

    public string? Publishers => Game.Publishers;

    public string? Modes => Game.Modes;

    public string? Languages => Game.Languages;

    public string? AgeRating => Game.AgeRating;

    public string? Website => Game.Website;

    public string? TrailerUrl => Game.TrailerUrl;

    public string? TrailerThumbnailUrl => Game.TrailerThumbnailUrl;

    public IReadOnlyList<string> Screenshots => (Game.ScreenshotUrls ?? "")
        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public bool IsFavorite
    {
        get => Game.IsFavorite;
        set
        {
            if (Game.IsFavorite != value)
            {
                Game.IsFavorite = value;
                OnPropertyChanged();
            }
        }
    }

    public GameStatus Status
    {
        get => Game.Status;
        set
        {
            if (Game.Status != value)
            {
                Game.Status = value;
                OnPropertyChanged();
            }
        }
    }

    public int UserRating
    {
        get => Game.UserRating;
        set
        {
            if (Game.UserRating != value)
            {
                Game.UserRating = value;
                OnPropertyChanged();
            }
        }
    }

    public string? Notes
    {
        get => Game.Notes;
        set
        {
            if (Game.Notes != value)
            {
                Game.Notes = value;
                OnPropertyChanged();
            }
        }
    }

    public string? LaunchArguments
    {
        get => Game.LaunchArguments;
        set
        {
            if (Game.LaunchArguments != value)
            {
                Game.LaunchArguments = value;
                OnPropertyChanged();
            }
        }
    }

    public string? WorkingDirectory
    {
        get => Game.WorkingDirectory;
        set
        {
            if (Game.WorkingDirectory != value)
            {
                Game.WorkingDirectory = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>The user's own tags, parsed from the comma-separated column.</summary>
    public IReadOnlyList<string> UserTags => (Game.UserTags ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public void SetUserTags(IEnumerable<string> tags)
    {
        Game.UserTags = string.Join(",", tags);
        OnPropertyChanged(nameof(UserTags));
    }

    /// <summary>True while the game's executable is running (set by the playtime tracker).</summary>
    [ObservableProperty]
    private bool _isRunning;

    public bool HasExecutable => !string.IsNullOrWhiteSpace(ExecutablePath) && File.Exists(ExecutablePath);

    /// <summary>The play button works with an existing exe, or with the Steam protocol when that is switched on.</summary>
    public bool CanLaunch => HasExecutable || GameLaunchService.BuildLaunchUri(Game) is not null;

    /// <summary>The game is started by address (or has no exe) but nothing tells the tracker what to watch.</summary>
    public bool LacksPlaytimeTracking => GameLaunchService.LacksPlaytimeTracking(
        GameLaunchService.BuildLaunchUri(Game) is not null, Game.ExecutablePath, Game.TrackedProcessName);

    public bool RunAsAdmin
    {
        get => Game.RunAsAdmin;
        set
        {
            if (Game.RunAsAdmin != value)
            {
                Game.RunAsAdmin = value;
                OnPropertyChanged();
            }
        }
    }

    public string? LaunchUri
    {
        get => Game.LaunchUri;
        set
        {
            if (Game.LaunchUri != value)
            {
                Game.LaunchUri = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanLaunch));
                OnPropertyChanged(nameof(LacksPlaytimeTracking));
            }
        }
    }

    public bool LaunchViaSteam
    {
        get => Game.LaunchViaSteam;
        set
        {
            if (Game.LaunchViaSteam != value)
            {
                Game.LaunchViaSteam = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanLaunch));
                OnPropertyChanged(nameof(LacksPlaytimeTracking));
            }
        }
    }

    public int? SteamAppId
    {
        get => Game.SteamAppId;
        set
        {
            if (Game.SteamAppId != value)
            {
                Game.SteamAppId = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanLaunch));
                OnPropertyChanged(nameof(LacksPlaytimeTracking));
            }
        }
    }

    public string? TrackedProcessName
    {
        get => Game.TrackedProcessName;
        set
        {
            if (Game.TrackedProcessName != value)
            {
                Game.TrackedProcessName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LacksPlaytimeTracking));
            }
        }
    }

    public string TotalPlaytimeText => TimeText.Duration(PlaytimeSeconds);

    public string LastPlayedText => Game.LastPlayedUtc is { } last ? TimeText.Relative(last) : "—";

    public string LastPlayedTooltip => Game.LastPlayedUtc is { } last
        ? $"{TimeText.DateTimeText(last)}"
        : Loc.T("Henüz oynanmadı");

    public string AveragePlaytimeText => Game.PlayCount > 0
        ? TimeText.Duration(PlaytimeSeconds / Game.PlayCount)
        : "—";

    public string PlayCountTooltip => Game.PlayCount > 0 ? Loc.F("{0} oturum", Game.PlayCount) : Loc.T("Henüz oturum kaydı yok");

    public string AddedText => TimeText.Date(Game.AddedAtUtc);

    public string AddedTooltip => Loc.F("Kütüphanede {0} gündür", Math.Max(0, (DateTime.UtcNow - Game.AddedAtUtc).Days));

    /// <summary>Re-reads the play statistics after the tracker updated the game.</summary>
    public void RefreshPlayStats()
    {
        OnPropertyChanged(nameof(TotalPlaytimeText));
        OnPropertyChanged(nameof(LastPlayedText));
        OnPropertyChanged(nameof(LastPlayedTooltip));
        OnPropertyChanged(nameof(AveragePlaytimeText));
        OnPropertyChanged(nameof(PlayCountTooltip));
        OnPropertyChanged(nameof(PlaytimeText));
    }

    public DateTime? ReleaseDateUtc => Game.ReleaseDateUtc;

    public string? ExecutablePath => Game.ExecutablePath;

    public string FolderPath => Game.FolderPath;

    public bool HasCover => !string.IsNullOrWhiteSpace(CoverImagePath) && File.Exists(CoverImagePath);

    public bool HasBackground => !string.IsNullOrWhiteSpace(BackgroundImagePath) && File.Exists(BackgroundImagePath);

    public bool IsMatched => Game.SteamGridDbId is not null || Game.IgdbId is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanMatch))]
    private bool _isMatching;

    public bool CanMatch => !IsMatching;

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>True while another game is being dragged over this one (drop indicator).</summary>
    [ObservableProperty]
    private bool _isDropTarget;

    /// <summary>Image used behind the details panel: the background art, else the cover.</summary>
    public string? PanelBackgroundPath => HasBackground ? BackgroundImagePath : CoverImagePath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlaytimeText))]
    [NotifyPropertyChangedFor(nameof(TotalPlaytimeText))]
    [NotifyPropertyChangedFor(nameof(AveragePlaytimeText))]
    private long _playtimeSeconds;

    /// <summary>"Oynanma: 3 sa 20 dk", or "Henüz oynanmadı" when there's no recorded time.</summary>
    public string PlaytimeText => PlaytimeSeconds switch
    {
        <= 0 => Loc.T("Henüz oynanmadı"),
        < 60 => Loc.T("Oynanma: 1 dk'dan az"),
        var s when s < 3600 => Loc.F("Oynanma: {0} dk", s / 60),
        var s => Loc.F("Oynanma: {0} sa {1} dk", s / 3600, s % 3600 / 60),
    };
}
