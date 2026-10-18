using System.Windows;
using System.Windows.Controls;

namespace Lancir.App.Controls;

/// <summary>Implemented by items of <see cref="FlowTwoColumnPanel"/> to ask for the full width.</summary>
public interface ISpanItem
{
    /// <summary>1 = one column, 2 = both columns.</summary>
    int Span { get; }
}

/// <summary>
/// Flows children left to right, two per row; a child whose DataContext asks for Span 2 takes a
/// whole row. Used for the Künye facts, where long values need the full width.
/// </summary>
public sealed class FlowTwoColumnPanel : Panel
{
    private const double ColumnGap = 18;
    private const double RowGap = 10;

    private Rect[] _rects = [];

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 400 : availableSize.Width;
        var columnWidth = (width - ColumnGap) / 2;
        _rects = new Rect[InternalChildren.Count];

        var y = 0.0;
        var column = 0;
        var rowHeight = 0.0;

        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];
            var full = child is FrameworkElement { DataContext: ISpanItem { Span: 2 } };

            if (full)
            {
                if (column == 1)
                {
                    y += rowHeight + RowGap;
                    column = 0;
                    rowHeight = 0;
                }

                child.Measure(new Size(width, double.PositiveInfinity));
                _rects[i] = new Rect(0, y, width, child.DesiredSize.Height);
                y += child.DesiredSize.Height + RowGap;
                continue;
            }

            child.Measure(new Size(columnWidth, double.PositiveInfinity));
            _rects[i] = new Rect(column == 0 ? 0 : columnWidth + ColumnGap, y, columnWidth, child.DesiredSize.Height);
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);

            if (column == 0)
            {
                column = 1;
            }
            else
            {
                y += rowHeight + RowGap;
                column = 0;
                rowHeight = 0;
            }
        }

        if (column == 1)
        {
            y += rowHeight + RowGap;
        }

        return new Size(width, Math.Max(0, y - RowGap));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        for (var i = 0; i < InternalChildren.Count && i < _rects.Length; i++)
        {
            InternalChildren[i].Arrange(_rects[i]);
        }

        return finalSize;
    }
}
