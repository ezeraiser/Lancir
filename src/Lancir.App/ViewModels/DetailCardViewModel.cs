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
    public DetailCardViewModel(string id, string title, GamePanelViewModel panel, CardSize size, bool hidden = false, int span = 0, int rows = 0)
    {
        Id = id;
        Title = title;
        Panel = panel;
        _size = size;
        _isHidden = hidden;
        _span = span;
        _rows = rows;
    }

    public string Id { get; }

    public string Title { get; }

    /// <summary>The panel view model; card content templates bind through it (e.g. Panel.Game).</summary>
    public GamePanelViewModel Panel { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SpanText))]
    private CardSize _size;

    /// <summary>How many columns the card spans; 0 means "by size" (Full = every column, otherwise one).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SpanText))]
    private int _span;

    /// <summary>Height in rows; 0 = as tall as the content. A set number gives the card a fixed height and the content scrolls inside it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CardHeight))]
    [NotifyPropertyChangedFor(nameof(HasFixedHeight))]
    private int _rows;

    public bool HasFixedHeight => Rows > 0;

    /// <summary>The card's pixel height for its row count, or NaN (automatic) when it follows its content.</summary>
    public double CardHeight => Rows > 0 ? Rows * Controls.MasonryPanel.RowUnit + (Rows - 1) * Controls.MasonryPanel.Spacing : double.NaN;

    /// <summary>The columns the card really takes with <paramref name="columns"/> columns on screen.</summary>
    public int EffectiveSpan(int columns) => Span > 0 ? Math.Min(Span, columns) : Size == CardSize.Full ? columns : 1;

    /// <summary>"2/4": the card's width in columns out of the columns on screen (the layout editor's label).</summary>
    public string SpanText
    {
        get
        {
            var columns = Math.Max(1, Panel.ActualCardColumns);
            return $"{EffectiveSpan(columns)}/{columns}";
        }
    }

    internal void RefreshSpanText() => OnPropertyChanged(nameof(SpanText));

    /// <summary>The card is switched off for this game: it takes no room, except in edit mode where it shows dimmed so it can be switched back on.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShown))]
    private bool _isHidden;

    /// <summary>Whether the card takes part in the layout right now.</summary>
    public bool IsShown => !IsHidden || Panel.IsEditingLayout;

    internal void RefreshShown() => OnPropertyChanged(nameof(IsShown));

    /// <summary>True while another card is being dragged over this one (drop indicator).</summary>
    [ObservableProperty]
    private bool _isDropTarget;
}
