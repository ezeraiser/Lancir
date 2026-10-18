using CommunityToolkit.Mvvm.ComponentModel;

namespace Lancir.App.ViewModels;

/// <summary>A size choice for a card in the layout editor of Settings.</summary>
public sealed record CardSizeOption(CardSize Size, int Span, string Label);

/// <summary>One card in the default layout editor of Settings (order is the row order).</summary>
public sealed partial class CardLayoutRow(string id, string title, CardSizeOption size, bool visible = true) : ObservableObject
{
    public string Id { get; } = id;

    public string Title { get; } = title;

    [ObservableProperty]
    private CardSizeOption _selectedSize = size;

    [ObservableProperty]
    private bool _isVisible = visible;
}
