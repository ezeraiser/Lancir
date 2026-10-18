using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Lancir.App.Controls;

/// <summary>
/// Lays the sections of a Settings page out in as many columns as the width allows (each at least 440 wide, at most 640): a section is a
/// small heading (a bold 13 pt TextBlock) together with the card that follows it, anything else is a section of its own. Each section goes
/// under the shortest column so the columns stay about equally tall.
/// </summary>
public sealed class ColumnFlowPanel : Panel
{
    private const double MinColumnWidth = 440;
    private const double MaxColumnWidth = 640;
    private const double ColumnGap = 28;

    private readonly List<(int First, int Last, int Column, double Top)> _groups = [];
    private double _columnWidth;

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? MinColumnWidth : availableSize.Width;
        var columns = Math.Clamp((int)Math.Floor((width + ColumnGap) / (MinColumnWidth + ColumnGap)), 1, 4);
        _columnWidth = Math.Min(MaxColumnWidth, (width - ColumnGap * (columns - 1)) / columns);

        _groups.Clear();
        var heights = new double[columns];
        var i = 0;
        while (i < InternalChildren.Count)
        {
            var first = i;
            // a section runs from its heading up to the next heading: the card, its description, its Kaydet button
            var last = i;
            while (last + 1 < InternalChildren.Count && !IsHeading(InternalChildren[last + 1]))
            {
                last++;
            }

            var column = 0;
            for (var c = 1; c < columns; c++)
            {
                if (heights[c] < heights[column])
                {
                    column = c;
                }
            }

            var top = heights[column];
            var groupHeight = 0.0;
            for (var k = first; k <= last; k++)
            {
                InternalChildren[k].Measure(new Size(_columnWidth, double.PositiveInfinity));
                groupHeight += InternalChildren[k].DesiredSize.Height;
            }

            _groups.Add((first, last, column, top));
            heights[column] += groupHeight;
            i = last + 1;
        }

        return new Size(columns * _columnWidth + ColumnGap * (columns - 1), heights.Max());
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var (first, last, column, top) in _groups)
        {
            var x = column * (_columnWidth + ColumnGap);
            var y = top;
            for (var k = first; k <= last; k++)
            {
                var height = InternalChildren[k].DesiredSize.Height;
                InternalChildren[k].Arrange(new Rect(x, y, _columnWidth, height));
                y += height;
            }
        }

        return finalSize;
    }

    private static bool IsHeading(UIElement child) =>
        child is TextBlock { FontSize: 13 } text && text.FontWeight == FontWeights.SemiBold;
}
