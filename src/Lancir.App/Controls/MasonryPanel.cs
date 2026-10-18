using System.Windows;
using System.Windows.Controls;
using Lancir.App.ViewModels;

namespace Lancir.App.Controls;

/// <summary>
/// Lays out the details panel cards in columns (1 to 10; <see cref="Columns"/> 0 picks the number from the width). Every card spans a
/// number of columns (from 1 up to all of them, see <see cref="DetailCardViewModel.EffectiveSpan"/>) and is placed, in order, at the
/// spot where it sits highest: the columns it would cover are compared and it goes under the tallest of them, at the position with the
/// lowest top. A Left card is held to the first column(s), a Right card to the last. Cards that are not shown (hidden for this game)
/// take no room. The DataContext of each child must be a <see cref="DetailCardViewModel"/>.
/// </summary>
public sealed class MasonryPanel : Panel
{
    /// <summary>The gap between cards, in pixels.</summary>
    public const double Spacing = 12;

    /// <summary>The height of one row of a card with a fixed height, in pixels.</summary>
    public const double RowUnit = 90;

    /// <summary>The width of one column right now, in pixels (the card resize handle snaps to it).</summary>
    public double ColumnWidthPx => _columnWidth;

    private const double MinColumnWidth = 220;
    private const double MinChosenColumnWidth = 110; // a column count picked by hand is honoured down to a narrow card
    private const int MaxColumns = 10;

    public static readonly DependencyProperty ColumnsProperty = DependencyProperty.Register(
        nameof(Columns), typeof(int), typeof(MasonryPanel),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    /// <summary>The number of columns (1 to 10), or 0 to choose from the width (1 narrow, up to 4 wide).</summary>
    public int Columns
    {
        get => (int)GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    public static readonly DependencyProperty ActualColumnsProperty = DependencyProperty.Register(
        nameof(ActualColumns), typeof(int), typeof(MasonryPanel),
        new FrameworkPropertyMetadata(2, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    /// <summary>The number of columns in use right now (reported back so the cards can offer widths up to it).</summary>
    public int ActualColumns
    {
        get => (int)GetValue(ActualColumnsProperty);
        set => SetValue(ActualColumnsProperty, value);
    }

    private Rect[] _rects = [];
    private int _columnCount = 2;
    private double _columnWidth;
    private double _totalHeight;

    /// <summary>How many columns fit: the asked-for number (or one picked from the width), never so many that a column gets too narrow.</summary>
    private int ColumnCountFor(double width)
    {
        var wanted = Columns > 0
            ? Columns
            : width < 520 ? 1 : width < 900 ? 2 : width < 1250 ? 3 : 4;
        var fits = Math.Max(1, (int)Math.Floor((width + Spacing) / ((Columns > 0 ? MinChosenColumnWidth : MinColumnWidth) + Spacing)));
        return Math.Clamp(Math.Min(wanted, fits), 1, MaxColumns);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 600 : availableSize.Width;
        var n = ColumnCountFor(width);
        _columnCount = n;
        _columnWidth = (width - Spacing * (n - 1)) / n;
        if (ActualColumns != n)
        {
            // reported after the layout pass: the cards' width labels change with it
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () => SetCurrentValue(ActualColumnsProperty, n));
        }

        _rects = new Rect[InternalChildren.Count];
        var heights = new double[n]; // the bottom (with spacing) of each column so far

        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];
            if (child is FrameworkElement { DataContext: DetailCardViewModel { IsShown: false } })
            {
                continue;
            }

            var card = (child as FrameworkElement)?.DataContext as DetailCardViewModel;
            var span = Math.Clamp(card?.EffectiveSpan(n) ?? 1, 1, n);
            var childWidth = span * _columnWidth + (span - 1) * Spacing;
            child.Measure(new Size(childWidth, double.PositiveInfinity));
            var height = child.DesiredSize.Height;

            // where may the card start?
            var first = 0;
            var last = n - span;
            if (card?.Size == CardSize.Left)
            {
                last = 0;
            }
            else if (card?.Size == CardSize.Right)
            {
                first = last;
            }

            var bestColumn = first;
            var bestTop = double.MaxValue;
            for (var c = first; c <= last; c++)
            {
                var top = 0.0;
                for (var k = c; k < c + span; k++)
                {
                    top = Math.Max(top, heights[k]);
                }

                if (top < bestTop - 0.5)
                {
                    bestTop = top;
                    bestColumn = c;
                }
            }

            _rects[i] = new Rect(bestColumn * (_columnWidth + Spacing), bestTop, childWidth, height);
            for (var k = bestColumn; k < bestColumn + span; k++)
            {
                heights[k] = bestTop + height + Spacing;
            }
        }

        _totalHeight = Math.Max(0, heights.Max() - Spacing);
        return new Size(width, _totalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        for (var i = 0; i < InternalChildren.Count && i < _rects.Length; i++)
        {
            var shown = InternalChildren[i] is not FrameworkElement { DataContext: DetailCardViewModel { IsShown: false } };
            InternalChildren[i].Arrange(shown ? _rects[i] : new Rect(0, 0, 0, 0)); // Rect.Empty has negative sizes, which Arrange rejects
        }

        return finalSize;
    }

    /// <summary>
    /// For a card dropped on empty space: the column under <paramref name="point"/> (the first column is Left, the last is Right,
    /// any between flows) and the index of the last card that starts above the point, so the dropped card goes after it.
    /// </summary>
    public bool TryGetColumnAt(Point point, out CardSize side, out int lastIndexInSegment)
    {
        side = CardSize.Auto;
        lastIndexInSegment = -1;
        if (_columnWidth <= 0 || point.Y < 0)
        {
            return false;
        }

        var column = (int)Math.Clamp(point.X / (_columnWidth + Spacing), 0, _columnCount - 1);
        side = column == 0 ? CardSize.Left : column == _columnCount - 1 ? CardSize.Right : CardSize.Auto;
        for (var i = 0; i < _rects.Length && i < InternalChildren.Count; i++)
        {
            if (InternalChildren[i] is FrameworkElement { DataContext: DetailCardViewModel { IsShown: true } } && _rects[i].Top <= point.Y)
            {
                lastIndexInSegment = i;
            }
        }

        return true;
    }
}
