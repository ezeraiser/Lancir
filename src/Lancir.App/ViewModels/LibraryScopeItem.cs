using CommunityToolkit.Mvvm.ComponentModel;
using Lancir.Core.Models;
using Wpf.Ui.Controls;

namespace Lancir.App.ViewModels;

public enum LibraryScopeKind
{
    /// <summary>Every game.</summary>
    All,

    /// <summary>The games with one status (Oynuyorum, Bitirdim, ...).</summary>
    Status,

    /// <summary>The games marked as favorites.</summary>
    Favorites,

    /// <summary>A library the user made.</summary>
    Custom,
}

/// <summary>One entry of the library list in the left strip: all games, favorites, a status, or a library of the user's own.</summary>
public sealed partial class LibraryScopeItem(
    LibraryScopeKind kind, string title, SymbolRegular symbol, GameStatus? status = null, Guid? customId = null, string? iconPath = null)
    : ObservableObject
{
    public LibraryScopeKind Kind { get; } = kind;

    public GameStatus? Status { get; } = status;

    public Guid? CustomId { get; } = customId;

    public bool IsCustom => Kind == LibraryScopeKind.Custom;

    /// <summary>Identifies the entry in the saved order of the strip.</summary>
    public string Key => Kind switch
    {
        LibraryScopeKind.All => "all",
        LibraryScopeKind.Favorites => "favorites",
        LibraryScopeKind.Status => $"status:{Status}",
        _ => $"custom:{CustomId}",
    };

    [ObservableProperty]
    private string _title = title;

    [ObservableProperty]
    private SymbolRegular _symbol = symbol;

    /// <summary>A picture the user loaded for the icon; when set it is shown instead of the symbol.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPicture))]
    private string? _iconPath = iconPath;

    public bool HasPicture => !string.IsNullOrEmpty(IconPath);

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>The same entry after the list was rebuilt.</summary>
    public bool Matches(LibraryScopeItem other) => Key == other.Key;
}
