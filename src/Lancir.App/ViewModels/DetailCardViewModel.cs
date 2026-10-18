using CommunityToolkit.Mvvm.ComponentModel;

namespace Lancir.App.ViewModels;

/// <summary>How a card of the game details panel is laid out.</summary>
public enum CardSize
{
    /// <summary>Spans both columns.</summary>
    Full,

    /// <summary>Half width; goes into whichever column is currently shorter.</summary>
    Auto,

    /// <summary>Half width, pinned to the left column.</summary>
    Left,

    /// <summary>Half width, pinned to the right column.</summary>
    Right,
}

/// <summary>One card (Etkinlik, Künye, Notlarım, ...) in the details panel.</summary>
public sealed partial class DetailCardViewModel : ObservableObject
{
    public DetailCardViewModel(string id, string title, GamePanelViewModel panel, CardSize size)
    {
        Id = id;
        Title = title;
        Panel = panel;
        _size = size;
    }

    public string Id { get; }

    public string Title { get; }

    /// <summary>The panel view model; card content templates bind through it (e.g. Panel.Game).</summary>
    public GamePanelViewModel Panel { get; }

    [ObservableProperty]
    private CardSize _size;

    /// <summary>True while another card is being dragged over this one (drop indicator).</summary>
    [ObservableProperty]
    private bool _isDropTarget;
}
