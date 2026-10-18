using System.Windows;
using System.Windows.Controls;
using Lancir.App.ViewModels;

namespace Lancir.App.Controls;

/// <summary>
/// Lays out the details panel cards in two columns. Consecutive half-width cards form a
/// segment whose left and right columns are stacked independently: an Auto card goes into the
/// shorter column, Left/Right cards are pinned. Full cards cut the flow and span both columns.
/// The DataContext of each child must be a <see cref="DetailCardViewModel"/>, which carries its size.
/// </summary>
public sealed class MasonryPanel : Panel
{
    private const double Spacing = 12;
    private const double SingleColumnBelow = 520;

    private Rect[] _rects = [];
    private readonly List<Segment> _segments = [];

    private readonly record struct Segment(double Top, double Bottom, int FirstIndex, int LastIndex);

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 600 : availableSize.Width;
        var singleColumn = width < SingleColumnBelow;
        var columnWidth = (width - Spacing) / 2;

        _rects = new Rect[InternalChildren.Count];
        _segments.Clear();

        var y = 0.0;
        var i = 0;
        while (i < InternalChildren.Count)
        {
            if (singleColumn || SizeOf(InternalChildren[i]) == CardSize.Full)
            {
                InternalChildren[i].Measure(new Size(width, double.PositiveInfinity));
                var height = InternalChildren[i].DesiredSize.Height;
                _rects[i] = new Rect(0, y, width, height);
                y += height + Spacing;
                i++;
                continue;
            }

            var first = i;
            double leftHeight = 0, rightHeight = 0;
            while (i < InternalChildren.Count && SizeOf(InternalChildren[i]) != CardSize.Full)
            {
                var child = InternalChildren[i];
                child.Measure(new Size(columnWidth, double.PositiveInfinity));
                var height = child.DesiredSize.Height;

                var size = SizeOf(child);
                var left = size == CardSize.Left || (size == CardSize.Auto && leftHeight <= rightHeight);
                if (left)
                {
                    _rects[i] = new Rect(0, y + leftHeight, columnWidth, height);
                    leftHeight += height + Spacing;
                }
                else
                {
                    _rects[i] = new Rect(columnWidth + Spacing, y + rightHeight, columnWidth, height);
                    rightHeight += height + Spacing;
                }

                i++;
            }

            var segmentHeight = Math.Max(Math.Max(leftHeight, rightHeight) - Spacing, 0);
            _segments.Add(new Segment(y, y + segmentHeight, first, i - 1));
            y += segmentHeight + Spacing;
        }

        return new Size(width, Math.Max(0, y - Spacing));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        for (var i = 0; i < InternalChildren.Count && i < _rects.Length; i++)
        {
            InternalChildren[i].Arrange(_rects[i]);
        }

        return finalSize;
    }

    /// <summary>
    /// Finds the half-width segment under <paramref name="point"/> (including the empty space below a
    /// short column) and which column the point is in. Used for dropping a card into a column.
    /// </summary>
    public bool TryGetColumnAt(Point point, out CardSize side, out int lastIndexInSegment)
    {
        side = CardSize.Auto;
        lastIndexInSegment = -1;

        foreach (var segment in _segments)
        {
            if (point.Y >= segment.Top && point.Y <= segment.Bottom)
            {
                side = point.X < ActualWidth / 2 ? CardSize.Left : CardSize.Right;
                lastIndexInSegment = segment.LastIndex;
                return true;
            }
        }

        return false;
    }

    private static CardSize SizeOf(UIElement child) =>
        child is FrameworkElement { DataContext: DetailCardViewModel card } ? card.Size : CardSize.Auto;
}
